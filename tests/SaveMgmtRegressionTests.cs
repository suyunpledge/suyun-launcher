// SaveIsolation v1.10 新增 API 的离线回归：Rename / Delete / Export
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace SuyunCraft
{
    static class SaveMgmtRegressionTests
    {
        static string GameRoot;
        static Config Cfg;

        static SaveInstance MakeInstance(string name)
        {
            var cfg = Cfg.Snapshot();
            return SaveIsolation.Create(cfg, "test-ver", name, null, false, CancellationToken.None);
        }

        static void InstallFakeVersion(string gameDir)
        {
            string meta = FileSafety.VersionFile(gameDir, "test-ver", ".json");
            FileSafety.WriteText(meta, "{\"id\":\"test-ver\",\"mainClass\":\"net.minecraft.client.main.Main\",\"javaVersion\":{\"majorVersion\":21},\"arguments\":{\"jvm\":[],\"game\":[]}}");
            FileSafety.WriteText(FileSafety.VersionFile(gameDir, "test-ver", ".jar"), "client");
        }

        internal static void Run(string root, Action<bool, string> check)
        {
            GameRoot = Path.Combine(root, "game");
            Directory.CreateDirectory(GameRoot);
            InstallFakeVersion(GameRoot);
            Cfg = new Config { GameDir = GameRoot, UserName = "tester", JavaPath = "fake.exe", MemoryGb = 2 };

            // ---- Rename ----
            var a = MakeInstance("环境A");
            var renamed = SaveIsolation.Rename(GameRoot, a.Id, "环境A-改名");
            check(renamed.Name == "环境A-改名", "Rename updates returned name");
            var reloaded = SaveIsolation.Load(GameRoot, a.Id);
            check(reloaded.Name == "环境A-改名", "Rename persists to instance.json");
            bool badName = false;
            try { SaveIsolation.Rename(GameRoot, a.Id, "  "); } catch (ArgumentException) { badName = true; }
            check(badName, "Rename rejects blank names");

            // ---- Export ----
            string marker = SaveIsolation.GameDirectory(GameRoot, a.Id);
            Directory.CreateDirectory(Path.Combine(marker, "saves/world"));
            File.WriteAllText(Path.Combine(marker, "saves/world", "level.dat"), "level");
            File.WriteAllText(Path.Combine(marker, "config", "x.txt"), "cfg");
            Directory.CreateDirectory(Path.Combine(marker, "config"));
            File.WriteAllText(Path.Combine(marker, "config", "x.txt"), "cfg");
            string zipPath = SaveIsolation.Export(Cfg, a.Id, CancellationToken.None);
            check(File.Exists(zipPath) && new FileInfo(zipPath).Length > 0, "Export produces a non-empty zip");
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                check(archive.Entries.Any(e => e.FullName == "instance/game/saves/world/level.dat"), "Export zip contains world file");
                check(archive.Entries.Any(e => e.FullName == "instance/instance.json"), "Export zip contains metadata");
                check(!archive.Entries.Any(e => e.FullName.EndsWith(".suyuncraft-instance.lock", StringComparison.OrdinalIgnoreCase)), "Export excludes the runtime lock");
            }

            // ---- Delete ----
            var b = MakeInstance("待删除");
            SaveIsolation.Delete(Cfg, b.Id, null);
            check(!Directory.Exists(Path.Combine(GameRoot, "suyuncraft-instances", b.Id)), "Delete removes the directory");
            bool missing = false;
            try { SaveIsolation.Delete(Cfg, b.Id, null); } catch (DirectoryNotFoundException) { missing = true; }
            check(missing, "Delete on missing environment reports clearly");

            // 当前使用中的环境拒绝删除
            var active = MakeInstance("使用中");
            var cfgUsing = Cfg.Snapshot(); cfgUsing.SaveInstanceId = active.Id;
            bool refused = false;
            try { SaveIsolation.Delete(cfgUsing, active.Id, null); } catch (InvalidOperationException) { refused = true; }
            check(refused, "Delete refuses the active environment");
            check(Directory.Exists(Path.Combine(GameRoot, "suyuncraft-instances", active.Id)), "Refused delete keeps the directory");

            // 游戏运行中拒绝删除
            bool runningRefused = false;
            try { SaveIsolation.Delete(Cfg, active.Id, delegate { return true; }); } catch (InvalidOperationException) { runningRefused = true; }
            check(runningRefused, "Delete refuses while the game is running");

            // 列表仍能读出全部合法环境
            var all = SaveIsolation.List(GameRoot);
            check(all.Any(i => i.Id == a.Id) && all.Any(i => i.Id == active.Id) && !all.Any(i => i.Id == b.Id), "List reflects rename and delete");
        }
    }
}
