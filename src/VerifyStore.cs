// 持久化校验缓存（P2）：记录通过完整校验的文件指纹（长度+mtime+期望契约），
// 下次启动若三者都没变则跳过哈希。失效规则：
//  · 期望 sha1/size 变化 → 键不同 → 必然重新完整校验
//  · 文件长度或 mtime 变化 → 记录不匹配 → 重新完整校验
//  · 校验失败/文件缺失 → 永不写入缓存
//  · schema 版本变化（校验逻辑升级）→ 整个缓存作废
//  · 设置页「强制完整校验」→ 删除缓存文件，全量重扫
using System;
using System.Collections.Generic;
using System.IO;

namespace SuyunCraft
{
    static class VerifyStore
    {
        const int Schema = 1;
        static Dictionary<string, string> entries;
        static readonly object gate = new object();
        static bool loaded;

        static string StoreFile
        { get { return Path.Combine(Config.BaseDir, "verify-cache.json"); } }

        public static void Load()
        {
            lock (gate)
            {
                entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                loaded = true;
                try
                {
                    if (!File.Exists(StoreFile)) return;
                    var root = Json.D(Json.Parse(File.ReadAllText(StoreFile, System.Text.Encoding.UTF8)));
                    if (root == null || Json.L(root, "schema", 0) != Schema) return;
                    var e = Json.D(root["entries"]);
                    if (e == null) return;
                    foreach (KeyValuePair<string, object> kv in e)
                    { string v = kv.Value as string; if (v != null) entries[kv.Key] = v; }
                }
                catch { entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
            }
        }

        // 命中条件：期望契约一致 + (length, mtime) 未变 + 头部抽样哈希一致。
        // 抽样防「等长篡改 + 恢复 mtime」的伪造：这类篡改头 4KB 命中率低，且破坏 JAR 头部必然异常。
        // 完整防伪造由「强制完整校验」入口兜底；日常损坏（下载错误、磁盘坏块）绝大多数会改变长度/mtime/头部。
        public static bool Lookup(string path, string sha1, long size)
        {
            lock (gate)
            {
                if (!loaded || entries == null) return false;
                string record;
                if (!entries.TryGetValue(Key(path, sha1, size), out record)) return false;
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                if (!record.StartsWith(info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|", StringComparison.Ordinal)) return false;
                // 大文件（>32KB：客户端 jar、核心库、大资源）做头 4KB 抽样；小文件数量庞大（数千个
                // 资源对象），逐个开句柄的开销会抵消全部加速收益，按业内通行做法信任 length+mtime。
                // 「强制完整校验」入口可随时对全部文件做完整哈希。
                if (info.Length <= 32 * 1024) return true;
                return HeadMatches(path, record);
            }
        }

        // 头 4KB 哈希核对（记录第三段）。读 4KB 的成本约为全量哈希的 1/50（对 500KB 平均文件）。
        static bool HeadMatches(string path, string record)
        {
            try
            {
                string[] parts = record.Split('|');
                if (parts.Length < 3 || parts[2] == "-") return true;
                using (var sha = System.Security.Cryptography.SHA1.Create())
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                {
                    byte[] buffer = new byte[4096];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    string head = BitConverter.ToString(sha.ComputeHash(buffer, 0, read)).Replace("-", "").ToLowerInvariant();
                    return head == parts[2];
                }
            }
            catch { return false; }
        }

        // 只记录完整校验为 true 的文件；记录 = length|mtime|头4KB哈希。
        public static void Record(string path, string sha1, long size)
        {
            lock (gate)
            {
                if (entries == null) return;
                var info = new FileInfo(path);
                if (!info.Exists) return;
                string head = "-";
                if (info.Length > 32 * 1024)
                {
                    try
                    {
                        using (var sha = System.Security.Cryptography.SHA1.Create())
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                        {
                            byte[] buffer = new byte[4096];
                            int read = stream.Read(buffer, 0, buffer.Length);
                            head = BitConverter.ToString(sha.ComputeHash(buffer, 0, read)).Replace("-", "").ToLowerInvariant();
                        }
                    }
                    catch { return; }
                }
                entries[Key(path, sha1, size)] = info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + head;
            }
        }

        static string Key(string path, string sha1, long size)
        { return Path.GetFullPath(path) + "|" + (sha1 ?? "") + "|" + size.ToString(); }

        public static void Save()
        {
            lock (gate)
            {
                if (entries == null) return;
                try
                {
                    // 修剪：文件已不存在的条目不再保留（stat 一次，纯元数据操作）
                    List<string> dead = new List<string>();
                    foreach (KeyValuePair<string, string> kv in entries)
                    {
                        int cut = kv.Key.IndexOf('|');
                        if (cut <= 0 || !File.Exists(kv.Key.Substring(0, cut))) dead.Add(kv.Key);
                    }
                    foreach (string k in dead) entries.Remove(k);
                    var root = new Dictionary<string, object>();
                    root["schema"] = Schema;
                    var boxed = new Dictionary<string, object>();
                    foreach (KeyValuePair<string, string> kv in entries) boxed[kv.Key] = kv.Value;
                    root["entries"] = boxed;
                    Directory.CreateDirectory(Config.BaseDir);
                    FileSafety.WriteText(StoreFile, Json.Dump(root));
                }
                catch { }
            }
        }

        public static void InvalidateAll()
        {
            lock (gate)
            {
                entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                loaded = true;
                try { if (File.Exists(StoreFile)) File.Delete(StoreFile); } catch { }
            }
        }
    }
}
