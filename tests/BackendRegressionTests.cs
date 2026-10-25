using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuyunCraft
{
    static class BackendRegressionTests
    {
        sealed class Hook : ILaunchPreflightCheck
        {
            readonly Action action;
            public Hook(Action action) { this.action = action; }
            public IEnumerable<LaunchIssue> Inspect(LaunchContext context, CancellationToken token)
            { action(); return new LaunchIssue[0]; }
        }
        sealed class ProgressSink : IProgress<LaunchProgress>
        {
            public readonly List<LaunchStage> Stages = new List<LaunchStage>();
            public void Report(LaunchProgress value) { Stages.Add(value.Stage); }
        }
        static void Jar(string root, string name, string json, string entry = "fabric.mod.json")
        {
            string path = Path.Combine(root, "mods", name + ".jar");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write(json);
        }
        static List<LaunchIssue> Scan(string root, string loader = "fabric")
        { return new ModPreflightCheck().Inspect(new LaunchContext("fixture", root, loader), CancellationToken.None).ToList(); }
        static bool Cancelled(Action action)
        { try { action(); return false; } catch (OperationCanceledException) { return true; } }

        public static void Run(string root, Action<bool, string> check)
        {
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "hash.bin"); File.WriteAllText(file, "abcd");
            var cache = new FileSafety.VerifyCache();
            check(FileSafety.Matches(file, null, 4, cache), "Cache accepts valid initial contract");
            check(!FileSafety.Matches(file, new string('0', 40), 4, cache), "Cache cannot bypass a changed expected hash");
            check(!FileSafety.Matches(file, null, 9, cache), "Cache cannot bypass a changed expected length");

            string mods = Path.Combine(root, "fabric"); Directory.CreateDirectory(mods);
            Jar(mods, "example", "{\"schemaVersion\":1,\"id\":\"example\",\"version\":\"1\",\"depends\":{\"helper_alias\":\"*\",\"missing\":\">=1\"},\"jars\":[{\"file\":\"nested.jar\"}]}");
            Jar(mods, "helper", "{\"schemaVersion\":1,\"id\":\"helper\",\"version\":\"1\",\"provides\":[\"helper_alias\"]}");
            var issues = Scan(mods);
            check(!issues.Any(i => i.BlocksLaunch), "Unresolved dependency is conservative and nonblocking");
            check(issues.Count(i => i.Code == "MOD_DEPENDENCY_UNRESOLVED") == 1 && issues.Any(i => i.Message.Contains("missing")), "Provided aliases satisfy ID lookup");
            // 1.4：depends.helper_alias="*" now genuinely resolved (helper provides the alias; Satisfied); no longer reports PARTIAL
            check(!issues.Any(i => i.Code == "MOD_RESOLUTION_PARTIAL" && i.Message.Contains("helper")), "Wildcard dependency on provided alias resolves silently");
            Jar(mods, "example-copy", "{\"schemaVersion\":1,\"id\":\"example\",\"version\":\"2\"}");
            check(Scan(mods).Any(i => i.Code == "MOD_DUPLICATE_ID"), "Duplicate top-level Fabric IDs reported");
            Jar(mods, "server", "{\"schemaVersion\":1,\"id\":\"server_mod\",\"version\":\"1\",\"environment\":\"server\",\"depends\":{\"server_dependency\":\"*\"}}");
            issues = Scan(mods);
            check(issues.Any(i => i.Code == "MOD_SERVER_ONLY") && !issues.Any(i => i.Message.Contains("server_dependency")), "Server-only mod dependencies skipped on client");
            Jar(mods, "forge", "modLoader=\"javafml\"", "META-INF/mods.toml");
            check(Scan(mods).Any(i => i.Code == "MOD_LOADER_MISMATCH"), "Forge marker detected without claiming TOML support");
            Jar(mods, "bad-json", "{");
            check(Scan(mods).Any(i => i.BlocksLaunch && i.Code == "MOD_METADATA_INVALID"), "Broken Fabric JSON blocks launch");
            check(Scan(mods, "vanilla").Single().Code == "MODS_WITHOUT_LOADER", "Vanilla warns without loading or inspecting mod contents");
            check(Scan(mods, "neoforge").Any(i => i.Code == "MOD_LOADER_UNCHECKED") && Scan(mods, "neoforge").Any(i => i.Code == "MOD_LOADER_MISMATCH"), "Other loaders report incomplete dependencies and mismatched markers");
            string bounded = Path.Combine(root, "bounded");
            Jar(bounded, "oversized", new string('x', 1024 * 1024 + 1));
            check(Scan(bounded).Any(i => i.BlocksLaunch), "Oversized compressed metadata rejected");
            string corrupt = Path.Combine(root, "corrupt"); Directory.CreateDirectory(Path.Combine(corrupt, "mods"));
            File.WriteAllText(Path.Combine(corrupt, "mods", "bad.jar"), "not a zip");
            check(Scan(corrupt).Any(i => i.BlocksLaunch), "Truncated mod jar rejected");

            var cancelled = new CancellationToken(true);
            check(Cancelled(() => new DownloadManager().RunAsync(cancelled).GetAwaiter().GetResult()), "External download cancellation prevents work");
            check(Cancelled(() => new ModPreflightCheck().Inspect(new LaunchContext("test", mods, "fabric"), cancelled)), "Preflight honors cancellation");

            string game = Path.Combine(root, "game");
            string metadataPath = FileSafety.VersionFile(game, "fixture", ".json");
            FileSafety.WriteText(metadataPath, "{\"id\":\"fixture\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\",\"javaVersion\":{\"majorVersion\":21},\"arguments\":{\"jvm\":[],\"game\":[\"--username\",\"${auth_player_name}\"]}}");
            FileSafety.WriteText(FileSafety.VersionFile(game, "fixture", ".jar"), "fixture-client");
            Jar(game, "wrong-directory", "{"); // Version isolation must ignore this shared mods directory.
            var config = new Config { GameDir = game, UserName = "Before", JavaPath = "fake-java.exe", MemoryGb = 2, VersionIsolation = true };
            var progress = new ProgressSink(); int javaCalls = 0;
            var service = new LaunchPreparationService(checks: new ILaunchPreflightCheck[] {
                new ModPreflightCheck(), new Hook(() => config.UserName = "After")
            }, selectJava: (preferred, required) => { javaCalls++; check(required == 21, "Pipeline resolves metadata Java requirement"); return preferred; },
                download: (items, token) => { throw new Exception("Unexpected network"); });
            var prepared = service.PrepareAsync(config, "fixture", progress, CancellationToken.None).GetAwaiter().GetResult();
            check(prepared.Plan.GameArgs.Contains("Before") && config.UserName == "After", "In-flight launch uses a configuration snapshot");
            check(prepared.Plan.WorkingDir == Path.GetDirectoryName(metadataPath), "Plan and mod preflight share the isolated game directory");
            check(progress.Stages.SequenceEqual(new[] { LaunchStage.Metadata, LaunchStage.Preflight, LaunchStage.Java, LaunchStage.Files, LaunchStage.Plan, LaunchStage.Ready }), "Launch phases run in defined order");
            check(service.LastReport.Status == "ready" && service.LastReport.StageMilliseconds.Count == 5 && javaCalls == 1, "Preparation report records all stages");
            string reportPath = Path.Combine(root, "report.json"); prepared.Report.Save(reportPath);
            check(!File.ReadAllText(reportPath).Contains("Before") && !File.ReadAllText(reportPath).Contains("fake-java"), "Diagnostics exclude account and command data");

            service = new LaunchPreparationService(selectJava: (p, r) => { throw new Exception("Java must not run"); });
            check(Cancelled(() => service.PrepareAsync(config, "fixture", null, cancelled).GetAwaiter().GetResult()) && service.LastReport.Status == "cancelled", "Cancelled preparation does not probe Java");
            using (var source = new CancellationTokenSource())
            {
                service = new LaunchPreparationService(checks: new[] { new Hook(source.Cancel) }, selectJava: (p, r) => { throw new Exception("Java must not run"); });
                check(Cancelled(() => service.PrepareAsync(config, "fixture", null, source.Token).GetAwaiter().GetResult()), "Cancellation during preflight prevents later phases");
            }
            Jar(Path.GetDirectoryName(metadataPath), "broken", "{");
            service = new LaunchPreparationService(selectJava: (p, r) => { throw new Exception("Java must not run"); });
            bool blocked = false;
            try { service.PrepareAsync(config, "fixture", null, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { blocked = true; }
            check(blocked && service.LastReport.FailedStage == "Preflight" && service.LastReport.Issues.Any(i => i.BlocksLaunch), "Invalid mod stops preparation before Java and downloads");

            string repair = Path.Combine(root, "repair");
            var metadata = Json.D(Json.Parse("{\"id\":\"repair\",\"downloads\":{\"client\":{\"size\":4,\"url\":\"https://example.invalid/client.jar\"}}}"));
            int downloads = 0;
            VersionFiles.EnsureAsync(repair, "repair", metadata, (items, token) => {
                downloads++; foreach (var item in items) FileSafety.WriteText(item.Dest, "abcd"); return Task.FromResult(true);
            }, CancellationToken.None).GetAwaiter().GetResult();
            check(downloads == 1 && File.Exists(FileSafety.VersionFile(repair, "repair", ".jar")), "Repair rechecks downloaded files before succeeding");
            downloads = 0; bool failed = false;
            try { VersionFiles.EnsureAsync(Path.Combine(root, "unrepairable"), "repair", metadata, (items, token) => {
                downloads++; return Task.FromResult(true);
            }, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { failed = true; }
            check(failed && downloads == 3, "Incomplete repair stops at bounded retry count");
            var detector = new MinecraftLoaderDetector();
            check(detector.Detect(Json.D(Json.Parse("{\"libraries\":[{\"name\":\"net.neoforged:neoforge:1\"}]}"))) == "neoforge", "Loader detector recognizes NeoForge extension point");
        }
    }
}
