using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuyunCraft
{
    class LocalVer
    {
        public string Id;
        public bool IsFabric;
        public string Loader = "unknown";
        public override string ToString() { return (Loader == "vanilla" ? "" : "[" + Loader + "] ") + Id; }
    }

    static class LaunchPlanner
    {
        // ---------- 继承合并（Fabric 的 inheritsFrom） ----------
        public static Dictionary<string, object> LoadMerged(string gameDir, string id)
        {
            return LoadMerged(gameDir, id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        static Dictionary<string, object> LoadMerged(string gameDir, string id, HashSet<string> chain)
        {
            string path = FileSafety.VersionFile(gameDir, id, ".json");
            if (chain.Count >= 32 || !chain.Add(id)) throw new InvalidDataException("版本继承存在循环或层级过深：" + id);
            if (!File.Exists(path)) throw new FileNotFoundException("本地不存在版本 " + id, path);
            var json = Json.D(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
            if (json == null) throw new InvalidDataException("版本 JSON 解析失败：" + id);
            string parentId = Json.S(json, "inheritsFrom", "");
            if (parentId.Length > 0) json = Merge(LoadMerged(gameDir, parentId, chain), json);
            else if (!Json.Has(json, "jar")) json["jar"] = id;
            chain.Remove(id);
            return json;
        }

        static Dictionary<string, object> Merge(Dictionary<string, object> parent, Dictionary<string, object> child)
        {
            Dictionary<string, object> r = new Dictionary<string, object>(parent);
            foreach (KeyValuePair<string, object> kv in child)
            {
                if (kv.Key == "inheritsFrom") continue;
                if (kv.Key == "id") { r["id"] = kv.Value; continue; }
                if (kv.Key == "libraries")
                {
                    // child 优先，同名去重
                    List<object> libs = new List<object>(Json.A(kv.Value) ?? new object[0]);
                    HashSet<string> names = new HashSet<string>();
                    foreach (object l in libs) { string n = Json.S(l, "name", ""); if (n.Length > 0) names.Add(LibraryKey(n)); }
                    object[] pl = Json.A(Json.O(parent, "libraries"));
                    if (pl != null) foreach (object l in pl) { string n = Json.S(l, "name", ""); if (!names.Contains(LibraryKey(n))) libs.Add(l); }
                    r["libraries"] = libs.ToArray();
                    continue;
                }
                if (kv.Key == "arguments")
                {
                    var cd = Json.D(kv.Value); var pd = Json.D(Json.O(parent, "arguments"));
                    Dictionary<string, object> merged = pd != null ? new Dictionary<string, object>(pd) : new Dictionary<string, object>();
                    foreach (KeyValuePair<string, object> c in cd)
                    {
                        if (merged.ContainsKey(c.Key) && Json.A(c.Value) != null && Json.A(merged[c.Key]) != null)
                        {
                            List<object> lst = new List<object>(Json.A(merged[c.Key]));
                            lst.AddRange(Json.A(c.Value));
                            merged[c.Key] = lst.ToArray();
                        }
                        else merged[c.Key] = c.Value;
                    }
                    r["arguments"] = merged;
                    continue;
                }
                r[kv.Key] = kv.Value;
            }
            return r;
        }

        // ---------- 规则判断 ----------
        public static bool RulesPass(object rulesObj)
        {
            return RulesPass(rulesObj, new Dictionary<string, bool>());
        }

        static bool RulesPass(object rulesObj, Dictionary<string, bool> features)
        {
            object[] rules = Json.A(rulesObj);
            if (rules == null) return true;
            bool allowed = false;
            foreach (object rule in rules)
            {
                bool match = true;
                var os = Json.D(Json.O(rule, "os"));
                if (os != null)
                {
                    if (Json.S(os, "name", "windows") != "windows") match = false;
                    string arch = Environment.Is64BitOperatingSystem ? "amd64" : "x86";
                    string pattern = Json.S(os, "arch", "");
                    if (pattern.Length > 0 && !RegexMatch(arch, pattern)) match = false;
                    pattern = Json.S(os, "version", "");
                    if (pattern.Length > 0 && !RegexMatch(Environment.OSVersion.Version.ToString(), pattern)) match = false;
                }
                var requested = Json.D(Json.O(rule, "features"));
                if (requested != null)
                    foreach (var entry in requested)
                    {
                        bool actual;
                        features.TryGetValue(entry.Key, out actual);
                        if (!(entry.Value is bool) || (bool)entry.Value != actual) match = false;
                    }
                if (match) allowed = Json.S(rule, "action", "") == "allow";
            }
            return allowed;
        }

        static bool RegexMatch(string value, string pattern)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(value, pattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        static string LibraryKey(string name)
        {
            string[] parts = name.Split(':');
            return parts.Length < 3 ? name : parts[0] + ":" + parts[1] + (parts.Length > 3 ? ":" + parts[3] : "");
        }

        public static string MavenPath(string name)
        {
            string[] ext = name.Split('@');
            string[] parts = ext[0].Split(':');
            if (parts.Length < 3 || parts.Length > 4 || ext.Length > 2) throw new InvalidDataException("无效 Maven 坐标：" + name);
            foreach (string part in parts) FileSafety.Id(part);
            string extension = ext.Length == 2 ? FileSafety.Id(ext[1]) : "jar";
            return parts[0].Replace('.', '/') + "/" + parts[1] + "/" + parts[2] + "/" +
                parts[1] + "-" + parts[2] + (parts.Length == 4 ? "-" + parts[3] : "") + "." + extension;
        }

        class LibraryFile
        {
            public string Path, Url, Sha1;
            public long Size;
            public bool Native;
            public List<string> Excludes = new List<string>();
        }

        static IEnumerable<LibraryFile> LibraryFiles(Dictionary<string, object> version)
        {
            foreach (object lib in Json.A(Json.O(version, "libraries")) ?? new object[0])
            {
                if (!RulesPass(Json.O(lib, "rules"))) continue;
                string name = Json.S(lib, "name", "");
                var downloads = Json.D(Json.O(lib, "downloads"));
                var natives = Json.D(Json.O(lib, "natives"));
                var artifact = Json.D(Json.O(downloads, "artifact"));
                if (artifact != null || (downloads == null && natives == null))
                    yield return DescribeLibrary(lib, artifact, name, false);
                if (natives == null || !natives.ContainsKey("windows")) continue;
                string classifier = Json.S(natives, "windows", "").Replace("$" + "{arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                var native = Json.D(Json.O(Json.O(downloads, "classifiers"), classifier));
                if (downloads != null && native == null) throw new InvalidDataException("缺少 Windows native：" + name + ":" + classifier);
                var file = DescribeLibrary(lib, native, name + ":" + classifier, true);
                foreach (object exclude in Json.A(Json.O(Json.O(lib, "extract"), "exclude")) ?? new object[0])
                    if (exclude is string) file.Excludes.Add((string)exclude);
                yield return file;
            }
        }

        static LibraryFile DescribeLibrary(object lib, object artifact, string coordinate, bool native)
        {
            string relative = artifact != null ? Json.S(artifact, "path", "") : MavenPath(coordinate);
            string baseUrl = Json.S(lib, "url", "https://libraries.minecraft.net/").TrimEnd('/') + "/";
            return new LibraryFile {
                Path = relative, Url = artifact != null ? Json.S(artifact, "url", baseUrl + relative) : baseUrl + relative,
                Sha1 = Json.S(artifact, "sha1", null), Size = Json.L(artifact, "size", 0), Native = native
            };
        }

        public static string LibsDir(string gameDir) { return Path.Combine(gameDir, "libraries"); }
        public static string AssetsDir(string gameDir) { return Path.Combine(gameDir, "assets"); }

        // ---------- 收集缺失文件（启动前校验 / 安装） ----------
        public static List<DlItem> CollectMissing(string gameDir, Dictionary<string, object> version, string id, FileSafety.VerifyCache cache = null)
        {
            var missing = new List<DlItem>();
            string jarId = Json.S(version, "jar", id);
            string jar = FileSafety.VersionFile(gameDir, jarId, ".jar");
            var client = Json.O(Json.O(version, "downloads"), "client");
            if (client != null) AddDownload(missing, jar, client, "客户端 " + jarId, cache);
            else if (!File.Exists(jar)) throw new FileNotFoundException("缺少客户端 jar，请安装原版 " + jarId, jar);
            foreach (var lib in LibraryFiles(version))
            {
                string path = FileSafety.Under(gameDir, "libraries/" + lib.Path, cache);
                AddDownload(missing, path, lib.Url, lib.Sha1, lib.Size, lib.Path, cache);
            }
            var index = Json.O(version, "assetIndex");
            if (index != null)
            {
                string indexId = FileSafety.Id(Json.S(index, "id", Json.S(version, "assets", "legacy")));
                string path = FileSafety.Under(gameDir, "assets/indexes/" + indexId + ".json", cache);
                AddDownload(missing, path, index, "资源索引 " + indexId, cache);
                if (FileSafety.Matches(path, Json.S(index, "sha1", null), Json.L(index, "size", 0), cache))
                    AddMissingAssets(missing, gameDir, path, indexId, cache);
            }
            var log = Json.O(Json.O(Json.O(version, "logging"), "client"), "file");
            if (log != null) AddDownload(missing, FileSafety.Under(gameDir, "assets/log_configs/" +
                FileSafety.Id(Json.S(log, "id", "")), cache), log, "日志配置", cache);
            return missing.GroupBy(item => item.Dest, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        }

        static void AddDownload(List<DlItem> list, string path, object metadata, string description, FileSafety.VerifyCache cache = null)
        {
            AddDownload(list, path, Json.S(metadata, "url", ""), Json.S(metadata, "sha1", null), Json.L(metadata, "size", 0), description, cache);
        }

        static void AddDownload(List<DlItem> list, string path, string url, string sha1, long size, string description, FileSafety.VerifyCache cache = null)
        {
            if (!FileSafety.Matches(path, sha1, size, cache))
                list.Add(new DlItem { Dest = path, Url = url, Sha1 = sha1, Size = size, Desc = description });
        }

        static void AddMissingAssets(List<DlItem> list, string gameDir, string indexPath, string indexId, FileSafety.VerifyCache cache)
        {
            var index = Json.D(Json.Parse(File.ReadAllText(indexPath, Encoding.UTF8)));
            var objects = Json.D(Json.O(index, "objects"));
            if (objects == null) throw new InvalidDataException("资源索引缺少 objects");
            bool virtualAssets = Json.B(index, "virtual", false);
            bool resources = Json.B(index, "map_to_resources", false);
            foreach (var entry in objects)
            {
                string hash = Json.S(entry.Value, "hash", "");
                if (!FileSafety.IsHash(hash, 40)) throw new InvalidDataException("资源 SHA1 格式错误：" + entry.Key);
                long size = Json.L(entry.Value, "size", 0);
                string relative = hash.Substring(0, 2) + "/" + hash;
                string path = FileSafety.Under(gameDir, "assets/objects/" + relative, cache);
                string virtualPath = virtualAssets ? FileSafety.Under(gameDir, "assets/virtual/" + indexId + "/" + entry.Key, cache) : null;
                string resourcePath = resources ? FileSafety.Under(gameDir, "resources/" + entry.Key, cache) : null;
                if (!FileSafety.Matches(path, hash, size, cache))
                {
                    list.Add(new DlItem { Dest = path, Url = "https://resources.download.minecraft.net/" + relative,
                        Sha1 = hash, Size = size, Desc = "资源 " + entry.Key });
                    continue;
                }
                foreach (string destination in new[] { virtualPath, resourcePath })
                {
                    if (destination == null || FileSafety.Matches(destination, hash, size, cache)) continue;
                    FileSafety.WriteViaTemp(destination, ".tmp", temp => File.Copy(path, temp));
                }
            }
        }



        // ---------- 参数展开 ----------
        static List<KeyValuePair<string, string>> Needles(Dictionary<string, string> vars)
        {
            var needles = new List<KeyValuePair<string, string>>(vars.Count);
            foreach (KeyValuePair<string, string> kv in vars)
                needles.Add(new KeyValuePair<string, string>("${" + kv.Key + "}", kv.Value));
            return needles;
        }

        static List<string> ExpandArgs(object[] arr, List<KeyValuePair<string, string>> needles, Dictionary<string, bool> features)
        {
            List<string> result = new List<string>();
            foreach (object e in arr)
            {
                string s = e as string;
                if (s != null) { result.Add(ReplaceVars(s, needles)); continue; }
                var d = Json.D(e);
                if (d == null) continue;
                if (!RulesPass(Json.O(d, "rules"), features)) continue;
                object val = d.ContainsKey("value") ? d["value"] : null;
                string vs = val as string;
                if (vs != null) result.Add(ReplaceVars(vs, needles));
                else
                {
                    object[] va = Json.A(val);
                    if (va != null) foreach (object v in va) { string x = v as string; if (x != null) result.Add(ReplaceVars(x, needles)); }
                }
            }
            return result;
        }

        static string ReplaceVars(string s, List<KeyValuePair<string, string>> needles)
        {
            foreach (KeyValuePair<string, string> kv in needles)
                s = s.Replace(kv.Key, kv.Value);
            return s;
        }

        public static List<string> SplitArgs(string value) { return CommandLine.Split(value); }

        static string OfflineUuid(string name)
        {
            using (var md5 = MD5.Create())
            {
                byte[] b = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
                b[6] = (byte)((b[6] & 0x0F) | 0x30);
                b[8] = (byte)((b[8] & 0x3F) | 0x80);
                string h = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
                return h.Substring(0, 8) + "-" + h.Substring(8, 4) + "-" + h.Substring(12, 4) + "-" + h.Substring(16, 4) + "-" + h.Substring(20, 12);
            }
        }

        // ---------- 构建启动计划 ----------
        public class LaunchPlan
        {
            public string JavaExe, WorkingDir, CommandText, NativesDir, MainClass;
            public List<string> JvmArgs, GameArgs;
            public int RequiredJavaMajor;
        }

        public static LaunchPlan Build(Config cfg, string versionId, int memGb, string userName, bool isolation, int winW, int winH, bool prepareNatives = true)
        {
            return Build(cfg, LoadMerged(Path.GetFullPath(cfg.GameDir), versionId), versionId, memGb, userName, isolation, winW, winH, prepareNatives);
        }

        public static LaunchPlan Build(Config cfg, Dictionary<string, object> merged, string versionId, int memGb, string userName, bool isolation, int winW, int winH, bool prepareNatives = true)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(userName ?? "", @"\A[A-Za-z0-9_]{1,16}\z"))
                throw new ArgumentException("离线玩家名须为 1–16 位英文字母、数字或下划线");
            if (memGb < 1 || memGb > 256) throw new ArgumentOutOfRangeException("memGb");
            string gameDir = Path.GetFullPath(cfg.GameDir);
            var mj = merged;

            string gameRoot = gameDir;
            string actualGameDir = GameDirectories.Resolve(cfg, versionId, isolation);
            if (prepareNatives) Directory.CreateDirectory(actualGameDir);

            string verDir = Path.GetDirectoryName(FileSafety.VersionFile(gameRoot, versionId, ".json"));
            string nativesDir = FileSafety.Under(gameRoot, "versions/" + versionId + "/natives/" + Guid.NewGuid().ToString("N"));
            string libs = LibsDir(gameRoot);
            string assets = AssetsDir(gameRoot);

            // classpath + natives
            List<string> cp = new List<string>();
            List<KeyValuePair<string, List<string>>> nativeJars = new List<KeyValuePair<string, List<string>>>();
            foreach (var file in LibraryFiles(mj))
            {
                string path = FileSafety.Under(gameRoot, "libraries/" + file.Path);
                if (file.Native) nativeJars.Add(new KeyValuePair<string, List<string>>(path, file.Excludes));
                else cp.Add(path);
            }
            string jarRef = Json.S(mj, "jar", versionId);
            cp.Add(FileSafety.VersionFile(gameRoot, jarRef, ".jar"));
            if (prepareNatives)
            {
            Directory.CreateDirectory(nativesDir);
            foreach (var native in nativeJars)
            {
                if (!File.Exists(native.Key)) throw new FileNotFoundException("缺少 native 库", native.Key);
                using (var zip = ZipFile.OpenRead(native.Key))
                {
                    long expanded = 0;
                    if (zip.Entries.Count > 10000) throw new InvalidDataException("native 压缩包条目过多");
                    foreach (var entry in zip.Entries)
                    {
                        expanded = checked(expanded + entry.Length);
                        if (expanded > 512L * 1024 * 1024) throw new InvalidDataException("native 压缩包展开大小超过限制");
                        // Validate even excluded entries; unsafe archives must fail closed.
                        string target = FileSafety.Under(nativesDir, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name) || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) ||
                            native.Value.Any(ex => entry.FullName.StartsWith(ex, StringComparison.Ordinal))) continue;
                        if (entry.Length > 256L * 1024 * 1024) throw new InvalidDataException("native 文件大小超过限制");
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                    }
                }
            }



            }

            // 变量表
            string aId = "legacy";
            var ai = Json.D(Json.O(mj, "assetIndex"));
            if (ai != null) aId = Json.S(ai, "id", Json.S(mj, "assets", "legacy"));
            else aId = Json.S(mj, "assets", "legacy");
            FileSafety.Id(aId);
            string uuid = OfflineUuid(userName);

            Dictionary<string, string> vars = new Dictionary<string, string>();
            if (winW > 0 && winH > 0) {
                vars["resolution_width"] = winW.ToString();
                vars["resolution_height"] = winH.ToString();
            }
            vars["game_assets"] = FileSafety.Under(gameRoot, "assets/virtual/" + aId);
            vars["auth_session"] = "0";
            vars["auth_player_name"] = userName;
            vars["version_name"] = versionId;
            vars["game_directory"] = actualGameDir;
            vars["assets_root"] = assets;
            vars["assets_index_name"] = aId;
            vars["auth_uuid"] = uuid;
            vars["auth_access_token"] = "0";
            vars["clientid"] = "0";
            vars["auth_xuid"] = "0";
            vars["user_type"] = "legacy";
            vars["version_type"] = "SuyunCraft";
            vars["user_properties"] = "{}";
            vars["natives_directory"] = nativesDir;
            vars["launcher_name"] = Program.AppName;
            vars["launcher_version"] = Program.AppVersion;
            vars["classpath"] = string.Join(";", cp.ToArray());
            vars["classpath_separator"] = ";";
            vars["library_directory"] = libs;
            var needles = Needles(vars);
            var features = new Dictionary<string, bool> { { "has_custom_resolution", vars.ContainsKey("resolution_width") } };

            // JVM 参数
            List<string> jvm = new List<string>();
            jvm.Add("-Xmx" + memGb + "G");
            jvm.Add("-Xms" + Math.Max(512, Math.Min(1024, memGb * 256)) + "M");
            object jvmArr = Json.O(Json.O(mj, "arguments"), "jvm");
            if (jvmArr != null && Json.A(jvmArr) != null) jvm.AddRange(ExpandArgs(Json.A(jvmArr), needles, features));
            else
            {
                jvm.Add("-Djava.library.path=" + nativesDir);
                jvm.Add("-Dminecraft.launcher.version=" + Program.AppVersion);
                jvm.Add("-Dminecraft.launcher.name=" + Program.AppName);
                jvm.Add("-cp");
                jvm.Add(string.Join(";", cp.ToArray()));
            }
            jvm.Add("-Dlog4j2.formatMsgNoLookups=true");
            // 日志配置
            var logClient = Json.D(Json.O(Json.O(mj, "logging"), "client"));
            if (logClient != null)
            {
                var logFile = Json.D(Json.O(logClient, "file"));
                if (logFile != null)
                {
                    string lgDest = FileSafety.Under(gameRoot, "assets/log_configs/" + FileSafety.Id(Json.S(logFile, "id", "")));
                    if (File.Exists(lgDest))
                    {
                        string arg = Json.S(logClient, "argument", "");
                        arg = arg.Replace("${path}", lgDest);
                        if (arg.Length > 0) jvm.Add(arg);
                    }
                }
            }
            if (!string.IsNullOrEmpty(cfg.ExtraJvm)) jvm.AddRange(SplitArgs(cfg.ExtraJvm));

            // 游戏参数
            List<string> game = new List<string>();
            object gameArr = Json.O(Json.O(mj, "arguments"), "game");
            if (gameArr != null && Json.A(gameArr) != null) game.AddRange(ExpandArgs(Json.A(gameArr), needles, features));
            else
            {
                string ma = Json.S(mj, "minecraftArguments", "");
                game.AddRange(SplitArgs(ma).Select(arg => ReplaceVars(arg, needles)));
            }
            if (winW > 0 && winH > 0)
            {
                bool hasRes = false;
                foreach (string g in game) if (g == "--width") { hasRes = true; break; }
                if (!hasRes) { game.Add("--width"); game.Add(winW.ToString()); game.Add("--height"); game.Add(winH.ToString()); }
            }

            if (!string.IsNullOrEmpty(cfg.SaveInstanceId))
            {
                // Normalize even hardcoded/custom version arguments to this environment.
                for (int i = game.Count - 1; i >= 0; i--)
                {
                    if (game[i].StartsWith("--gameDir=", StringComparison.Ordinal)) game.RemoveAt(i);
                    else if (game[i] == "--gameDir")
                    {
                        if (i + 1 >= game.Count || game[i + 1].StartsWith("--")) throw new InvalidDataException("版本的 --gameDir 参数缺少值");
                        game.RemoveRange(i, 2);
                    }
                }
                game.Add("--gameDir"); game.Add(actualGameDir);
                foreach (string arg in jvm)
                    if (new[] { "-Duser.dir=", "-Dfabric.modsFolder=", "-Dfabric.addMods=", "-Dfabric.gameDir=" }.Any(prefix => arg.StartsWith(prefix, StringComparison.Ordinal)))
                        throw new InvalidDataException("存档隔离时不允许覆盖游戏/模组目录的 JVM 参数：" + arg.Split('=')[0]);
            }

            string mainClass = Json.S(mj, "mainClass", "net.minecraft.client.main.Main");
            if (mainClass.Length == 0) mainClass = "net.minecraft.client.main.Main";

            // 拼接命令
            List<string> all = new List<string>();
            all.AddRange(jvm);
            all.Add(mainClass);
            all.AddRange(game);

            LaunchPlan plan = new LaunchPlan();
            plan.MainClass = mainClass;
            plan.JavaExe = cfg.JavaPath;
            plan.WorkingDir = actualGameDir;
            plan.JvmArgs = jvm;
            plan.GameArgs = game;
            plan.NativesDir = nativesDir;
            plan.CommandText = Quote(cfg.JavaPath) + " " + JoinQuoted(all);
            plan.RequiredJavaMajor = (int)Json.L(Json.O(mj, "javaVersion"), "majorVersion", 8);
            return plan;
        }

        static string JoinQuoted(List<string> args) { return CommandLine.Join(args); }
        public static string Quote(string value) { return CommandLine.Quote(value); }

    }
}
