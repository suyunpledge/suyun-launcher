// P1: Fabric dependency resolution - typed version constraints, three-state verdicts, nested jars and loader dependency overrides.
// Design constraint: parse failures always return Unknown, never assumed compatible; mod jars are never executed or unpacked。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SuyunCraft
{
    enum DependencyVerdict { Satisfied, Unsatisfied, Unknown }

    /// <summary>Bounded Fabric version: dotted-numeric core + optional prerelease suffix. Unparseable becomes Valid=false。</summary>
    sealed class FabricVersion
    {
        public readonly int[] Numbers;
        public readonly string Prerelease;
        public readonly string Raw;
        public bool Valid { get { return Numbers != null; } }

        FabricVersion(int[] numbers, string prerelease, string raw)
        { Numbers = numbers; Prerelease = prerelease; Raw = raw; }

        public static FabricVersion Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new FabricVersion(null, null, text == null ? "" : text);
            string core = text.Trim();
            string pre = null;
            int dash = core.IndexOf('-');
            if (dash >= 0) { pre = core.Substring(dash + 1).Trim(); core = core.Substring(0, dash); }
            if (core.Length == 0) return new FabricVersion(null, null, text);
            string[] parts = core.Split('.');
            if (parts.Length == 0 || parts.Length > 4) return new FabricVersion(null, null, text);
            int[] nums = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int v;
                if (p.Length == 0 || p.Length > 9 || !int.TryParse(p, out v) || v < 0)
                    return new FabricVersion(null, null, text);
                nums[i] = v;
            }
            return new FabricVersion(nums, pre, text);
        }

        // Semantic compare: segment-wise (missing pads 0); within the same coreRelease > prerelease; two prereleases compare ordinally.
        // either side invalid returns null (callers must treat null as Unknown, never equal）。
        public static int? Compare(FabricVersion a, FabricVersion b)
        {
            if (a == null || b == null || !a.Valid || !b.Valid) return null;
            int n = Math.Max(a.Numbers.Length, b.Numbers.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Numbers.Length ? a.Numbers[i] : 0;
                int y = i < b.Numbers.Length ? b.Numbers[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            bool ap = !string.IsNullOrEmpty(a.Prerelease), bp = !string.IsNullOrEmpty(b.Prerelease);
            if (!ap && !bp) return 0;
            if (ap && !bp) return -1;
            if (!ap && bp) return 1;
            return string.CompareOrdinal(a.Prerelease, b.Prerelease);
        }

        public override string ToString() { return Raw; }
    }

    /// <summary>A single comparison term, e.g. ">=1.2"、"1.16.x"、"~0.11"、"^1.2.3"、exact "1.0.0"、"*"。</summary>
    sealed class FabricTerm
    {
        public string Op = "";            // "" | "=" | ">" | ">=" | "<" | "<=" | "~" | "^" | "*"（* Any means）
        public bool Any;
        public FabricVersion Version;     // Op   * null when
        public bool WildcardTrailing;     // trailing x/X/*（ like 1.16.x); Op must be ""  or  "="
        public bool Valid = true;

        public DependencyVerdict Evaluate(FabricVersion candidate)
        {
            if (Any) return DependencyVerdict.Satisfied;
            if (!Valid) return DependencyVerdict.Unknown;
            if (WildcardTrailing)
            {
                if (Version == null || !Version.Valid || candidate == null || !candidate.Valid) return DependencyVerdict.Unknown;
                int[] lower = Version.Numbers;
                // upper bound = lower non-wildcard prefix +1（1.16.x → <1.17；1.x → <2）
                int[] upper = new int[lower.Length];
                for (int i = 0; i < lower.Length; i++) upper[i] = lower[i];
                upper[upper.Length - 1] = upper[upper.Length - 1] + 1;
                bool? lo = CompareGe(candidate, FabricVersion.Parse(Join(lower)));
                bool? hi = CompareLt(candidate, FabricVersion.Parse(Join(upper)));
                if (lo == null || hi == null) return DependencyVerdict.Unknown;
                return (lo.Value && hi.Value) ? DependencyVerdict.Satisfied : DependencyVerdict.Unsatisfied;
            }
            if (Version == null || !Version.Valid) return DependencyVerdict.Unknown;
            if (candidate == null || !candidate.Valid) return DependencyVerdict.Unknown;
            int? cmp = FabricVersion.Compare(candidate, Version);
            if (cmp == null) return DependencyVerdict.Unknown;
            int c = cmp.Value;
            bool ok;
            switch (Op)
            {
                case "": case "=": ok = c == 0; break;
                case ">": ok = c > 0; break;
                case ">=": ok = c >= 0; break;
                case "<": ok = c < 0; break;
                case "<=": ok = c <= 0; break;
                case "~":
                {
                    // >=v  and  <(minor+1)。v pad missing segments to three：~0.11 → >=0.11.0 <0.12.0
                    int[] b = Bump(Version.Numbers, 1);
                    bool? lo2 = CompareGe(candidate, Version);
                    bool? hi2 = CompareLt(candidate, FabricVersion.Parse(Join(b)));
                    if (lo2 == null || hi2 == null) return DependencyVerdict.Unknown;
                    ok = lo2.Value && hi2.Value;
                    break;
                }
                case "^":
                {
                    // ^x.y.z：first non-zero segment +1；^0.0.z → 0.0.z+1
                    int idx = 0;
                    while (idx < Version.Numbers.Length - 1 && Version.Numbers[idx] == 0) idx++;
                    int[] b = Bump(Version.Numbers, idx);
                    bool? lo3 = CompareGe(candidate, Version);
                    bool? hi3 = CompareLt(candidate, FabricVersion.Parse(Join(b)));
                    if (lo3 == null || hi3 == null) return DependencyVerdict.Unknown;
                    ok = lo3.Value && hi3.Value;
                    break;
                }
                default: return DependencyVerdict.Unknown;
            }
            return ok ? DependencyVerdict.Satisfied : DependencyVerdict.Unsatisfied;
        }

        static DependencyVerdict DependencyVerresolvedUnsatisfied() { return DependencyVerdict.Unsatisfied; }
        static string Join(int[] parts) { return string.Join(".", parts.Select(p => p.ToString()).ToArray()); }
        // Upper bound: position +1，; zeros after it（^1.2.3 → <2.0.0；~1.2.3 → <1.3.0）
        static int[] Bump(int[] source, int position)
        {
            int[] r = new int[Math.Max(source.Length, position + 1)];
            for (int i = 0; i < source.Length && i < r.Length; i++) r[i] = source[i];
            r[position] = r[position] + 1;
            for (int i = position + 1; i < r.Length; i++) r[i] = 0;
            return r;
        }
        static bool? CompareGe(FabricVersion c, FabricVersion v)
        { int? x = FabricVersion.Compare(c, v); return x == null ? null : (bool?)(x.Value >= 0); }
        static bool? CompareLt(FabricVersion c, FabricVersion v)
        { int? x = FabricVersion.Compare(c, v); return x == null ? null : (bool?)(x.Value < 0); }
    }

    /// <summary>One dependency (a key of depends/breaks/conflicts). Alternatives are OR; terms inside an alternative are AND AND。</summary>
    sealed class ModDependency
    {
        public string Id;
        public readonly List<string> Alternatives = new List<string>();
        public bool ExplicitOptional;
        public string Environment;                 // null=undeclared;"*"/"client"/"server"
        public readonly List<List<FabricTerm>> ParsedAlternatives = new List<List<FabricTerm>>();
        public bool Parsed = true;

        public bool AppliesToClient()
        { return Environment == null || Environment == "*" || Environment == "client"; }
    }

    static class FabricPredicate
    {
        // ">=1.0 <2.0" → terms AND；the whole alternative fails to parsefailed → that entry Unknown。
        public static List<FabricTerm> ParseAlternative(string text)
        {
            List<FabricTerm> terms = new List<FabricTerm>();
            if (text == null) { terms.Add(new FabricTerm { Valid = false }); return terms; }
            string normalized = Regex.Replace(text.Trim(), @"\s+", " ");
            if (normalized.Length == 0 || normalized == "*")
            { terms.Add(new FabricTerm { Any = true }); return terms; }
            string[] tokens = normalized.Split(' ');
            foreach (string token in tokens)
            {
                FabricTerm term = ParseTerm(token);
                if (term == null) { terms.Clear(); terms.Add(new FabricTerm { Valid = false }); return terms; }
                terms.Add(term);
            }
            return terms;
        }

        static FabricTerm ParseTerm(string token)
        {
            if (token.Length == 0) return null;
            Match m = Regex.Match(token, @"^(>=|<=|>|<|=|~|\^)?(.*)$");
            string op = m.Groups[1].Value; if (op.Length == 0) op = "";
            string rest = m.Groups[2].Value;
            if (rest.Length == 0) return null;
            if (rest == "*" || rest == "x" || rest == "X") return new FabricTerm { Any = true };
            bool wildcard = false;
            if (Regex.IsMatch(rest, @"(^|\.)(x|X|\*)$")) { wildcard = true; rest = Regex.Replace(rest, @"(\.|^)(x|X|\*)$", ""); if (rest.Length == 0) return new FabricTerm { Any = true }; }
            FabricVersion v = FabricVersion.Parse(rest);
            if (!v.Valid) return null;                       // Nonstandard token (placeholder, custom format）→ Unknown，never guessed.
            if (wildcard && op.Length > 0 && op != "=") return null;
            return new FabricTerm { Op = op, Version = v, WildcardTrailing = wildcard };
        }

        public static DependencyVerdict Evaluate(List<FabricTerm> terms, FabricVersion candidate)
        {
            bool anyUnknown = false;
            foreach (FabricTerm t in terms)
            {
                DependencyVerdict v = t.Evaluate(candidate);
                if (v == DependencyVerdict.Unsatisfied) return DependencyVerdict.Unsatisfied; // AND short-circuit
                if (v == DependencyVerdict.Unknown) anyUnknown = true;
            }
            return anyUnknown ? DependencyVerdict.Unknown : DependencyVerdict.Satisfied;
        }
    }

    // ---- dependencyDependency entries and the metadata model ----
    static class ModDependencyParser
    {
        public static ModDependency Parse(string id, object value)
        {
            ModDependency dep = new ModDependency { Id = id };
            List<string> alts = new List<string>();
            string s = value as string;
            if (s != null) alts.Add(s);
            else
            {
                object[] arr = Json.A(value);
                if (arr != null) foreach (object o in arr) { string a = o as string; if (a != null) alts.Add(a); }
                else
                {
                    var obj = Json.D(value);
                    if (obj != null)
                    {
                        dep.Environment = Json.S(obj, "environment", null);
                        if (Json.Has(obj, "optional")) dep.ExplicitOptional = Json.S(obj, "optional", "false") == "true";
                        object ver = Json.O(obj, "version");
                        string vs = ver as string;
                        if (vs != null) alts.Add(vs);
                        else
                        {
                            object[] va = Json.A(ver);
                            if (va != null) foreach (object o in va) { string a = o as string; if (a != null) alts.Add(a); }
                            else if (ver == null) alts.Add("*");   // object form omits version = any
                            else { dep.Parsed = false; alts.Add("*"); }
                        }
                    }
                    else { dep.Parsed = false; alts.Add("*"); }
                }
            }
            foreach (string a in alts) dep.Alternatives.Add(a);
            foreach (string a in alts)
            {
                List<FabricTerm> terms = FabricPredicate.ParseAlternative(a);
                dep.ParsedAlternatives.Add(terms);
                if (terms.Any(t => !t.Valid)) dep.Parsed = false;
            }
            return dep;
        }
    }

    // ---- Bounded nested jar reading ----
    static class NestedJarReader
    {
        public const int MaxNestedJars = 32;
        public const long MaxNestedJarBytes = 16L * 1024 * 1024;
        public const long MaxTotalNestedBytes = 64L * 1024 * 1024;

        public static List<ModMetadata> Read(ZipArchive archive, string parentFile, List<LaunchIssue> issues)
        {
            List<ModMetadata> nested = new List<ModMetadata>();
            string json = ReadEntryText(archive, "fabric.mod.json");
            if (json == null) return nested;
            var meta = Json.D(Json.Parse(json));
            if (meta == null) return nested;
            var jars = Json.A(Json.O(meta, "jars"));
            if (jars == null) return nested;
            long total = 0;
            int count = 0;
            foreach (object o in jars)
            {
                string file = Json.S(o, "file", "");
                if (file.Length == 0) continue;
                count++;
                if (count > MaxNestedJars) { issues.Add(new LaunchIssue("MOD_NESTED_LIMIT", parentFile + " Nested jars exceed " + MaxNestedJars + " ; truncated.")); break; }
                ZipArchiveEntry entry = archive.GetEntry(file);
                if (entry == null) { issues.Add(new LaunchIssue("MOD_NESTED_JAR_MISSING", parentFile + " declares declared nested jar missing：" + file)); continue; }
                if (entry.Length > MaxNestedJarBytes || total + entry.Length > MaxTotalNestedBytes)
                { issues.Add(new LaunchIssue("MOD_NESTED_LIMIT", parentFile + " Nested jar size limit exceeded: " + file)); continue; }
                total += entry.Length;
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        using (Stream input = entry.Open()) CopyBounded(input, ms, MaxNestedJarBytes);
                        ms.Position = 0;
                        using (var inner = new ZipArchive(ms, ZipArchiveMode.Read))
                        {
                            // Fabric Only one nesting level; jars inside nested jars are not recursive。
                            ModMetadata mod = new FabricModMetadataReader().Read(inner);
                            if (mod != null) { mod.FileName = parentFile + "!" + file; mod.IsNested = true; nested.Add(mod); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex is IOException || ex is InvalidDataException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
                        issues.Add(new LaunchIssue("MOD_NESTED_JAR_INVALID", parentFile + " Nested jar readingfailed：" + file + "（" + ex.Message + "）"));
                    else throw;
                }
            }
            return nested;
        }

        static string ReadEntryText(ZipArchive archive, string name)
        {
            ZipArchiveEntry e = archive.GetEntry(name);
            if (e == null) return null;
            if (e.Length > 1024 * 1024) throw new InvalidDataException("modmetadata exceeds 1 MB");
            using (var input = e.Open())
            using (var reader = new StreamReader(input, new UTF8Encoding(false, true)))
                return reader.ReadToEnd().TrimStart('\uFEFF');
        }

        static void CopyBounded(Stream source, Stream target, long limit)
        {
            byte[] buffer = new byte[8192]; int n; long copied = 0;
            while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                copied += n;
                if (copied > limit) throw new InvalidDataException("Nested jars exceedsize limit");
                target.Write(buffer, 0, n);
            }
        }
    }

    // ---- Fabric Loader dependencyoverrideconfig (config/fabric_loader_dependencies.json) ----
    static class LoaderOverrides
    {
        public static Dictionary<string, object> Load(string gameDirectory, List<LaunchIssue> issues)
        {
            try
            {
                string path = FileSafety.Under(gameDirectory, "config/fabric_loader_dependencies.json");
                if (!File.Exists(path)) return null;
                var fi = new FileInfo(path);
                if (fi.Length > 1024 * 1024) { issues.Add(new LaunchIssue("MOD_OVERRIDE_INVALID", "dependencyoverridefileexceeds 1 MB; ignored.")); return null; }
                string text;
                using (var input = File.OpenRead(path))
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[8192]; int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + n > 1024 * 1024) throw new InvalidDataException("dependencyoverridefileexceeds  1 MB");
                        output.Write(buffer, 0, n);
                    }
                    text = new UTF8Encoding(false, true).GetString(output.ToArray()).TrimStart('\uFEFF');
                }
                var root = Json.D(Json.Parse(text));
                if (root == null || Json.L(root, "version", 0) != 1)
                { issues.Add(new LaunchIssue("MOD_OVERRIDE_INVALID", "dependencyoverridefileformat unrecognized; ignored。")); return null; }
                var overrides = Json.D(Json.O(root, "overrides"));
                if (overrides == null) return null;
                return overrides;
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
                { issues.Add(new LaunchIssue("MOD_OVERRIDE_INVALID", "dependencyoverridefileread failed; ignored：" + ex.Message)); return null; }
                throw;
            }
        }

        public static bool Apply(ModMetadata mod, Dictionary<string, object> overrides, List<LaunchIssue> issues)
        {
            object value;
            if (overrides == null || !overrides.TryGetValue(mod.Id, out value)) return false;
            var entries = Json.D(value);
            if (entries == null) { issues.Add(new LaunchIssue("MOD_OVERRIDE_INVALID", mod.Id + "  dependency overrides must be grouped by mod and dependency type; ignored。")); return false; }
            bool applied = false;
            foreach (string kind in new[] { "depends", "breaks", "conflicts" })
            {
                var group = kind == "depends" ? mod.Depends : kind == "breaks" ? mod.Breaks : mod.Conflicts;
                bool replace = entries.ContainsKey(kind);
                var result = replace ? new List<ModDependency>() : new List<ModDependency>(group);
                bool valid = true, changed = false;
                // Fabric Loader 0.16.10: replacement suppresses +/-; otherwise remove then append.
                foreach (string operation in replace ? new[] { kind } : new[] { "-" + kind, "+" + kind })
                {
                    object raw;
                    if (!entries.TryGetValue(operation, out raw)) continue;
                    var dependencies = Json.D(raw);
                    if (dependencies == null) { valid = false; break; }
                    changed = true;
                    foreach (var dependency in dependencies)
                    {
                        if (operation.StartsWith("-")) result.RemoveAll(d => d.Id == dependency.Key);
                        else result.Add(ModDependencyParser.Parse(dependency.Key, dependency.Value));
                    }
                }
                if (!valid) { issues.Add(new LaunchIssue("MOD_OVERRIDE_INVALID", mod.Id + "   " + kind + " overridenot an object; ignored.")); continue; }
                if (changed) { group.Clear(); group.AddRange(result); applied = true; }
            }
            return applied;
        }
    }

    // ---- Resolution engine ----
    static class ModDependencyResolver
    {
        public static List<LaunchIssue> Resolve(List<ModMetadata> mods, string minecraftVersion, int javaMajor, Dictionary<string, object> overrides)
        {
            List<LaunchIssue> issues = new List<LaunchIssue>();
            // Provider table: id and provides both point to mods providing that IDmod
            Dictionary<string, List<ModMetadata>> providers = new Dictionary<string, List<ModMetadata>>(StringComparer.Ordinal);
            foreach (ModMetadata mod in mods)
            {
                AddProvider(providers, mod.Id, mod);
                foreach (string alias in mod.Provides) AddProvider(providers, alias, mod);
            }
            bool overrideUsed = false;
            foreach (ModMetadata mod in mods)
            {
                overrideUsed |= LoaderOverrides.Apply(mod, overrides, issues);
                ResolveGroup(mod, mod.Depends, "depends", providers, minecraftVersion, javaMajor, issues, errorOnUnsatisfied: true);
                ResolveGroup(mod, mod.Breaks, "breaks", providers, minecraftVersion, javaMajor, issues, errorOnUnsatisfied: false);
                ResolveGroup(mod, mod.Conflicts, "conflicts", providers, minecraftVersion, javaMajor, issues, errorOnUnsatisfied: false);
            }
            if (overrideUsed) issues.Add(new LaunchIssue("MOD_OVERRIDE_APPLIED", "Some dependency ranges overridden via fabric_loader_dependencies.json。"));
            return issues;
        }

        static void AddProvider(Dictionary<string, List<ModMetadata>> map, string id, ModMetadata mod)
        {
            if (id == null || id.Length == 0) return;
            List<ModMetadata> list;
            if (!map.TryGetValue(id, out list)) { list = new List<ModMetadata>(); map[id] = list; }
            list.Add(mod);
        }

        static void ResolveGroup(ModMetadata mod, List<ModDependency> group, string kind,
            Dictionary<string, List<ModMetadata>> providers, string minecraftVersion, int javaMajor,
            List<LaunchIssue> issues, bool errorOnUnsatisfied)
        {
            foreach (ModDependency dep in group)
            {
                if (!dep.AppliesToClient()) continue;                     // Server-scoped dependencies skipped on the client
                string rangeSource = "declares";
                if (!dep.Parsed)
                { issues.Add(new LaunchIssue("MOD_DEPENDENCY_UNKNOWN", mod.FileName + "   " + kind + " " + dep.Id + "  uses an unparseable version range (" + JoinRaw(dep) + "），compatibility unknown.")); continue; }

                // builtin providers：minecraft / java / fabricloader
                List<ModMetadata> found;
                providers.TryGetValue(dep.Id, out found);
                bool builtin = false;
                FabricVersion builtinVersion = null;
                bool builtinUnknown = false;
                if (found == null)
                {
                    if (dep.Id == "minecraft")
                    {
                        builtin = true;
                        if (minecraftVersion == null || minecraftVersion.Length == 0) builtinUnknown = true;
                        else builtinVersion = FabricVersion.Parse(minecraftVersion);
                    }
                    else if (dep.Id == "java")
                    {
                        builtin = true;
                        if (javaMajor <= 0) builtinUnknown = true;
                        else builtinVersion = FabricVersion.Parse(javaMajor <= 8 ? "1." + javaMajor : javaMajor.ToString());
                    }
                    else if (dep.Id == "fabricloader") { builtin = true; builtinUnknown = true; }
                }

                if (found == null && !builtin)
                {
                    if (kind != "depends" || dep.ExplicitOptional) continue;
                    issues.Add(new LaunchIssue("MOD_DEPENDENCY_UNRESOLVED", mod.FileName + " 's dependency " + dep.Id + " not found among mods; if the loader or an external component provides it, the game decides。"));
                    continue;
                }

                // OR Best of OR: satisfied if any alternative matches; otherwise Unknown → Unknown；otherwise Unsatisfied.
                bool anySat = false, anyUnknown = false;
                foreach (List<FabricTerm> alternative in dep.ParsedAlternatives)
                {
                    DependencyVerdict v = EvaluateAlternative(alternative, found, builtinVersion, builtinUnknown);
                    if (v == DependencyVerdict.Satisfied) { anySat = true; break; }
                    if (v == DependencyVerdict.Unknown) anyUnknown = true;
                }
                string rangeText = JoinRaw(dep);
                if (anySat)
                {
                    if (kind != "depends")
                        issues.Add(new LaunchIssue(kind == "breaks" ? "MOD_BREAKS_VIOLATED" : "MOD_CONFLICT_PRESENT",
                            mod.FileName + " declares " + kind + " " + dep.Id + "（" + rangeText + "），the installed set matches this conflict。"));
                    continue;
                }
                if (anyUnknown)
                    issues.Add(new LaunchIssue("MOD_DEPENDENCY_UNKNOWN", mod.FileName + "   " + kind + " " + dep.Id + "（" + rangeText + "，" + rangeSource + "）satisfiability undetermined。"));
                else if (errorOnUnsatisfied)
                {
                    if (dep.ExplicitOptional) continue;                   // Explicitly optional entries stay silent when unsatisfied, matching the loader
                    issues.Add(new LaunchIssue("MOD_DEPENDENCY_VERSION_UNSATISFIED", mod.FileName + "  requires dependency  " + dep.Id + " " + rangeText + "（" + rangeSource + "），not satisfied by the installed version。"));
                }
            }
        }

        static DependencyVerdict EvaluateAlternative(List<FabricTerm> alternative, List<ModMetadata> found, FabricVersion builtinVersion, bool builtinUnknown)
        {
            if (builtinUnknown) return DependencyVerdict.Unknown;
            if (found != null)
            {
                // Multiple providers: satisfied if any version matches
                bool anyUnknown = false;
                foreach (ModMetadata provider in found)
                {
                    DependencyVerdict v = FabricPredicate.Evaluate(alternative, FabricVersion.Parse(provider.Version));
                    if (v == DependencyVerdict.Satisfied) return DependencyVerdict.Satisfied;
                    if (v == DependencyVerdict.Unknown) anyUnknown = true;
                }
                return anyUnknown ? DependencyVerdict.Unknown : DependencyVerdict.Unsatisfied;
            }
            return FabricPredicate.Evaluate(alternative, builtinVersion);
        }

        static string JoinRaw(ModDependency dep)
        {
            if (dep.Alternatives.Count == 1) return dep.Alternatives[0];
            return "[" + string.Join(" / ", dep.Alternatives.ToArray()) + "]";
        }
    }
}
