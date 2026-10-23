using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            cancellation.ThrowIfCancellationRequested();
            using (cancellation.Register(() => Cancelled = true))
                await RunCoreAsync().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
        }

        async Task RunCoreAsync()
        {
            if (Concurrency < 1 || Concurrency > 32) throw new ArgumentOutOfRangeException("Concurrency");
            // Duplicate assets must share one destination and one writer.
            var items = Queue.ToArray().GroupBy(i => Path.GetFullPath(i.Dest), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).ToArray();
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
                        if (!Cancelled) throw new IOException("下载失败：" + item.Desc, last);
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
                    byte[] buffer = new byte[65536];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (item.Size > 0 && got + count > item.Size) throw new InvalidDataException("下载文件超过声明大小");
                        if (got + count > 1024L * 1024 * 1024) throw new InvalidDataException("单文件超过 1 GB 限制");
                        output.Write(buffer, 0, count);
                        got += count;
                        Interlocked.Add(ref InflightBytes, count);
                    }
                }
                if (!FileSafety.Matches(temp, item.Sha1, item.Size)) throw new InvalidDataException("文件大小或 SHA1 校验失败：" + item.Desc);
                if (!string.IsNullOrEmpty(item.Sha256) &&
                    (!FileSafety.IsHash(item.Sha256, 64) || !string.Equals(FileSafety.Hash(temp, true), item.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("SHA256 校验失败：" + item.Desc);
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
    }
}
