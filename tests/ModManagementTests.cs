using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuyunCraft
{
    static class ModManagementTests
    {
        internal static void Jar(string path, string id, string depends = "{}")
        {
            Archive(path, new[] { "fabric.mod.json", "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"version\":\"1\",\"depends\":" + depends + "}" });
        }
        static void Archive(string path, params string[][] entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var entry in entries) using (var writer = new StreamWriter(zip.CreateEntry(entry[0]).Open())) writer.Write(entry[1]);
        }
        static bool Reject(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        internal static Config Fixture(string root)
        {
            FileSafety.WriteText(FileSafety.VersionFile(root, "1.21.1", ".json"), "{\"id\":\"1.21.1\",\"mainClass\":\"net.minecraft.client.main.Main\",\"javaVersion\":{\"majorVersion\":21}}");
            FileSafety.WriteText(FileSafety.VersionFile(root, "custom-profile", ".json"), "{\"id\":\"custom-profile\",\"inheritsFrom\":\"1.21.1\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\"}");
            return new Config { GameDir = root, VersionIsolation = true };
        }
        internal static void Run(string root, Action<bool, string> check)
        {
            var cfg = Fixture(Path.Combine(root, "game"));
            var manager = new ModManagement(cfg, "custom-profile");
            check(manager.Loader == "fabric" && manager.MinecraftVersion == "1.21.1", "Inherited launcher profile resolves actual Minecraft version");
            check(manager.ModsDirectory == Path.Combine(cfg.GameDir, "versions", "custom-profile", "mods"), "General mods use selected isolated game directory");
            string download = Path.Combine(root, "downloads");
            string first = Path.Combine(download, "third-party.jar");
            Jar(first, "third_party", "{\"minecraft\":\"1.21.1\",\"java\":\">=21\"}");
            check(manager.Import(new[] { first }) == 1 && File.Exists(first), "Import third-party jar without moving original download");
            check(manager.List().Single().Id == "third_party" && !Directory.Exists(Path.Combine(cfg.GameDir, "mods")), "Inventory reads third-party metadata without touching shared mods");
            check(!manager.Diagnose().Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED"), "Preflight compares dependencies against base game version");
            var backend = new LaunchPreparationService(selectJava: (path, major) => { throw new IOException("fixture stops before downloads"); });
            try { backend.PrepareAsync(cfg, "custom-profile", null, CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { }
            check(!backend.LastReport.Issues.Any(i => i.Code == "MOD_DEPENDENCY_VERSION_UNSATISFIED"), "Actual launch pipeline uses inherited game version for mod checks");
            string originalHash = FileSafety.Hash(Path.Combine(manager.ModsDirectory, "third-party.jar"), true);
            check(Reject(() => manager.Import(new[] { first })) && FileSafety.Hash(Path.Combine(manager.ModsDirectory, "third-party.jar"), true) == originalHash, "Import never overwrites a same-name jar");
            string copy = Path.Combine(download, "renamed.jar"); Jar(copy, "third_party");
            check(Reject(() => manager.Import(new[] { copy })), "Renamed duplicate Fabric ID rejected");
            manager.SetEnabled("third-party.jar", false);
            check(!manager.List().Single().Enabled && manager.Diagnose().Count == 0, "Disabled jar remains visible and is excluded from launch preflight");
            check(manager.Import(new[] { copy }) == 1, "Can install replacement after disabling old version");
            check(Reject(() => manager.SetEnabled("third-party.jar.disabled", true)), "Enabling duplicate ID refused");
            manager.SetEnabled("renamed.jar", false); manager.SetEnabled("third-party.jar.disabled", true);
            check(manager.List().Count(m => m.Enabled) == 1, "Disable and re-enable preserve both version files");
            check(Reject(() => manager.SetEnabled("../third-party.jar", false)), "Toggle rejects path traversal");
            string valid = Path.Combine(download, "valid.jar"), bad = Path.Combine(download, "bad.jar");
            Jar(valid, "valid_mod"); File.WriteAllText(bad, "broken zip");
            check(Reject(() => manager.Import(new[] { valid, bad })) && !File.Exists(Path.Combine(manager.ModsDirectory, "valid.jar")), "Whole batch validates before first mod is committed");
            string twin = Path.Combine(download, "twin.jar"); Jar(twin, "valid_mod");
            check(Reject(() => manager.Import(new[] { valid, twin })), "Duplicate IDs within batch rejected before commit");
            string clash = Path.Combine(download, "clash.jar"); Jar(clash, "clash_mod");
            Directory.CreateDirectory(Path.Combine(manager.ModsDirectory, "clash.jar"));
            check(Reject(() => manager.Import(new[] { valid, clash })) && !File.Exists(Path.Combine(manager.ModsDirectory, "valid.jar")) && Directory.Exists(Path.Combine(manager.ModsDirectory, "clash.jar")), "Commit failure rolls back earlier imports without deleting existing paths");
            check(!Directory.EnumerateFiles(Path.Combine(manager.GameDirectory, "suyuncraft-mod-staging"), "*", SearchOption.AllDirectories).Any(), "Successful rollback cleans staged jar snapshots");
            string quilt = Path.Combine(download, "quilt.jar");
            Archive(quilt, new[] { "quilt.mod.json", "{\"schema_version\":1,\"quilt_loader\":{\"id\":\"quilt_sample\",\"version\":\"2\",\"metadata\":{\"name\":\"Quilt Sample\"}}}" });
            manager.Import(new[] { quilt });
            check(manager.List().Any(m => m.Id == "quilt_sample" && m.Name == "Quilt Sample"), "Quilt identity displayed from bounded JSON metadata");
            check(manager.Diagnose().Any(i => i.Code == "MOD_LOADER_MISMATCH" && i.Message.Contains("quilt.jar")), "Quilt-only jar warns on Fabric instance");
            string multi = Path.Combine(download, "multi.jar");
            Archive(multi, new[] { "fabric.mod.json", "{\"schemaVersion\":1,\"id\":\"multi_mod\",\"version\":\"1\"}" }, new[] { "META-INF/mods.toml", "modLoader=\"javafml\"" }, new[] { "META-INF/neoforge.mods.toml", "modLoader=\"javafml\"" });
            manager.Import(new[] { multi });
            check(manager.List().Single(m => m.Id == "multi_mod").Loaders.Count == 3 && !manager.Diagnose().Any(i => i.Code == "MOD_LOADER_MISMATCH" && i.Message.Contains("multi.jar")), "Multi-loader jar recognized without false Fabric mismatch");
            string forge = Path.Combine(download, "forge.jar"); Archive(forge, new[] { "META-INF/neoforge.mods.toml", "modLoader=\"javafml\"" });
            manager.Import(new[] { forge });
            check(manager.List().Single(m => m.FileName == "forge.jar").Loaders.Contains("neoforge"), "NeoForge jar can be managed using loader marker only");
            string unknown = Path.Combine(download, "legacy.jar"); Archive(unknown, new[] { "META-INF/MANIFEST.MF", "Manifest-Version: 1.0" });
            manager.Import(new[] { unknown });
            check(manager.List().Any(m => m.FileName == "legacy.jar" && m.Error.Length == 0) && manager.Diagnose().Any(i => i.Code == "MOD_METADATA_UNCHECKED"), "Unknown jar stays manageable with explicit unchecked diagnostic");
            string oversized = Path.Combine(download, "oversized.jar"); Archive(oversized, new[] { "quilt.mod.json", new string('x', 1024 * 1024 + 1) });
            check(Reject(() => manager.Import(new[] { oversized })), "Compressed oversized Quilt metadata refused");
            cfg.VersionIsolation = false;
            check(new ModManagement(cfg, "custom-profile").ModsDirectory == Path.Combine(cfg.GameDir, "mods"), "Shared mode uses shared mods directory");
            string conflicts = Path.Combine(root, "absent-conflicts");
            Jar(Path.Combine(conflicts, "mods", "optional.jar"), "optional_mod");
            string absent = Path.Combine(conflicts, "mods", "breaks.jar");
            Archive(absent, new[] { "fabric.mod.json", "{\"schemaVersion\":1,\"id\":\"breaks_mod\",\"version\":\"1\",\"breaks\":{\"not_installed\":\"*\"},\"conflicts\":{\"also_absent\":\"*\"},\"depends\":{\"fabric\":\"*\"}}" });
            var issues = new ModPreflightCheck().Inspect(new LaunchContext("test", conflicts, "fabric"), CancellationToken.None).ToArray();
            check(!issues.Any(i => i.Message.Contains("not_installed") || i.Message.Contains("also_absent")), "Absent incompatible mods are not misreported as missing dependencies");
            check(issues.Any(i => i.Code == "MOD_DEPENDENCY_UNRESOLVED" && i.Message.Contains("fabric")), "Fabric API legacy ID is not falsely supplied by Fabric Loader");
        }
    }
}
