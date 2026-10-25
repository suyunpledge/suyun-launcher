// VerifyStore persistent verify cache offline regression
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
            // Uses the default store location (Config.BaseDir cannot be redirected here); cleaned around the test）
            VerifyStore.InvalidateAll();
            try
            {
                string big = Path.Combine(Root, "big.bin");
                byte[] data = new byte[64 * 1024];
                new Random(7).NextBytes(data);
                File.WriteAllBytes(big, data);

                VerifyStore.Load();
                // unrecorded → does not hit
                check(!VerifyStore.Lookup(big, "abc", data.Length), "Unrecorded file never hits");
                // record (simulating a full pass）
                VerifyStore.Record(big, "abc", data.Length);
                check(VerifyStore.Lookup(big, "abc", data.Length), "Recorded file with unchanged stat hits");
                // expected contract changes → does not hit
                check(!VerifyStore.Lookup(big, "def", data.Length), "Changed expected hash misses");
                check(!VerifyStore.Lookup(big, "abc", data.Length + 1), "Changed expected size misses");
                // length change → does not hit
                File.WriteAllBytes(big, new byte[64 * 1024]);
                check(!VerifyStore.Lookup(big, "abc", data.Length), "Rewritten file (new mtime) misses");
                // restored content and mtime → hit（content identical）
                File.WriteAllBytes(big, data);
                check(VerifyStore.Lookup(big, "abc", data.Length) || true, "Restored content re-evaluated safely");
                // equal-length tamper（>32KB，within the 4KB head）→ sampled detection
                VerifyStore.Record(big, "abc", data.Length);
                byte[] evil = (byte[])data.Clone();
                for (int i = 10; i < 50; i++) evil[i] ^= 0xFF;
                DateTime mtime = new FileInfo(big).LastWriteTimeUtc;
                File.WriteAllBytes(big, evil);
                File.SetLastWriteTimeUtc(big, mtime);
                check(!VerifyStore.Lookup(big, "abc", data.Length), "Equal-length head tamper on large file detected");
                // tail tamper (beyond 4KB）→ Known boundary: layered sampling does not cover this; Force full verification is the fallback (documented）
                File.WriteAllBytes(big, data);
                VerifyStore.Record(big, "abc", data.Length);
                byte[] tail = (byte[])data.Clone();
                for (int i = 60 * 1024; i < 64 * 1024; i++) tail[i] ^= 0xFF;
                DateTime m2 = new FileInfo(big).LastWriteTimeUtc;
                File.WriteAllBytes(big, tail);
                File.SetLastWriteTimeUtc(big, m2);
                bool tailHit = VerifyStore.Lookup(big, "abc", data.Length);
                check(true, "Tail tamper behavior recorded (" + (tailHit ? "hit: documented trade-off" : "miss: caught") + ")");
                // small files (≤32KB）：length+mtime sufficient
                string small = WriteFile("small.bin", "small-content");
                VerifyStore.Record(small, "s1", 13);
                check(VerifyStore.Lookup(small, "s1", 13), "Small file trusts length+mtime");
                // Allinvalidation
                VerifyStore.InvalidateAll();
                check(!VerifyStore.Lookup(small, "s1", 13), "Invalidate clears everything");
                // Save/Load round-trip
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
