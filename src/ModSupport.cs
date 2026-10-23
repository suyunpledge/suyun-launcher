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
            if (fabricEntries.Length != 1) throw new InvalidDataException("重复的 fabric.mod.json");
            var entry = fabricEntries[0];
            if (entry.Length > MaxMetadataBytes) throw new InvalidDataException("模组元数据超过 1 MB");
            string text;
            using (var input = entry.Open())
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[8192]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > MaxMetadataBytes) throw new InvalidDataException("模组元数据超过 1 MB");
                    output.Write(buffer, 0, count);
                }
                text = new UTF8Encoding(false, true).GetString(output.ToArray()).TrimStart('\uFEFF');
            }
            var json = Json.D(Json.Parse(text));
            if (json == null) throw new InvalidDataException("模组元数据必须是 JSON 对象");
            // Schema 0 and future formats need their own reader, not a guessed interpretation.
            if (Json.L(json, "schemaVersion", 0) != 1) return null;
            string id = Json.S(json, "id", "");
            if (!Regex.IsMatch(id, @"\A[a-z][a-z0-9_-]{1,63}\z")) throw new InvalidDataException("无效的 Fabric 模组 ID");
            string version = Json.S(json, "version", "");
            if (string.IsNullOrWhiteSpace(version)) throw new InvalidDataException("缺少模组版本");
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
                    context.Loader == "vanilla" ? "当前版本未检测到模组加载器，mods 内的模组可能不会生效。" : "当前加载器的模组预检尚未实现：" + context.Loader));
                return issues;
            }
            if (paths.Length > 2048)
            {
                issues.Add(new LaunchIssue("MOD_SCAN_LIMIT", "模组超过 2048 个，已跳过预检，请交由加载器验证。"));
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
                    { issues.Add(new LaunchIssue("MOD_SCAN_LIMIT", name + " 超过 512 MB，未预检。")); continue; }
                    using (var archive = ZipFile.OpenRead(safePath))
                    {
                        if (archive.Entries.Count > 100000) throw new InvalidDataException("ZIP 条目数超过限制");
                        ModMetadata mod = null;
                        foreach (var reader in readers) { mod = reader.Read(archive); if (mod != null) break; }
                        if (mod == null)
                        {
                            bool forge = archive.GetEntry("META-INF/mods.toml") != null || archive.GetEntry("META-INF/neoforge.mods.toml") != null;
                            issues.Add(new LaunchIssue(forge ? "MOD_LOADER_MISMATCH" : "MOD_METADATA_UNCHECKED",
                                name + (forge ? " 含 Forge/NeoForge 元数据，尚未确认其 Fabric 兼容性。" : " 未识别到受支持的 Fabric 元数据。")));
                            continue;
                        }
                        mod.FileName = name;
                        if (!mod.ClientEnabled) { issues.Add(new LaunchIssue("MOD_SERVER_ONLY", name + " 为服务端模组，客户端预检已跳过。")); continue; }
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
                    issues.Add(new LaunchIssue("MOD_METADATA_INVALID", name + " 无法读取模组元数据：" + ex.Message, true));
                }
            }
            foreach (var duplicate in mods.GroupBy(m => m.Id).Where(g => g.Count() > 1))
                issues.Add(new LaunchIssue("MOD_DUPLICATE_ID", "多个顶层模组声明相同 ID " + duplicate.Key + "：" +
                    string.Join(", ", duplicate.Select(m => m.FileName)) + "。请检查是否重复安装。"));
            // 完整依赖解析：版本范围、breaks/conflicts、嵌套 jar 提供者、加载器覆盖配置
            var overrides = LoaderOverrides.Load(context.GameDirectory, issues);
            foreach (LaunchIssue r in ModDependencyResolver.Resolve(mods, minecraftVersion, javaMajor, overrides))
                issues.Add(r);
            if (mods.Any(m => m.HasUnparsableConstraints))
                issues.Add(new LaunchIssue("MOD_RESOLUTION_PARTIAL", "部分模组使用了无法解析的版本约束格式，相关兼容性由 Fabric Loader 最终判断。"));
            return issues;
        }
    }
}
