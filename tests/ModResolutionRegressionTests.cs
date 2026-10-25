// P1 依赖解析引擎的离线回归（加入 BackendRegressionTests.Run 的调用链）
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace SuyunCraft
{
    static class ModResolutionRegressionTests
    {
        static string Root;

        static string ModJson(string id, string version, string depends, string breaks = null, string environment = null, string provides = null)
        {
            List<string> parts = new List<string>();
            parts.Add("\"schemaVersion\":1");
            parts.Add("\"id\":\"" + id + "\"");
            parts.Add("\"version\":\"" + version + "\"");
            if (depends != null) parts.Add("\"depends\":" + depends);
            if (breaks != null) parts.Add("\"breaks\":" + breaks);
            if (environment != null) parts.Add("\"environment\":\"" + environment + "\"");
            if (provides != null) parts.Add("\"provides\":" + provides);
            return "{" + string.Join(",", parts.ToArray()) + "}";
        }

        static string Dir(string name)
        {
            string d = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.Combine(d, "mods"));
            return d;
        }

        static void Jar(string dir, string name, string json)
        {
            string path = Path.Combine(dir, "mods", name + ".jar");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open())) writer.Write(json);
        }

        static List<LaunchIssue> Scan(string dir, string mc = "1.21.1", int java = 21)
        { return new ModPreflightCheck().Inspect(new LaunchContext("t", dir, "fabric"), CancellationToken.None, mc, java).ToList(); }

        internal static void Run(string root, Action<bool, string> check)
        {
            Root = root;

            // ---- SemVer 与比较运算 ----
            string semver = Dir("semver");
            Jar(semver, "lib", ModJson("lib", "1.2.3", null));
            Jar(semver, "need-old", ModJson("need-old", "1", "{\"lib\":\"<1.2.0\"}"));
            var issues = Scan(semver);
            check(issues.Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED" && i.Message.Contains("need-old")), "Version lower-bound unsatisfied detected");

            string ge = Dir("ge");
            Jar(ge, "lib", ModJson("lib", "2.0.0", null));
            Jar(ge, "ok", ModJson("ok", "1", "{\"lib\":\">=1.0.0\"}"));
            check(!Scan(ge).Any(i => i.Code.StartsWith("MOD_DEPENDENCY", StringComparison.Ordinal)), "In-range dependency passes silently");

            // ---- AND 条件与通配 ----
            string andRange = Dir("andrange");
            Jar(andRange, "lib", ModJson("lib", "1.16.5", null));
            Jar(andRange, "narrow", ModJson("narrow", "1", "{\"lib\":\">=1.16 <1.17\"}"));
            check(!Scan(andRange).Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED"), "AND range 1.16.x satisfied by 1.16.5");
            Jar(andRange, "wide", ModJson("wide", "1", "{\"lib\":\"1.16.x\"}"));
            check(!Scan(andRange).Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED" && i.Message.Contains("wide")), "Trailing wildcard matches same minor");

            // ---- OR alternatives ----
            string orRange = Dir("orrange");
            Jar(orRange, "lib", ModJson("lib", "0.11.0", null));
            Jar(orRange, "pick", ModJson("pick", "1", "{\"lib\":[\">=2.0\", \"0.11.x\"]}"));
            check(!Scan(orRange).Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED"), "OR alternative satisfied by second branch");

            string orMiss = Dir("ormiss");
            Jar(orMiss, "lib", ModJson("lib", "1.5.0", null));
            Jar(orMiss, "pick", ModJson("pick", "1", "{\"lib\":[\">=2.0\", \"0.11.x\"]}"));
            check(Scan(orMiss).Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED" && i.Message.Contains("pick")), "All OR branches unsatisfied reported");

            // ---- 非标准版本 → Unknown，不误拦 ----
            string weird = Dir("weird");
            Jar(weird, "lib", ModJson("lib", "1.0.0+build.7", null));
            Jar(weird, "user", ModJson("user", "1", "{\"lib\":\">=1.0.0+build.1\"}"));
            issues = Scan(weird);
            check(issues.Any(i => i.Code == "MOD_DEPENDENCY_UNKNOWN"), "Build-metadata version reported unknown, never guessed");
            check(!issues.Any(i => i.BlocksLaunch), "Unknown never blocks launch");

            // ---- minecraft / java 内建依赖 ----
            string builtin = Dir("builtin");
            Jar(builtin, "gate", ModJson("gate", "1", "{\"minecraft\":\">=1.21\",\"java\":\">=21\"}"));
            check(!Scan(builtin, "1.21.1", 21).Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED"), "minecraft/java builtin versions satisfy ranges");
            check(Scan(builtin, "1.20.1", 17).Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED"), "Old minecraft/java detected via builtins");

            // ---- breaks 违反 ----
            string breaks = Dir("breaks");
            Jar(breaks, "lib", ModJson("lib", "3.0.0", null));
            Jar(breaks, "hater", ModJson("hater", "1", null, breaks: "{\"lib\":\">=3.0\"}"));
            check(Scan(breaks).Any(i => i.Code == "MOD_BREAKS_VIOLATED"), "breaks range violation reported");

            // ---- 嵌套 jar 提供者 ----
            string nested = Dir("nested");
            string nestedDir = Path.Combine(Root, "nested-mods");
            string outerPath = Path.Combine(nested, "mods", "bundle.jar");
            using (var archive = ZipFile.Open(outerPath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open()))
                    writer.Write(ModJson("bundle", "1", "{\"inner\":\"*\"}", null, null, null).Replace("\"inner\":\"*\"", "\"jars\":[{\"file\":\"inner.jar\"}],\"depends\":{\"inner\":\"*\"}"));
                var innerEntry = archive.CreateEntry("inner.jar");
                using (var innerStream = innerEntry.Open())
                using (var innerArchive = new ZipArchive(innerStream, ZipArchiveMode.Create, leaveOpen: true))
                using (var writer = new StreamWriter(innerArchive.CreateEntry("fabric.mod.json").Open()))
                    writer.Write(ModJson("inner", "1.0", null));
            }
            issues = Scan(nested);
            check(!issues.Any(i => i.Code == "MOD_DEPENDENCY_UNRESOLVED" && i.Message.Contains("inner")), "Nested jar satisfies parent dependency");

            // ---- 服务端限定依赖在客户端跳过 ----
            string envSkip = Dir("envskip");
            Jar(envSkip, "mod", ModJson("mod", "1", "{\"serverhelper\":{\"version\":\"*\",\"environment\":\"server\"}}"));
            check(!Scan(envSkip).Any(i => i.Message.Contains("serverhelper")), "Server-scoped dependency skipped on client");

            // ---- 依赖覆盖配置 ----
            string ovr = Dir("override");
            Jar(ovr, "lib", ModJson("lib", "1.0.0", null));
            Jar(ovr, "user", ModJson("user", "1", "{\"lib\":\">=2.0\"}"));
            Directory.CreateDirectory(Path.Combine(ovr, "config"));
            File.WriteAllText(Path.Combine(ovr, "config", "fabric_loader_dependencies.json"),
                "{\"version\":1,\"overrides\":{\"user\":{\"depends\":{\"lib\":\"*\"}}}}");
            issues = Scan(ovr);
            check(!issues.Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED"), "Loader override relaxes range to any");
            check(issues.Any(i => i.Code == "MOD_OVERRIDE_APPLIED"), "Override usage is surfaced to the user");
            Jar(ovr, "other", ModJson("other", "1", "{\"lib\":\">=3.0\"}"));
            issues = Scan(ovr);
            check(issues.Count(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED") == 1 && issues.Any(i => i.Message.Contains("other.jar")), "Official overrides affect only the named mod");
            string overridePath = Path.Combine(ovr, "config", "fabric_loader_dependencies.json");
            File.WriteAllText(overridePath, "{\"version\":1,\"overrides\":{\"user\":{\"-depends\":{\"lib\":\"ignored\"},\"+depends\":{\"lib\":\"*\"}}}}");
            check(Scan(ovr).Count(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED") == 1, "Official remove then add dependency override works");
            File.WriteAllText(overridePath, "{\"version\":1,\"overrides\":{\"user\":{\"depends\":{},\"+depends\":{\"not_installed\":\"*\"}}}}");
            check(!Scan(ovr).Any(i => i.Message.Contains("not_installed")), "Replacement override suppresses add/remove as Fabric Loader does");
            File.WriteAllText(overridePath, "{\"version\":1,\"overrides\":{\"user\":{\"+depends\":{\"lib\":\"*\"}}}}");
            check(Scan(ovr).Count(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED") == 2, "Add-only override does not erase original dependency constraints");
            File.WriteAllText(overridePath, "{");
            check(Scan(ovr).Any(i => i.Code == "MOD_OVERRIDE_INVALID"), "Malformed loader override reports a diagnostic instead of crashing preflight");

            // ---- 重复 ID 仍按原样报告（Fabric ID 最短 2 位，用合法的 xx） ----
            string dup = Dir("dup");
            Jar(dup, "a", ModJson("xx", "1", null));
            Jar(dup, "b", ModJson("xx", "2", null));
            check(Scan(dup).Any(i => i.Code == "MOD_DUPLICATE_ID"), "Duplicate IDs still reported");

            // ---- 版本比较器单元 ----
            check(FabricVersion.Compare(FabricVersion.Parse("1.16.5"), FabricVersion.Parse("1.16.4")) == 1, "Compare 1.16.5 > 1.16.4");
            check(FabricVersion.Compare(FabricVersion.Parse("1.0.0-alpha"), FabricVersion.Parse("1.0.0")) == -1, "Prerelease sorts before release");
            check(FabricVersion.Compare(FabricVersion.Parse("1.2"), FabricVersion.Parse("1.2.0")) == 0, "Missing segments pad to zero");
            check(!FabricVersion.Parse("1.0.0+build").Valid || FabricVersion.Compare(FabricVersion.Parse("1.0.0+build"), FabricVersion.Parse("1.0.0")) != null, "Nonstandard suffix stays comparable or invalid, never crashes");
            check(FabricVersion.Parse("latest").Valid == false, "Placeholder versions are invalid, not guessed");
            var term = FabricPredicate.ParseAlternative(">=1.16 <1.17");
            check(FabricPredicate.Evaluate(term, FabricVersion.Parse("1.16.9")) == DependencyVerdict.Satisfied, "Predicate AND evaluates range");
            check(FabricPredicate.Evaluate(FabricPredicate.ParseAlternative("^1.2.3"), FabricVersion.Parse("2.0.0")) == DependencyVerdict.Unsatisfied, "Caret upper bound enforced");
            check(FabricPredicate.Evaluate(FabricPredicate.ParseAlternative("^1.2.3"), FabricVersion.Parse("1.9.9")) == DependencyVerdict.Satisfied, "Caret allows minor bumps");
            check(FabricPredicate.Evaluate(FabricPredicate.ParseAlternative("~0.11"), FabricVersion.Parse("0.11.4")) == DependencyVerdict.Satisfied, "Tilde keeps patch range");
        }
    }
}
