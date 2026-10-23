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
                    throw new InvalidDataException("下载地址必须使用 HTTPS");
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
                    if (string.IsNullOrEmpty(location)) throw new InvalidDataException("重定向缺少地址");
                    url = new Uri(uri, location).AbsoluteUri;
                    continue;
                }
                if (code != 200) { response.Dispose(); abort.Dispose(); throw new IOException("HTTP " + code); }
                return new Response(response, abort);
            }
            throw new IOException("重定向次数过多");
        }

        public static string DownloadString(string url, string mirror)
        {
            string[] tries = new string[] { Mirrors.Map(url, mirror), Mirrors.Map(url, Mirrors.Other(mirror)), url };
            List<string> cand = new List<string>();
            foreach (string u in tries) if (!cand.Contains(u)) cand.Add(u);
            return RaceFetch(cand);
        }

        // 多通道并发竞速：每个候选 URL 同时走「系统代理」与「直连」，谁先成功用谁。
        // 慢网络下串行重试需要逐源等满超时，竞速把最坏等待从"源数×超时"压缩到"最快通道"的耗时。
        static string RaceFetch(List<string> urls)
        {
            Exception last = null;
            List<Task<string>> tasks = new List<Task<string>>();
            foreach (string u in urls)
            {
                string cu = u;
                tasks.Add(Task.Factory.StartNew(delegate { try { return FetchString(cu, true); } catch (Exception ex) { last = ex; return null; } }));
                tasks.Add(Task.Factory.StartNew(delegate { try { return FetchString(cu, false); } catch (Exception ex) { last = ex; return null; } }));
            }
            List<Task<string>> pending = new List<Task<string>>(tasks);
            while (pending.Count > 0)
            {
                int idx = Task.WaitAny(pending.ToArray());
                Task<string> done = pending[idx];
                if (done.Result != null) return done.Result;
                pending.RemoveAt(idx);
            }
            throw new Exception("下载失败（已并发尝试 " + urls.Count + " 个源 × 代理/直连）: " + urls[0] + (last != null ? " —— " + last.Message : ""));
        }

        static string FetchString(string url, bool useSystemProxy)
        {
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            using (var resp = Open(url, deadline.Token, useSystemProxy))
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                var text = new StringBuilder();
                var buffer = new char[8192];
                int read;
                while ((read = sr.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (text.Length + read > 32 * 1024 * 1024) throw new InvalidDataException("JSON 文件过大");
                    text.Append(buffer, 0, read);
                }
                return text.ToString();
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
                if (json == null) throw new Exception("版本清单获取失败，且没有本地缓存可用");
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
