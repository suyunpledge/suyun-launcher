using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuyunCraft
{
    static class StartupReliabilityTests
    {
        static bool Rejected(Action action)
        { try { action(); return false; } catch (IOException) { return true; } catch (OperationCanceledException) { return true; } catch (InvalidDataException) { return true; } }

        static long Copy(byte[] bytes, DlItem item, CancellationToken token, Action<int> progress = null)
        {
            using (var input = new MemoryStream(bytes))
            using (var output = new MemoryStream())
            {
                long count = DownloadManager.CopyVerified(input, output, item, token, progress);
                if (!output.ToArray().SequenceEqual(bytes)) throw new Exception("Streaming copy changed data");
                return count;
            }
        }

        internal static void Run(string root, Action<bool, string> check)
        {
            Directory.CreateDirectory(root);
            byte[] data = new byte[200000]; new Random(21).NextBytes(data);
            string path = Path.Combine(root, "source.bin"); File.WriteAllBytes(path, data);
            string sha1 = FileSafety.Hash(path, false), sha256 = FileSafety.Hash(path, true);
            var item = new DlItem { Dest = path, Size = data.Length, Sha1 = sha1, Sha256 = sha256, Url = "https://example.com/a" };
            check(Copy(data, item, CancellationToken.None) == data.Length, "Streaming download verifies SHA1 and SHA256 together");
            check(Rejected(() => Copy(data, new DlItem { Size = data.Length + 1 }, CancellationToken.None)), "Truncated response rejected");
            check(Rejected(() => Copy(data, new DlItem { Size = data.Length - 1 }, CancellationToken.None)), "Oversized response rejected");
            check(Rejected(() => Copy(new byte[0], new DlItem(), CancellationToken.None)), "Empty unknown-length response rejected");
            check(Rejected(() => Copy(data, new DlItem { Sha1 = new string('0', 40) }, CancellationToken.None)), "Incorrect SHA1 rejected during streaming verification");
            check(Rejected(() => Copy(data, new DlItem { Sha256 = new string('0', 64) }, CancellationToken.None)), "Incorrect SHA256 rejected during streaming verification");
            using (var cancel = new CancellationTokenSource())
            {
                check(Rejected(() => Copy(data, item, cancel.Token, n => cancel.Cancel())), "Mid-file cancellation stops the streaming copy");
                check(Rejected(() => FileSafety.Hash(path, false, cancel.Token)), "File hashing honors cancellation");
                check(Rejected(() => FileSafety.Matches(path, sha1, data.Length, new FileSafety.VerifyCache { Cancellation = cancel.Token })), "Verification cache cannot bypass cancellation");
                var manager = new DownloadManager();
                check(Rejected(() => manager.RunAsync(cancel.Token).GetAwaiter().GetResult()) && manager.Finished && manager.Cancelled, "Pre-cancelled download records a terminal state");
            }
            check(DownloadManager.PrepareItems(new[] { item, item }).Length == 1, "Identical download destinations have one writer");
            check(Rejected(() => DownloadManager.PrepareItems(new[] { item, new DlItem { Dest = path, Size = item.Size, Sha1 = new string('0', 40) } })), "Conflicting destination hashes are rejected before downloading");
            check(Rejected(() => DownloadManager.PrepareItems(new[] { new DlItem { Dest = path, Sha256 = "bad" } })), "Malformed hash is rejected before network work");
            var invalid = new DownloadManager { Concurrency = 0 }; invalid.RunAsync().GetAwaiter().GetResult();
            check(invalid.Finished && invalid.Error is ArgumentOutOfRangeException, "Invalid download setup reports completion and diagnostic");

            using (var both = new CountdownEvent(2))
            using (var loserStopped = new ManualResetEventSlim())
            {
                string result = Net.RaceFetch(new List<string> { "fixture" }, CancellationToken.None, (url, proxy, token) => {
                    both.Signal();
                    if (!both.Wait(3000)) throw new TimeoutException("Race fixture did not start");
                    if (proxy) return "winner";
                    token.WaitHandle.WaitOne(3000);
                    if (token.IsCancellationRequested) loserStopped.Set();
                    token.ThrowIfCancellationRequested(); return "late";
                });
                check(result == "winner" && loserStopped.Wait(3000), "Metadata winner cancels outstanding requests");
            }
            using (var cancel = new CancellationTokenSource())
            using (var started = new CountdownEvent(2))
            using (var stopped = new CountdownEvent(2))
            {
                var race = Task.Run(() => Net.RaceFetch(new List<string> { "fixture" }, cancel.Token, (url, proxy, token) => {
                    started.Signal();
                    token.WaitHandle.WaitOne(5000);
                    stopped.Signal(); token.ThrowIfCancellationRequested(); return "unexpected";
                }));
                check(started.Wait(3000), "Both metadata channels enter the cancellation fixture");
                cancel.Cancel();
                check(Rejected(() => race.GetAwaiter().GetResult()) && stopped.Wait(3000), "User cancellation aborts all metadata channels");
            }
            check(Rejected(() => Net.RaceFetch(new List<string> { "fixture" }, CancellationToken.None,
                (url, proxy, token) => { throw new IOException("offline"); })), "All failed metadata channels report an error");

            var buffer = new GameLogBuffer();
            Parallel.For(0, 10000, i => buffer.Append("line " + i + new string('x', 1000)));
            buffer.Append("last-marker");
            string logs = buffer.Drain(true);
            check(logs.Length < 1100000 && logs.Contains("suppressed") && logs.Contains("last-marker"), "Flooded log queue stays bounded and keeps recent output");
            check(buffer.Drain(true) == "", "Log drain consumes its batch once");
            buffer.Append(new string('x', 1000000));
            check(buffer.Drain(false).Length < 9000, "A single huge mod log line is truncated");

            ProbeJava(root, check);
            CheckSaveOperations(root, check);
            VerifyStore.InvalidateAll();
            try
            {
                VerifyStore.Record(path, sha1, data.Length);
                var entries = (Dictionary<string, string>)typeof(VerifyStore).GetField("entries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                string key = entries.Keys.Single(); var info = new FileInfo(path);
                entries[key] = info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|-";
                check(!VerifyStore.Lookup(path, sha1, data.Length), "Missing large-file sample hash never counts as a cache hit");
                entries[key] += "|extra";
                check(!VerifyStore.Lookup(path, sha1, data.Length), "Malformed verification record fails closed");
            }
            finally { VerifyStore.InvalidateAll(); }
        }

        static void ProbeJava(string root, Action<bool, string> check)
        {
            string javaRoot = Path.Combine(root, "fake-jre");
            string bin = Path.Combine(javaRoot, "bin"); Directory.CreateDirectory(bin);
            string executable = Path.Combine(bin, "java.exe");
            File.Copy(typeof(StartupReliabilityTests).Assembly.Location, executable);
            string mode = Path.Combine(bin, "probe-mode.txt"), calls = Path.Combine(bin, "probe-calls.txt");
            File.WriteAllText(mode, "21");
            check(JavaUtil.GetMajor(executable) == 21, "Java version probe reads the child process");
            check(JavaUtil.GetMajor(executable) == 21 && File.ReadAllLines(calls).Length == 1, "Unchanged Java probe is reused without another process");
            File.WriteAllText(mode, "17"); File.WriteAllText(Path.Combine(javaRoot, "release"), "JAVA_VERSION=17");
            check(JavaUtil.GetMajor(executable) == 17 && File.ReadAllLines(calls).Length == 2, "Changed runtime release invalidates the Java probe cache");
            File.WriteAllText(mode, "hang"); File.AppendAllText(Path.Combine(javaRoot, "release"), "changed");
            using (var cancel = new CancellationTokenSource(250))
            {
                var watch = Stopwatch.StartNew();
                check(Rejected(() => JavaUtil.GetMajor(executable, cancel.Token)) && watch.ElapsedMilliseconds < 2500, "Cancellation kills a stalled Java probe promptly");
            }
            File.WriteAllText(mode, "bad");
            check(JavaUtil.GetMajor(executable) == -1, "Unparseable Java version is rejected");
            File.WriteAllText(mode, "21");
            check(JavaUtil.GetMajor(executable) == 21, "Failed Java probe is retried rather than cached");
        }

        static void CheckSaveOperations(string root, Action<bool, string> check)
        {
            var cfg = ModManagementTests.Fixture(Path.Combine(root, "worlds"));
            var instance = SaveIsolation.Create(cfg, "custom-profile", "reliability tests", null, false, CancellationToken.None);
            string game = SaveIsolation.GameDirectory(cfg.GameDir, instance.Id);
            var active = cfg.Snapshot(); active.SaveInstanceId = instance.Id;
            using (GameDirectories.Acquire(active, instance.Version))
            {
                check(Rejected(() => SaveIsolation.Export(cfg, instance.Id, CancellationToken.None)), "Backup refuses an environment held by another launcher operation");
                check(Rejected(() => SaveIsolation.Delete(cfg, instance.Id, null)), "Delete refuses an environment held by a running game");
            }
            using (GameDirectories.EnterMaintenance(game))
            {
                bool blocked = Task.Run(() => Rejected(() => { using (GameDirectories.Acquire(active, instance.Version)) { } })).Result;
                check(blocked, "Maintenance gate blocks a concurrent launch before directory mutation");
            }
            Directory.CreateDirectory(Path.Combine(game, "saves/world"));
            File.WriteAllText(Path.Combine(game, "saves/world/level.dat"), "world");
            string session = Path.Combine(game, "saves/world/session.lock"); File.WriteAllText(session, "session");
            using (new FileStream(session, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                check(Rejected(() => SaveIsolation.Export(cfg, instance.Id, CancellationToken.None)), "Backup refuses a world locked by Minecraft");
                check(Rejected(() => SaveIsolation.Delete(cfg, instance.Id, null)), "Delete refuses a world locked by Minecraft");
            }
            string first = null, second = null;
            try
            {
                first = SaveIsolation.Export(cfg, instance.Id, CancellationToken.None);
                second = SaveIsolation.Export(cfg, instance.Id, CancellationToken.None);
                check(first != second && File.Exists(first), "Repeated backups never overwrite an earlier export");
                using (var zip = ZipFile.OpenRead(first))
                {
                    check(zip.Entries.Any(e => e.FullName == "instance/game/mods/"), "Backup preserves empty mods directories");
                    check(!zip.Entries.Any(e => e.FullName.EndsWith("session.lock")), "World session locks are excluded from the backup");
                }
                for (int i = 0; i < 60; i++) File.WriteAllText(Path.Combine(game, "config/test-" + i), "data");
                string exportRoot = Path.GetDirectoryName(first);
                int before = Directory.GetFiles(exportRoot).Length;
                using (var cancel = new CancellationTokenSource())
                    check(Rejected(() => SaveIsolation.Export(cfg, instance.Id, cancel.Token, new CancelProgress(cancel))), "Cancelling an active backup aborts before publishing");
                check(Directory.GetFiles(exportRoot).Length == before, "Cancelled backup cleans up its partial archive");
                using (GameDirectories.Acquire(active, instance.Version)) { check(true, "Cancelled backup releases the environment and world locks"); }
            }
            finally
            {
                if (first != null && File.Exists(first)) File.Delete(first);
                if (second != null && File.Exists(second)) File.Delete(second);
            }
            SaveIsolation.Delete(cfg, instance.Id, null);
            check(!Directory.Exists(game) && Rejected(() => { using (GameDirectories.Acquire(active, instance.Version)) { } }), "Deleted environment cannot be recreated by a stale launch selection");
        }

        sealed class CancelProgress : IProgress<string>
        {
            readonly CancellationTokenSource cancel;
            public CancelProgress(CancellationTokenSource cancel) { this.cancel = cancel; }
            public void Report(string value) { cancel.Cancel(); }
        }
    }
}
