// Persistent verify cache (P2): records fingerprints (length,+mtime+expected contract),
// if all three are unchanged, hashing is skipped. Invalidation:
//  · expected sha1/size changes → different key → forces a full re-verify
//  · file length or mtime changes → record mismatch → full re-verification
//  · verify fails/file missing → never written to the cache
//  · schema version changes (verify logic upgrade）→ the whole cache is invalidated
//  · the Settings page「Force full verification」→ Deletethe cache file; full rescan
using System;
using System.Collections.Generic;
using System.IO;

namespace SuyunCraft
{
    static class VerifyStore
    {
        const int Schema = 1;
        const int MaxEntries = 50000;
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
                    if (new FileInfo(StoreFile).Length > 16L * 1024 * 1024) return;
                    var root = Json.D(Json.Parse(File.ReadAllText(StoreFile, System.Text.Encoding.UTF8)));
                    if (root == null || Json.L(root, "schema", 0) != Schema) return;
                    var e = Json.D(root["entries"]);
                    if (e == null) return;
                    if (e.Count > MaxEntries) return;
                    foreach (KeyValuePair<string, object> kv in e)
                    { string v = kv.Value as string; if (v != null) entries[kv.Key] = v; }
                }
                catch { entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
            }
        }

        // hitCondition: expected contract matches + (length, mtime) unchanged + head sample hash matches。
        // Performance cache only: sampling is not a full integrity or anti-tampering check.
        // Use the settings full-verification action to rehash every file.
        public static bool Lookup(string path, string sha1, long size)
        {
            lock (gate)
            {
                if (!loaded || entries == null) return false;
                string record;
                if (!entries.TryGetValue(Key(path, sha1, size), out record)) return false;
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0 || (size > 0 && info.Length != size)) return false;
                if (!record.StartsWith(info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|", StringComparison.Ordinal)) return false;
                string[] parts = record.Split('|');
                if (parts.Length != 3) return false;
                // large files (>32KB：client jar, core libraries, large assets) get a 4KB head sample; small files are numerous (thousands
                // asset objects); per-file handles would erase the speedup, so trust length+mtime。
                // 「Force full verification」entry can force a full hash of every file at any time。
                if (info.Length <= 32 * 1024) return parts[2] == "-";
                return HeadMatches(path, record);
            }
        }

        // 4KB head hash check (third field). Reading 4KB costs ~1/50 of a full hash (500KB averagefile）。
        static bool HeadMatches(string path, string record)
        {
            try
            {
                string[] parts = record.Split('|');
                if (parts.Length != 3 || !FileSafety.IsHash(parts[2], 40)) return false;
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

        // Only files that fully verify are recorded; record = = length|mtime|a 4KB head hash.
        public static void Record(string path, string sha1, long size)
        {
            lock (gate)
            {
                if (entries == null || (entries.Count >= MaxEntries && !entries.ContainsKey(Key(path, sha1, size)))) return;
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
                    // Prune: entries whose files no longer exist are dropped (one stat, metadata only）
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
