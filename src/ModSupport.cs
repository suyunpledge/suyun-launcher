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
    sealed class ModMetadata
    {
        public string Id, Version, FileName;
        public bool ClientEnabled = true, HasNestedJars, HasConstraints, IsNested, HasUnparsableConstraints;
        public readonly List<string> Provides = new List<string>();
        public readonly List<string> Dependencies = new List<string>();
        public readonly List<ModDependency> Depends = new List<ModDependency>();
        public readonly List<ModDependency> Breaks = new List<ModDependency>();
        public readonly List<ModDependency> Conflicts = new List<ModDependency>();
    }

    // Return null when unsupported. Readers inspect metadata only; never load assemblies or execute jars.
    interface IModMetadataReader
    {
        ModMetadata Read(ZipArchive archive);
    }

    sealed class FabricModMetadataReader : IModMetadataReader
    {
        const int MaxMetadataBytes = 1024 * 1024;
        public ModMetadata Read(ZipArchive archive)
        {
            var fabricEntries = archive.Entries.Where(e => e.FullName == "fabric.mod.json").ToArray();
            if (fabricEntries.Length == 0) return null;
            if (fabricEntries.Length != 1) throw new InvalidDataException("Duplicate fabric.mod.json");
            var entry = fabricEntries[0];
            if (entry.Length > MaxMetadataBytes) throw new InvalidDataException("modmetadata exceeds 1 MB");
            string text;
            using (var input = entry.Open())
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[8192]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > MaxMetadataBytes) throw new InvalidDataException("modmetadata exceeds 1 MB");
                    output.Write(buffer, 0, count);
                }
                text = new UTF8Encoding(false, true).GetString(output.ToArray()).TrimStart('\uFEFF');
            }
            var json = Json.D(Json.Parse(text));
            if (json == null) throw new InvalidDataException("modmetadata must be a JSON object");
            // Schema 0 and future formats need their own reader, not a guessed interpretation.
            if (Json.L(json, "schemaVersion", 0) != 1) return null;
            string id = Json.S(json, "id", "");
            if (!Regex.IsMatch(id, @"\A[a-z][a-z0-9_-]{1,63}\z")) throw new InvalidDataException("invalid Fabric mod id");
            string version = Json.S(json, "version", "");
            if (string.IsNullOrWhiteSpace(version)) throw new InvalidDataException("missing mod version");
            var mod = new ModMetadata { Id = id, Version = version };
            object environment = Json.O(json, "environment");
            if (environment is string) mod.ClientEnabled = (string)environment != "server";
            object[] environments = Json.A(environment);
            if (environments != null) mod.ClientEnabled = environments.Contains("*") || environments.Contains("client");
            foreach (object alias in Json.A(Json.O(json, "provides")) ?? new object[0])
                if (alias is string) mod.Provides.Add((string)alias);
            var depends = Json.D(Json.O(json, "depends"));
            if (depends != null)
            {
                mod.Dependencies.AddRange(depends.Keys);
                foreach (KeyValuePair<string, object> kv in depends)
                {
                    ModDependency parsed = ModDependencyParser.Parse(kv.Key, kv.Value);
                    mod.Depends.Add(parsed);
                    if (!parsed.Parsed) mod.HasUnparsableConstraints = true;
                }
            }
            foreach (string group in new[] { "breaks", "conflicts" })
            {
                var entries = Json.D(Json.O(json, group));
                if (entries == null) continue;
                foreach (KeyValuePair<string, object> kv in entries)
                {
                    ModDependency parsed = ModDependencyParser.Parse(kv.Key, kv.Value);
                    if (group == "breaks") mod.Breaks.Add(parsed); else mod.Conflicts.Add(parsed);
                    if (!parsed.Parsed) mod.HasUnparsableConstraints = true;
                }
            }
            mod.HasNestedJars = (Json.A(Json.O(json, "jars")) ?? new object[0]).Length > 0;
            mod.HasConstraints = mod.Depends.Count > 0 || mod.Breaks.Count > 0 || mod.Conflicts.Count > 0;
            return mod;
        }
    }

    sealed class ModPreflightCheck : ILaunchPreflightCheck
    {
        readonly IModMetadataReader[] readers;
        public ModPreflightCheck(IEnumerable<IModMetadataReader> readers = null)
        { this.readers = (readers ?? new IModMetadataReader[] { new FabricModMetadataReader() }).ToArray(); }

        public IEnumerable<LaunchIssue> Inspect(LaunchContext context, CancellationToken cancellation)
        {
            return Inspect(context, cancellation, null, 0);
        }

        public IEnumerable<LaunchIssue> Inspect(LaunchContext context, CancellationToken cancellation, string minecraftVersion, int javaMajor)
        {
            var issues = new List<LaunchIssue>();
            cancellation.ThrowIfCancellationRequested();
            string modsDirectory = FileSafety.Under(context.GameDirectory, "mods");
            if (!Directory.Exists(modsDirectory)) return issues;
            var paths = Directory.EnumerateFiles(modsDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(p => Path.GetExtension(p).Equals(".jar", StringComparison.OrdinalIgnoreCase)).Take(2049).ToArray();
            if (paths.Length == 0) return issues;
            if (context.Loader != "fabric")
            {
                issues.Add(new LaunchIssue(context.Loader == "vanilla" ? "MODS_WITHOUT_LOADER" : "MOD_LOADER_UNCHECKED",
                    context.Loader == "vanilla" ? "No mod loader detected; mods may not take effect。" : "Loader markers only; not yet parsed:  " + context.Loader + "  dependency rules。"));
                if (context.Loader == "vanilla") return issues;
            }
            if (paths.Length > 2048)
            {
                issues.Add(new LaunchIssue("MOD_SCAN_LIMIT", "modOver 2048 mods; preflight skipped, deferring to the loader。"));
                return issues;
            }
            var mods = new List<ModMetadata>();
            foreach (string path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                cancellation.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                try
                {
                    string safePath = FileSafety.Under(modsDirectory, name);
                    if (new FileInfo(safePath).Length > 512L * 1024 * 1024)
                    { issues.Add(new LaunchIssue("MOD_SCAN_LIMIT", name + "  exceeds 512 MB; not preflighted.")); continue; }
                    using (var archive = ZipFile.OpenRead(safePath))
                    {
                        if (archive.Entries.Count > 100000) throw new InvalidDataException("ZIP entry count exceeds the limit");
                        var loaders = ModManagement.DetectLoaders(archive);
                        if (context.Loader != "fabric")
                        {
                            if (context.Loader != "vanilla" && loaders.Count > 0 && !ModManagement.RecognizesLoader(loaders, context.Loader))
                                issues.Add(new LaunchIssue("MOD_LOADER_MISMATCH", name + "  loader marker:  " + string.Join(" / ", loaders) + "，currently " + context.Loader + "。"));
                            continue;
                        }
                        ModMetadata mod = null;
                        foreach (var reader in readers) { mod = reader.Read(archive); if (mod != null) break; }
                        if (mod == null)
                        {
                            bool mismatch = loaders.Count > 0 && !loaders.Contains("fabric");
                            issues.Add(new LaunchIssue(mismatch ? "MOD_LOADER_MISMATCH" : "MOD_METADATA_UNCHECKED",
                                name + (mismatch ? "  loader marker:  " + string.Join(" / ", loaders) + "，Fabric compatibility unconfirmed." : "  No supported Fabric metadata found.")));
                            continue;
                        }
                        mod.FileName = name;
                        if (!mod.ClientEnabled) { issues.Add(new LaunchIssue("MOD_SERVER_ONLY", name + "  is server-only; client preflight skipped。")); continue; }
                        mods.Add(mod);
                        if (mod.HasNestedJars)
                        {
                            List<ModMetadata> nested = NestedJarReader.Read(archive, name, issues);
                            foreach (ModMetadata n in nested)
                            {
                                if (!n.ClientEnabled) continue;
                                mods.Add(n);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is InvalidDataException) && !(ex is ArgumentException) &&
                        !(ex is InvalidOperationException) && !(ex is UnauthorizedAccessException)) throw;
                    issues.Add(new LaunchIssue("MOD_METADATA_INVALID", name + " Failed to read mod metadata：" + ex.Message, true));
                }
            }
            if (context.Loader != "fabric") return issues;
            foreach (var duplicate in mods.Where(m => !m.IsNested).GroupBy(m => m.Id).Where(g => g.Count() > 1))
                issues.Add(new LaunchIssue("MOD_DUPLICATE_ID", "Multiple top-level mods declare ID " + duplicate.Key + "：" +
                    string.Join(", ", duplicate.Select(m => m.FileName)) + "。Check for duplicate installs."));
            // Full dependency resolution: ranges, breaks/conflicts, nested jar providers, loader overrides
            var overrides = LoaderOverrides.Load(context.GameDirectory, issues);
            foreach (LaunchIssue r in ModDependencyResolver.Resolve(mods, minecraftVersion, javaMajor, overrides))
                issues.Add(r);
            if (mods.Any(m => m.HasUnparsableConstraints))
                issues.Add(new LaunchIssue("MOD_RESOLUTION_PARTIAL", "Some mods use unparseable version constraints; the loader makes the final call。"));
            return issues;
        }
    }
}
