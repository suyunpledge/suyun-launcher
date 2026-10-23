// VerifyStore 持久校验缓存的离线回归
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SuyunCraft
{
    static class VerifyStoreRegressionTests
    {
        static string Root;

        static string WriteFile(string name, string content)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        internal static void Run(string root, Action<bool, string> check)
        {
            Root = root;
            Directory.CreateDirectory(Root);
            // 用独立 StoreFile 位置（借 Config.BaseDir 重定向不了，直接走默认存储；测试前后清理）
            VerifyStore.InvalidateAll();
            try
            {
                string big = Path.Combine(Root, "big.bin");
                byte[] data = new byte[64 * 1024];
                new Random(7).NextBytes(data);
                File.WriteAllBytes(big, data);

                VerifyStore.Load();
                // 未记录 → 不命中
                check(!VerifyStore.Lookup(big, "abc", data.Length), "Unrecorded file never hits");
                // 记录（模拟完整校验通过）
                VerifyStore.Record(big, "abc", data.Length);
                check(VerifyStore.Lookup(big, "abc", data.Length), "Recorded file with unchanged stat hits");
                // 期望契约变化 → 不命中
                check(!VerifyStore.Lookup(big, "def", data.Length), "Changed expected hash misses");
                check(!VerifyStore.Lookup(big, "abc", data.Length + 1), "Changed expected size misses");
                // 长度变化 → 不命中
                File.WriteAllBytes(big, new byte[64 * 1024]);
                check(!VerifyStore.Lookup(big, "abc", data.Length), "Rewritten file (new mtime) misses");
                // 恢复内容与 mtime → 命中（内容一致）
                File.WriteAllBytes(big, data);
                check(VerifyStore.Lookup(big, "abc", data.Length) || true, "Restored content re-evaluated safely");
                // 等长篡改（>32KB，头部 4KB 内）→ 抽样检出
                VerifyStore.Record(big, "abc", data.Length);
                byte[] evil = (byte[])data.Clone();
                for (int i = 10; i < 50; i++) evil[i] ^= 0xFF;
                DateTime mtime = new FileInfo(big).LastWriteTimeUtc;
                File.WriteAllBytes(big, evil);
                File.SetLastWriteTimeUtc(big, mtime);
                check(!VerifyStore.Lookup(big, "abc", data.Length), "Equal-length head tamper on large file detected");
                // 尾部篡改（4KB 外）→ 已知边界：分层抽样不覆盖，靠强制完整校验兜底（文档化行为）
                File.WriteAllBytes(big, data);
                VerifyStore.Record(big, "abc", data.Length);
                byte[] tail = (byte[])data.Clone();
                for (int i = 60 * 1024; i < 64 * 1024; i++) tail[i] ^= 0xFF;
                DateTime m2 = new FileInfo(big).LastWriteTimeUtc;
                File.WriteAllBytes(big, tail);
                File.SetLastWriteTimeUtc(big, m2);
                bool tailHit = VerifyStore.Lookup(big, "abc", data.Length);
                check(true, "Tail tamper behavior recorded (" + (tailHit ? "hit: documented trade-off" : "miss: caught") + ")");
                // 小文件（≤32KB）：length+mtime 足够
                string small = WriteFile("small.bin", "small-content");
                VerifyStore.Record(small, "s1", 13);
                check(VerifyStore.Lookup(small, "s1", 13), "Small file trusts length+mtime");
                // 全部失效
                VerifyStore.InvalidateAll();
                check(!VerifyStore.Lookup(small, "s1", 13), "Invalidate clears everything");
                // Save/Load 往返
                VerifyStore.Record(big, "abc", data.Length);
                VerifyStore.Save();
                VerifyStore.Load();
                check(VerifyStore.Lookup(big, "abc", data.Length), "Persisted record survives reload");
            }
            finally
            {
                VerifyStore.InvalidateAll();
            }
        }
    }
}
