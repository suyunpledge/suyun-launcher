using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SuyunCraft
{
    sealed class SaveInstance
    {
        public string Id, Name, Version;
        public override string ToString() { return Name + (string.IsNullOrEmpty(Version) ? "" : " · " + Version); }
    }

    static class GameDirectories
    {
        public static string Legacy(Config cfg, string version, bool isolation)
        {
            return isolation ? Path.GetDirectoryName(FileSafety.VersionFile(cfg.GameDir, version, ".json")) : Path.GetFullPath(cfg.GameDir);
        }
        public static string Resolve(Config cfg, string version, bool? isolation = null)
        {
            if (string.IsNullOrEmpty(cfg.SaveInstanceId)) return Legacy(cfg, version, isolation ?? cfg.VersionIsolation);
            var instance = SaveIsolation.Load(cfg.GameDir, cfg.SaveInstanceId);
            if (instance.Version != version) throw new InvalidOperationException("存档环境绑定版本 " + instance.Version + "，请切换对应版本；不能用其他版本直接打开。");
            string dir = SaveIsolation.GameDirectory(cfg.GameDir, instance.Id);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("存档环境目录丢失，已停止操作：" + dir);
            return dir;
        }

        public static FileStream Acquire(Config cfg, string version, bool allowMultipleWorlds = false)
        {
            string dir = Resolve(cfg, version);
            Directory.CreateDirectory(dir);
            try
            {
                var stream = new FileStream(FileSafety.Under(dir, ".suyuncraft-instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try { SaveIsolation.CheckWorlds(dir, !allowMultipleWorlds && !string.IsNullOrEmpty(cfg.SaveInstanceId), true); return stream; }
                catch { stream.Dispose(); throw; }
            }
            catch (IOException ex) { throw new IOException("该环境正在使用或存档仍被游戏占用，请退出游戏后再操作。", ex); }
        }
    }

    static class SaveIsolation
    {
        const string Root = "suyuncraft-instances";
        static void ValidId(string id)
        {
            if (id == null || !Regex.IsMatch(id, @"\A[0-9a-f]{32}\z")) throw new InvalidDataException("无效的存档环境 ID");
        }
        static string InstanceDirectory(string root, string id)
        {
            ValidId(id); return FileSafety.Under(root, Root + "/" + id);
        }
        public static string GameDirectory(string root, string id)
        {
            ValidId(id); return FileSafety.Under(root, Root + "/" + id + "/game");
        }
        public static SaveInstance Load(string root, string id)
        {
            ValidId(id);
            string path = FileSafety.Under(root, Root + "/" + id + "/instance.json");
            string text;
            using (var input = File.OpenRead(path))
            using (var bytes = new MemoryStream())
            {
                byte[] buffer = new byte[4096]; int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (bytes.Length + n > 65536) throw new InvalidDataException("存档环境信息超过 64 KB");
                    bytes.Write(buffer, 0, n);
                }
                text = new UTF8Encoding(false, true).GetString(bytes.ToArray()).TrimStart('\uFEFF');
            }
            var json = Json.D(Json.Parse(text));
            if (json == null || Json.L(json, "schema", 0) != 1 || Json.S(json, "id", "") != id) throw new InvalidDataException("存档环境信息损坏，已停止操作。");
            string name = Json.S(json, "name", ""), version = Json.S(json, "version", "");
            ValidName(name); FileSafety.Id(version);
            return new SaveInstance { Id = id, Name = name, Version = version };
        }
        static void ValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 60 || name.Any(char.IsControl)) throw new ArgumentException("环境名称须为 1–60 个字符，不含控制字符。");
        }
        public static List<SaveInstance> List(string root)
        {
            string directory = FileSafety.Under(root, Root);
            var instances = new List<SaveInstance>();
            if (!Directory.Exists(directory)) return instances;
            var directories = Directory.EnumerateDirectories(directory).Take(1025).ToArray();
            if (directories.Length > 1024) throw new InvalidDataException("存档环境超过 1024 个，请拆分游戏根目录。");
            foreach (string path in directories)
            {
                string id = Path.GetFileName(path);
                if (!Regex.IsMatch(id, @"\A[0-9a-f]{32}\z")) continue;
                try { instances.Add(Load(root, id)); }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is InvalidDataException) && !(ex is ArgumentException) && !(ex is UnauthorizedAccessException) && !(ex is InvalidOperationException)) throw;
                    instances.Add(new SaveInstance { Id = id, Name = "[损坏/不可读] " + id, Version = "?" });
                }
            }
            return instances.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static void CheckWorlds(string dir, bool singleWorld, bool checkRunning)
        {
            string saves = FileSafety.Under(dir, "saves");
            if (!Directory.Exists(saves)) return;
            int worlds = 0;
            var directories = Directory.EnumerateDirectories(saves).Take(1025).ToArray();
            if (directories.Length > 1024) throw new InvalidDataException("存档目录超过 1024 个，请分环境管理。");
            foreach (string folder in directories)
            {
                string safe = FileSafety.Under(dir, "saves/" + Path.GetFileName(folder));
                if (!File.Exists(FileSafety.Under(safe, "level.dat"))) continue;
                if (++worlds > 1 && singleWorld) throw new InvalidDataException("此隔离环境中存在多个存档。请把它们分别复制导入独立环境，再启动。");
                string session = FileSafety.Under(safe, "session.lock");
                if (checkRunning && File.Exists(session)) using (new FileStream(session, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            }
        }

        sealed class CopyState
        {
            public int Files, Directories;
            public long Bytes;
            public CancellationToken Cancellation;
            public IProgress<string> Progress;
        }
        static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("复制不接受符号链接或目录联接：" + path);
        }
        static void CopyDirectory(string source, string target, CopyState state, bool world, int depth = 0)
        {
            state.Cancellation.ThrowIfCancellationRequested(); RejectLink(source);
            if (++state.Directories > 200000) throw new InvalidDataException("副本超过 200000 个目录");
            if (depth > 128) throw new InvalidDataException("目录嵌套超过 128 层");
            Directory.CreateDirectory(target);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                state.Cancellation.ThrowIfCancellationRequested(); RejectLink(entry);
                string name = Path.GetFileName(entry);
                if (world && depth == 0 && name.Equals("session.lock", StringComparison.OrdinalIgnoreCase)) continue;
                string destination = FileSafety.Under(target, name);
                if (Directory.Exists(entry)) CopyDirectory(entry, destination, state, world, depth + 1);
                else CopyFile(entry, destination, state);
            }
        }
        static void CopyFile(string source, string target, CopyState state)
        {
            RejectLink(source);
            if (++state.Files > 200000) throw new InvalidDataException("副本超过 200000 个文件，请拆分存档环境。");
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[65536]; int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    state.Cancellation.ThrowIfCancellationRequested();
                    state.Bytes += n;
                    if (state.Bytes > 256L * 1024 * 1024 * 1024) throw new InvalidDataException("副本超过 256 GB");
                    output.Write(buffer, 0, n);
                }
            }
            if (state.Progress != null && state.Files % 25 == 0) state.Progress.Report("已复制 " + state.Files + " 个文件，" + state.Bytes / 1048576 + " MB…");
        }

        // ---------- v1.10: 重命名 / 删除 / 导出 ----------
        static void WriteInstanceJson(string root, string id, string name, string version)
        {
            ValidId(id); ValidName(name); FileSafety.Id(version);
            FileSafety.WriteText(FileSafety.Under(root, Root + "/" + id + "/instance.json"),
                Json.Dump(new { schema = 1, id = id, name = name, version = version }));
        }

        /// <summary>重命名环境（仅改写元数据，不触碰游戏文件）。</summary>
        public static SaveInstance Rename(string root, string id, string newName)
        {
            var instance = Load(root, id);          // 校验现有元数据可读
            newName = (newName ?? "").Trim();
            WriteInstanceJson(root, id, newName, instance.Version);
            return new SaveInstance { Id = id, Name = newName, Version = instance.Version };
        }

        /// <summary>删除环境目录。拒绝删除当前配置正在使用的环境。</summary>
        public static void Delete(Config cfg, string id, Func<bool> gameRunning)
        {
            ValidId(id);
            if (cfg.SaveInstanceId == id) throw new InvalidOperationException("该环境正在被启动器使用，请先在主页切回「默认目录」再删除。");
            if (gameRunning != null && gameRunning()) throw new InvalidOperationException("请先退出游戏，再删除存档环境。");
            string dir = InstanceDirectory(cfg.GameDir, id);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("环境目录不存在：" + dir);
            // 先占用环境锁，确认没有别的进程在用
            string lockPath = FileSafety.Under(dir, "game/.suyuncraft-instance.lock");
            if (File.Exists(lockPath))
                using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Directory.Delete(dir, true);
        }

        /// <summary>导出环境为 zip（存到启动器数据目录 exports 下），返回 zip 路径。用于备份或迁移。</summary>
        public static string Export(Config cfg, string id, CancellationToken cancellation, IProgress<string> progress = null)
        {
            ValidId(id);
            var instance = Load(cfg.GameDir, id);
            string dir = InstanceDirectory(cfg.GameDir, id);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("环境目录不存在：" + dir);
            string exportRoot = Path.Combine(Config.BaseDir, "exports");
            Directory.CreateDirectory(exportRoot);
            string fileName = "instance-" + id.Substring(0, 8) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
            string zipPath = Path.Combine(exportRoot, fileName);
            string temp = zipPath + ".part";
            long files = 0;
            try
            {
                using (var output = File.Create(temp))
                using (var archive = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var state = new CopyState { Cancellation = cancellation };
                    AddDirectoryToArchive(archive, dir, "instance/", state, progress, ref files);
                }
                if (File.Exists(zipPath)) File.Delete(zipPath);
                File.Move(temp, zipPath);
            }
            finally
            {
                if (File.Exists(temp)) try { File.Delete(temp); } catch { }
            }
            return zipPath;
        }

        static void AddDirectoryToArchive(System.IO.Compression.ZipArchive archive, string source, string prefix, CopyState state, IProgress<string> progress, ref long files)
        {
            state.Cancellation.ThrowIfCancellationRequested();
            RejectLink(source);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                state.Cancellation.ThrowIfCancellationRequested();
                RejectLink(entry);
                string name = Path.GetFileName(entry);
                if (name.Equals(".suyuncraft-instance.lock", StringComparison.OrdinalIgnoreCase)) continue;   // 运行锁不进备份
                string entryName = prefix + name;
                if (Directory.Exists(entry))
                {
                    AddDirectoryToArchive(archive, entry, entryName + "/", state, progress, ref files);
                }
                else
                {
                    var zipEntry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                    using (var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var target = zipEntry.Open())
                        input.CopyTo(target);
                    files++;
                    if (progress != null && files % 50 == 0) progress.Report("已导出 " + files + " 个文件…");
                }
            }
        }
        public static SaveInstance Create(Config cfg, string version, string name, string worldDirectory, bool copyEnvironment, CancellationToken cancellation, IProgress<string> progress = null)
        {
            name = (name ?? "").Trim(); ValidName(name); FileSafety.Id(version);
            LaunchPlanner.LoadMerged(cfg.GameDir, version); // Bind to an installed, readable version.
            cancellation.ThrowIfCancellationRequested();
            string id = Guid.NewGuid().ToString("N");
            string stageRelative = Root + "/.creating-" + id;
            string staging = FileSafety.Under(cfg.GameDir, stageRelative);
            string game = FileSafety.Under(staging, "game");
            Directory.CreateDirectory(game);
            FileStream environmentLock = null, worldLock = null;
            try
            {
                var state = new CopyState { Cancellation = cancellation, Progress = progress };
                if (copyEnvironment)
                {
                    environmentLock = GameDirectories.Acquire(cfg, version, true);
                    string source = GameDirectories.Resolve(cfg, version);
                    foreach (string folder in new[] { "mods", "config", "defaultconfigs", "resourcepacks", "shaderpacks", "kubejs", "scripts" })
                    {
                        string path = FileSafety.Under(source, folder);
                        if (Directory.Exists(path)) CopyDirectory(path, FileSafety.Under(game, folder), state, false);
                    }
                    foreach (string file in new[] { "options.txt", "optionsof.txt", "optionsshaders.txt" })
                    {
                        string path = FileSafety.Under(source, file);
                        if (File.Exists(path)) CopyFile(path, FileSafety.Under(game, file), state);
                    }
                    // A copied AI configuration must not authenticate against the source world's bridge.
                    string aiConfig = FileSafety.Under(game, "config/suyuncraft-ai.json");
                    if (File.Exists(aiConfig))
                    {
                        if (new FileInfo(aiConfig).Length > 65536) throw new InvalidDataException("AI 配置超过 64 KB");
                        var json = Json.D(Json.Parse(File.ReadAllText(aiConfig, Encoding.UTF8)));
                        if (json == null) throw new InvalidDataException("AI 配置不是对象");
                        byte[] token = new byte[32];
                        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(token);
                        json["token"] = BitConverter.ToString(token).Replace("-", "").ToLowerInvariant();
                        FileSafety.WriteText(aiConfig, Json.Dump(json));
                    }
                }
                if (!string.IsNullOrEmpty(worldDirectory))
                {
                    string source = Path.GetFullPath(worldDirectory); RejectLink(source);
                    if (!File.Exists(FileSafety.Under(source, "level.dat"))) throw new InvalidDataException("请选择包含 level.dat 的存档文件夹。");
                    string session = FileSafety.Under(source, "session.lock");
                    if (File.Exists(session)) worldLock = new FileStream(session, FileMode.Open, FileAccess.Read, FileShare.None);
                    if (progress != null) progress.Report("正在复制存档，保留原件…");
                    CopyDirectory(source, FileSafety.Under(game, "saves/world"), state, true);
                }
                foreach (string folder in new[] { "mods", "config", "saves", "resourcepacks" }) Directory.CreateDirectory(FileSafety.Under(game, folder));
                cancellation.ThrowIfCancellationRequested();
                FileSafety.WriteText(FileSafety.Under(staging, "instance.json"), Json.Dump(new { schema = 1, id = id, name = name, version = version }));
                Directory.Move(FileSafety.Under(cfg.GameDir, stageRelative), InstanceDirectory(cfg.GameDir, id));
                return new SaveInstance { Id = id, Name = name, Version = version };
            }
            finally
            {
                if (worldLock != null) worldLock.Dispose();
                if (environmentLock != null) environmentLock.Dispose();
                // The exact generated staging path is revalidated before recursive cleanup.
                string cleanup = FileSafety.Under(cfg.GameDir, stageRelative);
                if (Directory.Exists(cleanup)) Directory.Delete(cleanup, true);
            }
        }
    }
}
