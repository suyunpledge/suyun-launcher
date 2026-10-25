using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace SuyunCraft
{
    sealed class ModFileInfo
    {
        public string FileName, Id = "", Name = "", Version = "", Error = "";
        public bool Enabled;
        public readonly List<string> Loaders = new List<string>();
    }

    // Metadata inspection and file operations only. Never execute a downloaded jar.
    sealed class ModManagement
    {
        public readonly string GameDirectory, ModsDirectory, Loader, MinecraftVersion;
        public readonly int JavaMajor;
        readonly Config configuration;
        readonly string versionId;
        public ModManagement(Config cfg, string version)
        {
            configuration = cfg.Snapshot(); versionId = version;
            var metadata = LaunchPlanner.LoadMerged(cfg.GameDir, version);
            Loader = new MinecraftLoaderDetector().Detect(metadata);
            MinecraftVersion = GameVersion(metadata, version);
            JavaMajor = (int)Json.L(Json.O(metadata, "javaVersion"), "majorVersion", 8);
            GameDirectory = GameDirectories.Resolve(cfg, version);
            ModsDirectory = FileSafety.Under(GameDirectory, "mods");
        }

        internal static string GameVersion(Dictionary<string, object> metadata, string fallback)
        {
            string version = Json.S(metadata, "clientVersion", "");
            return version.Length == 0 ? Json.S(metadata, "jar", fallback) : version;
        }

        string ModPath(string name) { return FileSafety.Under(GameDirectory, "mods/" + FileSafety.Id(name)); }

        internal static string ReadMetadata(ZipArchive archive, string name)
        {
            var entries = archive.Entries.Where(e => e.FullName == name).ToArray();
            if (entries.Length != 1) throw new InvalidDataException("missing or duplicate  " + name);
            if (entries[0].Length > 1024 * 1024) throw new InvalidDataException("modmetadata exceeds 1 MB");
            using (var input = entries[0].Open())
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[8192]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > 1024 * 1024) throw new InvalidDataException("modmetadata exceeds 1 MB");
                    output.Write(buffer, 0, count);
                }
                return new UTF8Encoding(false, true).GetString(output.ToArray()).TrimStart('\uFEFF');
            }
        }

        internal static List<string> DetectLoaders(ZipArchive archive)
        {
            var result = new List<string>();
            if (archive.GetEntry("fabric.mod.json") != null) result.Add("fabric");
            if (archive.GetEntry("quilt.mod.json") != null) result.Add("quilt");
            if (archive.GetEntry("META-INF/mods.toml") != null || archive.GetEntry("mcmod.info") != null) result.Add("forge");
            if (archive.GetEntry("META-INF/neoforge.mods.toml") != null) result.Add("neoforge");
            return result;
        }

        internal static bool RecognizesLoader(IList<string> loaders, string loader)
        {
            // Quilt can load many Fabric mods; mods.toml is also used by older NeoForge.
            return loaders.Contains(loader) || loader == "quilt" && loaders.Contains("fabric") || loader == "neoforge" && loaders.Contains("forge");
        }

        internal static ModFileInfo ReadFile(string path, string name)
        {
            var info = new ModFileInfo { FileName = name, Name = name, Enabled = name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) };
            try
            {
                if (new FileInfo(path).Length > 512L * 1024 * 1024) throw new InvalidDataException("mod exceeds 512 MB; skipped");
                using (var archive = ZipFile.OpenRead(path))
                {
                    if (archive.Entries.Count > 100000) throw new InvalidDataException("ZIP entry count exceeds the limit");
                    info.Loaders.AddRange(DetectLoaders(archive));
                    if (info.Loaders.Contains("fabric"))
                    {
                        var mod = new FabricModMetadataReader().Read(archive);
                        if (mod != null) { info.Id = mod.Id; info.Version = mod.Version; info.Name = mod.Id; }
                    }
                    else if (info.Loaders.Contains("quilt"))
                    {
                        var json = Json.D(Json.Parse(ReadMetadata(archive, "quilt.mod.json")));
                        if (json == null) throw new InvalidDataException("Quilt metadata must be an object");
                        if (Json.L(json, "schema_version", 0) == 1)
                        {
                            var loader = Json.O(json, "quilt_loader");
                            info.Id = Json.S(loader, "id", ""); info.Version = Json.S(loader, "version", "");
                            if (!System.Text.RegularExpressions.Regex.IsMatch(info.Id, @"\A[a-z][a-z0-9_-]{1,63}\z") || string.IsNullOrWhiteSpace(info.Version)) throw new InvalidDataException("invalid Quilt mod id/version");
                            info.Name = Json.S(Json.O(loader, "metadata"), "name", info.Id);
                        }
                    }
                    // TOML and legacy Forge metadata are markers only, not guessed dependency rules.
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is InvalidDataException) && !(ex is ArgumentException) &&
                    !(ex is InvalidOperationException) && !(ex is UnauthorizedAccessException)) throw;
                info.Error = ex.Message;
            }
            return info;
        }

        public List<ModFileInfo> List()
        {
            string dir = FileSafety.Under(GameDirectory, "mods");
            if (!Directory.Exists(dir)) return new List<ModFileInfo>();
            var paths = Directory.EnumerateFiles(dir).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).Take(2049).ToArray();
            if (paths.Length > 2048) throw new InvalidDataException("modOver 2048; manage across instances");
            return paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => {
                string name = Path.GetFileName(p);
                try { return ReadFile(FileSafety.Under(dir, name), name); }
                catch (InvalidDataException ex) { return new ModFileInfo { FileName = name, Name = name, Error = ex.Message, Enabled = name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) }; }
            }).ToList();
        }

        public List<LaunchIssue> Diagnose()
        {
            return new ModPreflightCheck().Inspect(new LaunchContext("selected", GameDirectory, Loader), CancellationToken.None, MinecraftVersion, JavaMajor).ToList();
        }

        public int Import(IEnumerable<string> sources)
        {
            using (GameDirectories.Acquire(configuration, versionId)) return ImportCore(sources);
        }
        int ImportCore(IEnumerable<string> sources)
        {
            var files = sources.Take(129).ToArray();
            if (files.Length == 0) return 0;
            if (files.Length > 128) throw new InvalidDataException("Import at most 128 files at oncemod");
            var existing = List();
            if (existing.Count + files.Length > 2048) throw new InvalidDataException("modcount would exceed 2048");
            var names = new HashSet<string>(existing.Select(m => m.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? m.FileName.Substring(0, m.FileName.Length - 9) : m.FileName), StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(existing.Where(m => m.Enabled && m.Id.Length > 0).Select(m => m.Id), StringComparer.Ordinal);
            string staging = FileSafety.Under(GameDirectory, "suyuncraft-mod-staging/" + Guid.NewGuid().ToString("N"));
            var prepared = new List<KeyValuePair<string, string>>();
            var committed = new List<string>();
            bool preserveStaging = false;
            Directory.CreateDirectory(staging);
            long total = 0;
            try
            {
                foreach (string source in files)
                {
                    string name = FileSafety.Id(Path.GetFileName(source));
                    if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("accepts only  .jar mod：" + name);
                    if (!names.Add(name)) throw new InvalidDataException("a mod with this name exists (possibly already Disabled）：" + name);
                    string temp = FileSafety.Under(staging, name);
                    prepared.Add(new KeyValuePair<string, string>(temp, name));
                    // Copy a bounded snapshot, then validate that exact snapshot before committing.
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        long size = 0; byte[] buffer = new byte[65536]; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            size += count; total += count;
                            if (size > 512L * 1024 * 1024 || total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Per-mod limit 512 MB; per-batch limit  2 GB");
                            output.Write(buffer, 0, count);
                        }
                    }
                    var mod = ReadFile(temp, name);
                    if (mod.Error.Length > 0) throw new InvalidDataException(name + "：" + mod.Error);
                    if (mod.Id.Length > 0 && !ids.Add(mod.Id)) throw new InvalidDataException("duplicate mod id " + mod.Id + "；Disable the old one first: version");
                }
                Directory.CreateDirectory(FileSafety.Under(GameDirectory, "mods"));
                foreach (var pair in prepared)
                {
                    string destination = ModPath(pair.Value);
                    File.Move(pair.Key, destination); // Never overwrite a file that appeared after inspection.
                    committed.Add(destination);
                }
                return committed.Count;
            }
            catch (Exception original)
            {
                // Return our newly committed files to staging before cleaning; existing jars are untouched.
                var failures = new List<Exception>();
                foreach (string path in committed.AsEnumerable().Reverse())
                {
                    try { File.Move(ModPath(Path.GetFileName(path)), FileSafety.Under(staging, Path.GetFileName(path))); }
                    catch (Exception rollback) { failures.Add(rollback); }
                }
                if (failures.Count > 0)
                {
                    preserveStaging = true; failures.Insert(0, original);
                    throw new IOException("Some imported files could not roll back; check mods and stagingdirectory：" + staging, new AggregateException(failures));
                }
                throw;
            }
            finally
            {
                if (!preserveStaging)
                {
                    foreach (var pair in prepared) { string path = FileSafety.Under(staging, pair.Value); if (File.Exists(path)) File.Delete(path); }
                    if (Directory.Exists(staging)) Directory.Delete(staging, false);
                }
            }
        }

        public void SetEnabled(string fileName, bool enabled)
        {
            using (GameDirectories.Acquire(configuration, versionId)) SetEnabledCore(fileName, enabled);
        }
        void SetEnabledCore(string fileName, bool enabled)
        {
            FileSafety.Id(fileName);
            bool current = fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            if (!current && !fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("is not managedmodfile");
            if (current == enabled) return;
            string source = ModPath(fileName);
            string targetName = enabled ? fileName.Substring(0, fileName.Length - 9) : fileName + ".disabled";
            if (enabled)
            {
                var mod = ReadFile(source, targetName);
                if (mod.Error.Length > 0) throw new InvalidDataException(mod.Error);
                if (mod.Id.Length > 0 && List().Any(m => m.Enabled && m.Id == mod.Id)) throw new InvalidDataException("already enabled: moddeclares ID " + mod.Id + "，Disable the old one first: version");
            }
            File.Move(source, ModPath(targetName));
        }
    }
}
