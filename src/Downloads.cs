using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SuyunCraft
{
    class DlItem
    {
        public string Url, Dest, Sha1, Sha256, Desc;
        public long Size;
    }

    class DownloadManager
    {
        public readonly ConcurrentQueue<DlItem> Queue = new ConcurrentQueue<DlItem>();
        public int Concurrency = 6;
        public string Mirror = Mirrors.Bmclapi;
        public long TotalBytes, DoneBytes, InflightBytes;
        public int TotalFiles, DoneFiles;
        public string CurrentFile = "";
        public Exception Error;
        public volatile bool Finished;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        public bool Cancelled { get { return cancel.IsCancellationRequested; } set { if (value) cancel.Cancel(); } }

        public async Task RunAsync(CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                using (cancellation.Register(() => Cancelled = true))
                    await RunCoreAsync().ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            catch (Exception ex) { Error = ex; Cancelled = true; }
            finally { Finished = true; }
        }

        internal static DlItem[] PrepareItems(IEnumerable<DlItem> source)
        {
            var unique = new Dictionary<string, DlItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Dest)) throw new InvalidDataException("Downloaddestination missing");
                if (item.Size < 0 || item.Size > 1024L * 1024 * 1024 ||
                    (!string.IsNullOrEmpty(item.Sha1) && !FileSafety.IsHash(item.Sha1, 40)) ||
                    (!string.IsNullOrEmpty(item.Sha256) && !FileSafety.IsHash(item.Sha256, 64)))
                    throw new InvalidDataException("DownloadInvalid size or hash format: " + item.Desc);
                string path = Path.GetFullPath(item.Dest);
                DlItem previous;
                if (unique.TryGetValue(path, out previous))
                {
                    if (previous.Size != item.Size || !string.Equals(previous.Sha1 ?? "", item.Sha1 ?? "", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(previous.Sha256 ?? "", item.Sha256 ?? "", StringComparison.OrdinalIgnoreCase) ||
                        (string.IsNullOrEmpty(item.Sha1) && string.IsNullOrEmpty(item.Sha256) && previous.Url != item.Url))
                        throw new InvalidDataException("Conflicting verification info for one destination: " + item.Desc);
                    continue;
                }
                unique.Add(path, new DlItem { Url = item.Url, Dest = path, Sha1 = item.Sha1, Sha256 = item.Sha256, Size = item.Size, Desc = item.Desc });
            }
            return unique.Values.ToArray();
        }

        async Task RunCoreAsync()
        {
            if (Concurrency < 1 || Concurrency > 32) throw new ArgumentOutOfRangeException("Concurrency");
            // Duplicate assets must share one destination and one writer.
            var items = PrepareItems(Queue.ToArray());
            DlItem unused;
            while (Queue.TryDequeue(out unused)) { }
            foreach (var item in items) Queue.Enqueue(item);
            TotalFiles = items.Length;
            TotalBytes = items.Sum(i => Math.Max(0, i.Size));
            try
            {
                var workers = new List<Task>();
                for (int i = 0; i < Math.Min(Concurrency, TotalFiles); i++) workers.Add(Task.Run((Action)Worker));
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            finally { Finished = true; }
        }

        void Worker()
        {
            try
            {
                DlItem item;
                while (!Cancelled && Queue.TryDequeue(out item))
                {
                    CurrentFile = item.Desc ?? Path.GetFileName(item.Dest);
                    Exception last = null;
                    bool ok = false;
                    for (int attempt = 0; attempt < 3 && !ok && !Cancelled; attempt++)
                    {
                        try { DownloadOne(item, attempt % 2 == 0 ? Mirror : Mirrors.Other(Mirror)); ok = true; }
                        catch (Exception ex)
                        {
                            last = ex;
                            if (!Cancelled) cancel.Token.WaitHandle.WaitOne(250 * (attempt + 1));
                        }
                    }
                    if (!ok)
                    {
                        if (!Cancelled) throw new IOException("Downloadfailed：" + item.Desc, last);
                        break;
                    }
                    Interlocked.Increment(ref DoneFiles);
                    CurrentFile = item.Desc ?? Path.GetFileName(item.Dest);
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref Error, ex, null);
                Cancelled = true;
            }
        }

        void DownloadOne(DlItem item, string mirror)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                DownloadFile(item, mirror, deadline.Token);
            }
        }

        void DownloadFile(DlItem item, string mirror, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(item.Dest)));
            string temp = item.Dest + "." + Guid.NewGuid().ToString("N") + ".part";
            long got = 0;
            try
            {
                using (var response = Net.Open(Mirrors.Map(item.Url, mirror), token))
                using (var input = response.GetResponseStream())
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
                {
                    CopyVerified(input, output, item, token, count => { got += count; Interlocked.Add(ref InflightBytes, count); });
                }
                token.ThrowIfCancellationRequested();
                FileSafety.Commit(temp, item.Dest);
                Interlocked.Add(ref DoneBytes, got);
            }
            finally
            {
                Interlocked.Add(ref InflightBytes, -got);
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        // Hash while streaming: never reread the downloaded file just to verify it.
        internal static long CopyVerified(Stream input, Stream output, DlItem item, CancellationToken token, Action<int> progress = null)
        {
            using (var sha1 = string.IsNullOrEmpty(item.Sha1) ? null : SHA1.Create())
            using (var sha256 = string.IsNullOrEmpty(item.Sha256) ? null : SHA256.Create())
            {
                long got = 0;
                byte[] buffer = new byte[65536];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int count = input.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    token.ThrowIfCancellationRequested();
                    got = checked(got + count);
                    if (got > 1024L * 1024 * 1024 || (item.Size > 0 && got > item.Size))
                        throw new InvalidDataException("DownloadfileSize limit exceeded: " + item.Desc);
                    if (sha1 != null) sha1.TransformBlock(buffer, 0, count, buffer, 0);
                    if (sha256 != null) sha256.TransformBlock(buffer, 0, count, buffer, 0);
                    output.Write(buffer, 0, count);
                    if (progress != null) progress(count);
                }
                token.ThrowIfCancellationRequested();
                if (got == 0 || (item.Size > 0 && got != item.Size)) throw new InvalidDataException("Downloadfileempty or wrong size: " + item.Desc);
                VerifyHash(sha1, item.Sha1, item.Desc);
                VerifyHash(sha256, item.Sha256, item.Desc);
                return got;
            }
        }

        static void VerifyHash(HashAlgorithm algorithm, string expected, string description)
        {
            if (algorithm == null) return;
            algorithm.TransformFinalBlock(new byte[0], 0, 0);
            if (!string.Equals(BitConverter.ToString(algorithm.Hash).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DownloadfileHash verification failed: " + description);
        }
    }
}
