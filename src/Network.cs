using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuyunCraft
{
    static class Mirrors
    {
        public const string Official = "official";
        public const string Bmclapi = "bmclapi";

        static readonly KeyValuePair<string, string>[] Prefixes = new[]
        {
            new KeyValuePair<string, string>("https://piston-meta.mojang.com/", "https://bmclapi2.bangbang93.com/"),
            new KeyValuePair<string, string>("https://piston-data.mojang.com/", "https://bmclapi2.bangbang93.com/"),
            new KeyValuePair<string, string>("https://launchermeta.mojang.com/", "https://bmclapi2.bangbang93.com/"),
            new KeyValuePair<string, string>("https://resources.download.minecraft.net/", "https://bmclapi2.bangbang93.com/assets/"),
            new KeyValuePair<string, string>("https://libraries.minecraft.net/", "https://bmclapi2.bangbang93.com/maven/"),
            new KeyValuePair<string, string>("https://maven.fabricmc.net/", "https://bmclapi2.bangbang93.com/maven/")
        };

        public static string Map(string url, string mirror)
        {
            if (mirror != Bmclapi) return url;
            foreach (var prefix in Prefixes)
                if (url.StartsWith(prefix.Key)) return prefix.Value + url.Substring(prefix.Key.Length);
            return url;
        }
        public static string Other(string mirror) { return mirror == Bmclapi ? Official : Bmclapi; }
    }

    static class Net
    {
        public static string UserAgent = "SuyunCraft/" + Program.AppVersion + " (personal launcher)";

        public sealed class Response : IDisposable
        {
            readonly HttpWebResponse response;
            readonly CancellationTokenRegistration abort;
            internal Response(HttpWebResponse response, CancellationTokenRegistration abort) { this.response = response; this.abort = abort; }
            public Stream GetResponseStream() { return response.GetResponseStream(); }
            public void Dispose() { abort.Dispose(); response.Dispose(); }
        }

        public static Response Open(string url, CancellationToken token)
        {
            return Open(url, token, true);
        }

        public static Response Open(string url, CancellationToken token, bool useSystemProxy)
        {
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new InvalidDataException("DownloadURLs must use HTTPS");
                token.ThrowIfCancellationRequested();
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.UserAgent = UserAgent;
                req.Timeout = 30000; req.ReadWriteTimeout = 30000;
                req.AllowAutoRedirect = false;
                if (!useSystemProxy) req.Proxy = null;
                HttpWebResponse response;
                var abort = token.Register(req.Abort);
                try { response = (HttpWebResponse)req.GetResponse(); }
                catch { abort.Dispose(); throw; }
                int code = (int)response.StatusCode;
                if (code >= 300 && code < 400)
                {
                    string location = response.Headers["Location"];
                    response.Dispose(); abort.Dispose();
                    if (string.IsNullOrEmpty(location)) throw new InvalidDataException("Redirect missing location");
                    url = new Uri(uri, location).AbsoluteUri;
                    continue;
                }
                if (code != 200) { response.Dispose(); abort.Dispose(); throw new IOException("HTTP " + code); }
                return new Response(response, abort);
            }
            throw new IOException("Too many redirects");
        }

        public static string DownloadString(string url, string mirror, CancellationToken cancellation = default(CancellationToken))
        {
            string[] tries = new string[] { Mirrors.Map(url, mirror), Mirrors.Map(url, Mirrors.Other(mirror)), url };
            List<string> cand = new List<string>();
            foreach (string u in tries) if (!cand.Contains(u)) cand.Add(u);
            return RaceFetch(cand, cancellation, FetchString);
        }

        // Race multiple endpoints: each candidate URL is fetched via system proxy and direct concurrently; first success wins.
        // Serial retries wait out every timeout on slow networks; racing cuts the worst case from"source count×timeout"down to"the fastest path"elapsed。
        internal static string RaceFetch(List<string> urls, CancellationToken cancellation, Func<string, bool, CancellationToken, string> fetch)
        {
            cancellation.ThrowIfCancellationRequested();
            var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var token = stop.Token;
            List<Task<string>> tasks = new List<Task<string>>();
            try
            {
                foreach (string u in urls)
                {
                    string cu = u;
                    tasks.Add(Task.Run(() => fetch(cu, true, token), token));
                    tasks.Add(Task.Run(() => fetch(cu, false, token), token));
                }
                var pending = new List<Task<string>>(tasks);
                Exception last = null;
                while (pending.Count > 0)
                {
                    int idx = Task.WaitAny(pending.ToArray(), -1, cancellation);
                    Task<string> done = pending[idx];
                    cancellation.ThrowIfCancellationRequested();
                    if (done.Status == TaskStatus.RanToCompletion && done.Result != null) return done.Result;
                    if (done.IsFaulted) last = done.Exception.GetBaseException();
                    pending.RemoveAt(idx);
                }
                throw new IOException("Downloadfailed（tried " + urls.Count + " endpoints x proxy/direct）" + (last != null ? "：" + last.Message : ""), last);
            }
            finally
            {
                // A winner or user cancellation aborts losing HTTP requests immediately.
                stop.Cancel();
                Task.WhenAll(tasks).ContinueWith(done => {
                    if (done.IsFaulted) { var observed = done.Exception; }
                    stop.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        static string FetchString(string url, bool useSystemProxy, CancellationToken cancellation)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            using (var resp = Open(url, deadline.Token, useSystemProxy))
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                var text = new StringBuilder();
                var buffer = new char[8192];
                int read;
                while ((read = sr.Read(buffer, 0, buffer.Length)) > 0)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (text.Length + read > 32 * 1024 * 1024) throw new InvalidDataException("JSON filetoo large");
                    text.Append(buffer, 0, read);
                }
                return text.ToString();
            }
            }
        }
        public static bool LastFromCache = false;
        public static string ManifestCachePath { get { return Path.Combine(Config.BaseDir, "manifest_cache.json"); } }
        static List<ManifestVer> _manifestCache;
        static object _manLock = new object();
        public static List<ManifestVer> GetVersions(string mirror = null)
        {
            lock (_manLock)
            {
                if (_manifestCache != null) return _manifestCache;
                mirror = mirror ?? Config.Load().Mirror;
                LastFromCache = false;
                string json = null;
                try { json = DownloadString("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", mirror); }
                catch
                {
                    try { if (File.Exists(ManifestCachePath)) { json = File.ReadAllText(ManifestCachePath, Encoding.UTF8); LastFromCache = true; } } catch { }
                }
                if (json == null) throw new Exception("versionmanifest fetch failed and no local cache available");
                if (!LastFromCache)
                {
                    try { Directory.CreateDirectory(Config.BaseDir); File.WriteAllText(ManifestCachePath, json, Encoding.UTF8); } catch { }
                }
                var root = Json.D(Json.Parse(json));
                var arr = Json.A(root["versions"]);
                List<ManifestVer> list = new List<ManifestVer>();
                foreach (object o in arr)
                {
                    ManifestVer v = new ManifestVer();
                    v.Id = Json.S(o, "id", ""); v.Type = Json.S(o, "type", "release");
                    v.Time = Json.S(o, "releaseTime", ""); v.Url = Json.S(o, "url", ""); v.Sha1 = Json.S(o, "sha1", null);
                    if (v.Id.Length > 0 && v.Url.Length > 0) list.Add(v);
                }
                _manifestCache = list;
                return list;
            }
        }
        public static void ClearManifestCache() { lock (_manLock) { _manifestCache = null; } }
    }

    class ManifestVer
    {
        public string Id, Type, Time, Url, Sha1;
    }
}
