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
            if (instance.Version != version) throw new InvalidOperationException("Save instanceboundversion " + instance.Version + "，switch to that version; other versions cannot open it。");
            string dir = SaveIsolation.GameDirectory(cfg.GameDir, instance.Id);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("Save instancedirectorymissing; operation stopped: " + dir);
            return dir;
        }

        public static FileStream Acquire(Config cfg, string version, bool allowMultipleWorlds = false)
        {
            using (EnterMaintenance(Resolve(cfg, version)))
            {
            string dir = Resolve(cfg, version);
            if (string.IsNullOrEmpty(cfg.SaveInstanceId)) Directory.CreateDirectory(dir);
            try
            {
                var stream = new FileStream(FileSafety.Under(dir, ".suyuncraft-instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try { SaveIsolation.CheckWorlds(dir, !allowMultipleWorlds && !string.IsNullOrEmpty(cfg.SaveInstanceId), true); return stream; }
                catch { stream.Dispose(); throw; }
            }
            catch (IOException ex) { throw new IOException("The instance is in use or the world is still locked; exit the game first.", ex); }
            }
        }

        // A short cross-process gate lives outside the directory being renamed/deleted.
        internal static IDisposable EnterMaintenance(string directory) { return new MaintenanceLease(directory); }
        sealed class MaintenanceLease : IDisposable
        {
            readonly Mutex mutex;
            public MaintenanceLease(string directory)
            {
                string key;
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\', '/').ToUpperInvariant()))).Replace("-", "");
                mutex = new Mutex(false, "Local\\SuyunCraft-environment-" + key);
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) { mutex.Dispose(); throw new IOException("The instance is under maintenance; try again shortlyretry。"); }
            }
            public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
        }
    }

    static class SaveIsolation
    {
        const string Root = "suyuncraft-instances";
        static void ValidId(string id)
        {
            if (id == null || !Regex.IsMatch(id, @"\A[0-9a-f]{32}\z")) throw new InvalidDataException("invalid save instance id");
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
                    if (bytes.Length + n > 65536) throw new InvalidDataException("Save instanceinfoexceeds 64 KB");
                    bytes.Write(buffer, 0, n);
                }
                text = new UTF8Encoding(false, true).GetString(bytes.ToArray()).TrimStart('\uFEFF');
            }
            var json = Json.D(Json.Parse(text));
            if (json == null || Json.L(json, "schema", 0) != 1 || Json.S(json, "id", "") != id) throw new InvalidDataException("Save instanceinfocorrupted; operation stopped.");
            string name = Json.S(json, "name", ""), version = Json.S(json, "version", "");
            ValidName(name); FileSafety.Id(version);
            return new SaveInstance { Id = id, Name = name, Version = version };
        }
        static void ValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 60 || name.Any(char.IsControl)) throw new ArgumentException("Instance namemust be 1–60  characters, no control characters。");
        }
        public static List<SaveInstance> List(string root)
        {
            string directory = FileSafety.Under(root, Root);
            var instances = new List<SaveInstance>();
            if (!Directory.Exists(directory)) return instances;
            var directories = Directory.EnumerateDirectories(directory).Take(1025).ToArray();
            if (directories.Length > 1024) throw new InvalidDataException("Save instanceover 1024; split the game rootdirectory。");
            foreach (string path in directories)
            {
                string id = Path.GetFileName(path);
                if (!Regex.IsMatch(id, @"\A[0-9a-f]{32}\z")) continue;
                try { instances.Add(Load(root, id)); }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is InvalidDataException) && !(ex is ArgumentException) && !(ex is UnauthorizedAccessException) && !(ex is InvalidOperationException)) throw;
                    instances.Add(new SaveInstance { Id = id, Name = "[corrupted/unreadable] " + id, Version = "?" });
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
            if (directories.Length > 1024) throw new InvalidDataException("Over 1024 worlds; manage across instances。");
            foreach (string folder in directories)
            {
                string safe = FileSafety.Under(dir, "saves/" + Path.GetFileName(folder));
                if (!File.Exists(FileSafety.Under(safe, "level.dat"))) continue;
                if (++worlds > 1 && singleWorld) throw new InvalidDataException("Multiple worlds in one instance; import each separately, thenLaunch。");
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
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Copies reject symlinks and junctions：" + path);
        }
        static void CopyDirectory(string source, string target, CopyState state, bool world, int depth = 0)
        {
            state.Cancellation.ThrowIfCancellationRequested(); RejectLink(source);
            if (++state.Directories > 200000) throw new InvalidDataException("Copy exceeds 200000 directories");
            if (depth > 128) throw new InvalidDataException("directorynesting exceeds 128 levels");
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
            if (++state.Files > 200000) throw new InvalidDataException("Copy exceeds 200000 files; splitSave instance。");
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[65536]; int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    state.Cancellation.ThrowIfCancellationRequested();
                    state.Bytes += n;
                    if (state.Bytes > 256L * 1024 * 1024 * 1024) throw new InvalidDataException("Copy exceeds 256 GB");
                    output.Write(buffer, 0, n);
                }
            }
            if (state.Progress != null && state.Files % 25 == 0) state.Progress.Report("Copied " + state.Files + "  file，" + state.Bytes / 1048576 + " MB…");
        }

        // ---------- v1.10: Rename / Delete / export ----------
        static void WriteInstanceJson(string root, string id, string name, string version)
        {
            ValidId(id); ValidName(name); FileSafety.Id(version);
            FileSafety.WriteText(FileSafety.Under(root, Root + "/" + id + "/instance.json"),
                Json.Dump(new { schema = 1, id = id, name = name, version = version }));
        }

        /// <summary>Renameinstance (metadata only, game files untouchedfile）。</summary>
        public static SaveInstance Rename(string root, string id, string newName)
        {
            var instance = Load(root, id);          // Validate that existing metadata is readable
            newName = (newName ?? "").Trim();
            WriteInstanceJson(root, id, newName, instance.Version);
            return new SaveInstance { Id = id, Name = newName, Version = instance.Version };
        }

        /// <summary>Deleteinstance directory. Refuses to delete the active instance。</summary>
        public static void Delete(Config cfg, string id, Func<bool> gameRunning)
        {
            ValidId(id);
            if (cfg.SaveInstanceId == id) throw new InvalidOperationException("This instance is active; switch back to the default directory firstDelete。");
            if (gameRunning != null && gameRunning()) throw new InvalidOperationException("Exit the game before deleting a save instance.");
            string dir = InstanceDirectory(cfg.GameDir, id);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("Instance directory does not exist：" + dir);
            // Hold an exclusive lock while removing the environment from the selectable namespace.
            string lockPath = FileSafety.Under(dir, "game/.suyuncraft-instance.lock");
            string retired = FileSafety.Under(cfg.GameDir, Root + "/.deleting-" + id + "-" + Guid.NewGuid().ToString("N"));
            using (GameDirectories.EnterMaintenance(GameDirectory(cfg.GameDir, id)))
            {
                using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                CheckWorlds(GameDirectory(cfg.GameDir, id), false, true);
                Directory.Move(dir, retired);
            }
            try { Directory.Delete(retired, true); }
            catch (IOException ex) { throw new IOException("Instance removed from the list; some files were locked and remain in this directory：" + retired, ex); }
        }

        /// <summary>Export the instance as a zip into the launcher exports directory; returns the zip path。</summary>
        public static string Export(Config cfg, string id, CancellationToken cancellation, IProgress<string> progress = null)
        {
            ValidId(id);
            var instance = Load(cfg.GameDir, id);
            cancellation.ThrowIfCancellationRequested();
            string dir = InstanceDirectory(cfg.GameDir, id);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("Instance directory does not exist：" + dir);
            string exportRoot = Path.Combine(Config.BaseDir, "exports");
            Directory.CreateDirectory(exportRoot);
            string fileName = "instance-" + id.Substring(0, 8) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".zip";
            string zipPath = Path.Combine(exportRoot, fileName);
            string temp = zipPath + ".part";
            long files = 0;
            var snapshot = cfg.Snapshot(); snapshot.SaveInstanceId = id;
            var worldLocks = new List<FileStream>();
            try
            {
                using (GameDirectories.Acquire(snapshot, instance.Version, true))
                {
                string saves = FileSafety.Under(GameDirectory(cfg.GameDir, id), "saves");
                if (Directory.Exists(saves))
                    foreach (string world in Directory.EnumerateDirectories(saves))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string session = FileSafety.Under(saves, Path.GetFileName(world) + "/session.lock");
                        if (File.Exists(session)) worldLocks.Add(new FileStream(session, FileMode.Open, FileAccess.Read, FileShare.None));
                    }
                using (var output = File.Create(temp))
                using (var archive = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var state = new CopyState { Cancellation = cancellation };
                    AddDirectoryToArchive(archive, dir, "instance/", state, progress, ref files);
                }
                cancellation.ThrowIfCancellationRequested();
                File.Move(temp, zipPath);
                }
            }
            finally
            {
                foreach (var worldLock in worldLocks) worldLock.Dispose();
                if (File.Exists(temp)) try { File.Delete(temp); } catch { }
            }
            return zipPath;
        }

        static void AddDirectoryToArchive(System.IO.Compression.ZipArchive archive, string source, string prefix, CopyState state, IProgress<string> progress, ref long files, int depth = 0)
        {
            state.Cancellation.ThrowIfCancellationRequested();
            RejectLink(source);
            if (++state.Directories > 200000 || depth > 128) throw new InvalidDataException("Backup has too many directories or nesting exceeds 128");
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                state.Cancellation.ThrowIfCancellationRequested();
                RejectLink(entry);
                string name = Path.GetFileName(entry);
                if (name.Equals(".suyuncraft-instance.lock", StringComparison.OrdinalIgnoreCase) || name.Equals("session.lock", StringComparison.OrdinalIgnoreCase)) continue;
                string entryName = prefix + name;
                if (Directory.Exists(entry))
                {
                    archive.CreateEntry(entryName + "/"); // Preserve empty mod/config/world directories.
                    AddDirectoryToArchive(archive, entry, entryName + "/", state, progress, ref files, depth + 1);
                }
                else
                {
                    var zipEntry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                    using (var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var target = zipEntry.Open())
                    {
                        byte[] buffer = new byte[65536]; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            state.Cancellation.ThrowIfCancellationRequested();
                            state.Bytes += count;
                            if (state.Bytes > 256L * 1024 * 1024 * 1024) throw new InvalidDataException("Backup exceeds 256 GB");
                            target.Write(buffer, 0, count);
                        }
                    }
                    if (++files > 200000) throw new InvalidDataException("Backup exceeds 200000  file");
                    if (progress != null && files % 50 == 0) progress.Report("Exported " + files + "  file…");
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
                        if (new FileInfo(aiConfig).Length > 65536) throw new InvalidDataException("AI config exceeds 64 KB");
                        var json = Json.D(Json.Parse(File.ReadAllText(aiConfig, Encoding.UTF8)));
                        if (json == null) throw new InvalidDataException("AI config is not an object");
                        byte[] token = new byte[32];
                        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(token);
                        json["token"] = BitConverter.ToString(token).Replace("-", "").ToLowerInvariant();
                        FileSafety.WriteText(aiConfig, Json.Dump(json));
                    }
                }
                if (!string.IsNullOrEmpty(worldDirectory))
                {
                    string source = Path.GetFullPath(worldDirectory); RejectLink(source);
                    if (!File.Exists(FileSafety.Under(source, "level.dat"))) throw new InvalidDataException("Pick a world folder containing level.dat。");
                    string session = FileSafety.Under(source, "session.lock");
                    if (File.Exists(session)) worldLock = new FileStream(session, FileMode.Open, FileAccess.Read, FileShare.None);
                    if (progress != null) progress.Report("Copying the world, original kept…");
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
