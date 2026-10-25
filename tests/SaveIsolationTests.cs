using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SuyunCraft
{
    static class SaveIsolationTests
    {
        static bool Reject(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        static void Write(string root, string file, string text) { FileSafety.WriteText(FileSafety.Under(root, file), text); }
        static bool Same(string one, string two) { return File.ReadAllText(one) == File.ReadAllText(two); }
        sealed class CancelDuringCopy : IProgress<string>
        {
            public readonly CancellationTokenSource Source = new CancellationTokenSource();
            public void Report(string text) { if (text.StartsWith("Copied")) Source.Cancel(); }
        }
        public static void Run(string root, Action<bool, string> check)
        {
            var cfg = ModManagementTests.Fixture(Path.Combine(root, "game"));
            cfg.JavaPath = @"C:\fake-java.exe";
            string source = GameDirectories.Resolve(cfg, "custom-profile");
            ModManagementTests.Jar(FileSafety.Under(source, "mods/third-party.jar"), "third_party");
            Write(source, "config/sample.cfg", "source-config"); Write(source, "defaultconfigs/server.toml", "source-default");
            Write(source, "resourcepacks/test.txt", "pack"); Write(source, "shaderpacks/test.txt", "shader");
            Write(source, "kubejs/server_scripts/test.js", "script"); Write(source, "options.txt", "options");
            Write(source, "config/suyuncraft-ai.json", "{\"token\":\"original-token\",\"endpoint\":\"http://127.0.0.1:18765/plan\",\"dailyRequests\":30}");
            Write(source, "config/suyuncraft-ai-quota.local.json", "{\"day\":\"2026-10-23\",\"requests\":9}");
            string world = FileSafety.Under(source, "saves/original");
            Write(world, "level.dat", "world-data"); Write(world, "region/r.0.0.mca", "region-data");
            Write(world, "datapacks/sample/pack.mcmeta", "pack-data"); Write(world, "session.lock", "unlocked-session");
            Write(source, "saves/other/level.dat", "other-world");
            var a = SaveIsolation.Create(cfg, "custom-profile", "survival A", world, true, CancellationToken.None);
            var b = SaveIsolation.Create(cfg, "custom-profile", "survival B", world, true, CancellationToken.None);
            string dirA = SaveIsolation.GameDirectory(cfg.GameDir, a.Id), dirB = SaveIsolation.GameDirectory(cfg.GameDir, b.Id);
            check(a.Id != b.Id && dirA != dirB && SaveIsolation.List(cfg.GameDir).Count == 2, "Two worlds on the same version receive separate immutable environment IDs");
            check(File.Exists(Path.Combine(world, "level.dat")) && Same(Path.Combine(world, "region/r.0.0.mca"), Path.Combine(dirA, "saves/world/region/r.0.0.mca")), "World import copies complete region data and preserves source");
            check(File.Exists(Path.Combine(dirA, "saves/world/datapacks/sample/pack.mcmeta")) && !Directory.Exists(Path.Combine(dirA, "saves/other")), "World datapacks follow only the selected world");
            check(!File.Exists(Path.Combine(dirA, "saves/world/session.lock")) && File.Exists(Path.Combine(world, "session.lock")), "Imported copy does not inherit source session lock");
            check(File.Exists(Path.Combine(dirA, "mods/third-party.jar")) && File.Exists(Path.Combine(dirA, "defaultconfigs/server.toml")) && File.Exists(Path.Combine(dirA, "kubejs/server_scripts/test.js")), "Optional environment seed copies mods, server defaults and scripts");
            Write(dirA, "config/sample.cfg", "changed-A"); Write(dirA, "saves/world/region/r.0.0.mca", "changed-region-A");
            check(File.ReadAllText(Path.Combine(dirB, "config/sample.cfg")) == "source-config" && File.ReadAllText(Path.Combine(source, "config/sample.cfg")) == "source-config", "Configuration changes cannot propagate through copies or links");
            check(File.ReadAllText(Path.Combine(dirB, "saves/world/region/r.0.0.mca")) == "region-data" && File.ReadAllText(Path.Combine(world, "region/r.0.0.mca")) == "region-data", "World writes stay inside their environment");
            string tokenA = Json.S(Json.Parse(File.ReadAllText(Path.Combine(dirA, "config/suyuncraft-ai.json"))), "token", "");
            string tokenB = Json.S(Json.Parse(File.ReadAllText(Path.Combine(dirB, "config/suyuncraft-ai.json"))), "token", "");
            check(tokenA.Length == 64 && tokenA != tokenB && tokenA != "original-token", "Each copied AI environment gets an independent bridge token");
            check(File.ReadAllText(Path.Combine(dirA, "config/suyuncraft-ai-quota.local.json")).Contains("9"), "Copy preserves existing AI usage counts rather than resetting budget");
            cfg.SaveInstanceId = a.Id;
            check(GameDirectories.Resolve(cfg, "custom-profile", false) == dirA && new ModManagement(cfg, "custom-profile").GameDirectory == dirA, "Save isolation overrides version toggle and routes mod management consistently");
            check(AiIntegration.GameDirectory(cfg, "custom-profile") == dirA, "AI installation and bridge configuration resolve the selected save environment");
            string bundle = Path.Combine(root, "ai-bundle");
            ModManagementTests.Jar(Path.Combine(bundle, "dist/suyuncraft-ai-0.5.0.jar"), "suyuncraft_ai");
            ModManagementTests.Jar(Path.Combine(bundle, "dist/fabric-api-0.106.0+1.21.1.jar"), "fabric-api");
            AiIntegration.Install(cfg, "custom-profile", bundle);
            check(File.Exists(Path.Combine(dirA, "mods/suyuncraft-ai.jar")) && !File.Exists(Path.Combine(dirB, "mods/suyuncraft-ai.jar")) && !File.Exists(Path.Combine(source, "mods/suyuncraft-ai.jar")), "AI installation writes only the selected environment's mods");
            check(Json.S(Json.Parse(File.ReadAllText(Path.Combine(dirA, "config/suyuncraft-ai.json"))), "token", "") == tokenA, "AI install preserves the selected world's independent token");
            new ModManagement(cfg, "custom-profile").SetEnabled("third-party.jar", false);
            check(!File.Exists(Path.Combine(dirA, "mods/third-party.jar")) && File.Exists(Path.Combine(dirB, "mods/third-party.jar")) && File.Exists(Path.Combine(source, "mods/third-party.jar")), "Disabling a mod affects neither another world nor source mods");
            var metadata = LaunchPlanner.LoadMerged(cfg.GameDir, "custom-profile");
            metadata["arguments"] = Json.Parse("{\"game\":[\"--gameDir\",\"C:/shared-worlds\",\"--gameDir=C:/other\",\"--username\",\"${auth_player_name}\"]}");
            var plan = LaunchPlanner.Build(cfg, metadata, "custom-profile", 2, "Player", false, 0, 0, false);
            int index = plan.GameArgs.IndexOf("--gameDir");
            check(plan.WorkingDir == dirA && plan.GameArgs.Count(s => s == "--gameDir") == 1 && plan.GameArgs[index + 1] == dirA && !plan.GameArgs.Any(s => s.Contains("shared-worlds") || s.Contains("C:/other")), "Isolated launch enforces working directory and removes hardcoded shared game paths");
            check(plan.JvmArgs.Any(s => s.Contains(Path.Combine(cfg.GameDir, "versions"))) && !plan.JvmArgs.Any(s => s.Contains(Path.Combine(dirB, "mods"))), "Read-only game libraries remain shared while other environment mods stay absent");
            cfg.ExtraJvm = "-Dfabric.addMods=C:/external.jar";
            check(Reject(() => LaunchPlanner.Build(cfg, metadata, "custom-profile", 2, "Player", false, 0, 0, false)), "Save isolation rejects explicit Fabric external mod directory overrides");
            cfg.ExtraJvm = "";
            check(Reject(() => GameDirectories.Resolve(cfg, "1.21.1")), "World cannot accidentally launch on another bound version");
            var service = new LaunchPreparationService(selectJava: (path, major) => { throw new IOException("fixture stops after preflight"); });
            try { service.PrepareAsync(cfg, "custom-profile", null, CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { }
            check(!service.LastReport.Issues.Any(i => i.Code == "MOD_METADATA_INVALID"), "Preparation checks only selected environment mods");
            using (var lease = GameDirectories.Acquire(cfg, "custom-profile"))
            {
                check(Reject(() => GameDirectories.Acquire(cfg, "custom-profile")), "Environment lease prevents simultaneous launch or modification");
                check(Reject(() => new ModManagement(cfg, "custom-profile").SetEnabled("third-party.jar.disabled", true)), "Mod mutation is blocked while environment is in use");
                var otherCfg = cfg.Snapshot(); otherCfg.SaveInstanceId = b.Id;
                using (GameDirectories.Acquire(otherCfg, "custom-profile")) check(true, "Independent worlds have independent operation locks");
            }
            using (GameDirectories.Acquire(cfg, "custom-profile")) check(true, "Lease release allows later sessions");
            string sessionA = Path.Combine(dirA, "saves/world/session.lock"); File.WriteAllText(sessionA, "busy");
            using (new FileStream(sessionA, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check(Reject(() => GameDirectories.Acquire(cfg, "custom-profile")), "World session lock rejects mutation even without a launcher lock");
            Write(dirA, "saves/second/level.dat", "second");
            check(Reject(() => GameDirectories.Acquire(cfg, "custom-profile")), "Multiple worlds in one isolated environment require splitting before launch");
            var split = SaveIsolation.Create(cfg, "custom-profile", "split the copy", Path.Combine(dirA, "saves/second"), true, CancellationToken.None);
            check(File.Exists(Path.Combine(SaveIsolation.GameDirectory(cfg.GameDir, split.Id), "mods/third-party.jar.disabled")), "Splitting a multi-world environment can preserve its mod configuration");
            cfg.SaveInstanceId = "";
            var empty = SaveIsolation.Create(cfg, "custom-profile", "clean instance", null, false, CancellationToken.None);
            string clean = SaveIsolation.GameDirectory(cfg.GameDir, empty.Id);
            check(Directory.Exists(Path.Combine(clean, "saves")) && !Directory.EnumerateFiles(Path.Combine(clean, "mods")).Any() && !Directory.EnumerateFiles(Path.Combine(clean, "config")).Any(), "Clean environment starts without source mods, config or worlds");
            int before = SaveIsolation.List(cfg.GameDir).Count;
            check(Reject(() => SaveIsolation.Create(cfg, "custom-profile", "invalid import", source, true, CancellationToken.None)) && SaveIsolation.List(cfg.GameDir).Count == before, "Failed import never publishes an incomplete environment");
            check(!Directory.EnumerateDirectories(Path.Combine(cfg.GameDir, "suyuncraft-instances"), ".creating-*").Any(), "Failed import removes only its generated staging directory");
            check(Reject(() => SaveIsolation.Create(cfg, "custom-profile", "Cancel", world, true, new CancellationToken(true))) && SaveIsolation.List(cfg.GameDir).Count == before, "Cancelled copy does not publish a partial environment");
            using (new FileStream(Path.Combine(world, "session.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check(Reject(() => SaveIsolation.Create(cfg, "custom-profile", "active worlds", world, false, CancellationToken.None)) && SaveIsolation.List(cfg.GameDir).Count == before, "Import refuses a world held open by another process");
            for (int n = 0; n < 35; n++) Write(world, "region/extra-" + n + ".mca", "original");
            var cancel = new CancelDuringCopy();
            check(Reject(() => SaveIsolation.Create(cfg, "custom-profile", "midwayCancel", world, false, cancel.Source.Token, cancel)) && cancel.Source.IsCancellationRequested && SaveIsolation.List(cfg.GameDir).Count == before && !Directory.EnumerateDirectories(Path.Combine(cfg.GameDir, "suyuncraft-instances"), ".creating-*").Any(), "Cancellation during actual file copy removes staged data and publishes nothing");
            cancel.Source.Dispose();
            check(File.ReadAllText(Path.Combine(world, "region/extra-34.mca")) == "original", "Mid-copy cancellation leaves all original world files intact");
            cfg.SaveInstanceId = "../escape";
            check(Reject(() => GameDirectories.Resolve(cfg, "custom-profile")), "Instance ID rejects traversal instead of falling back to shared saves");
            cfg.SaveInstanceId = Guid.NewGuid().ToString("N");
            check(Reject(() => GameDirectories.Resolve(cfg, "custom-profile")), "Missing instance never silently falls back to shared game directory");
            cfg.SaveInstanceId = b.Id;
            string savedConfig = Path.Combine(root, "settings.json"); cfg.Save(savedConfig);
            check(Config.Load(savedConfig).SaveInstanceId == b.Id, "Selected save environment persists in launcher settings");
            string manifest = Path.Combine(cfg.GameDir, "suyuncraft-instances", b.Id, "instance.json");
            File.WriteAllText(manifest, "{");
            check(Reject(() => GameDirectories.Resolve(cfg, "custom-profile")) && SaveIsolation.List(cfg.GameDir).Any(i => i.Id == b.Id && i.Version == "?"), "Corrupt instance remains visible and cannot launch shared worlds");
        }
    }
}
