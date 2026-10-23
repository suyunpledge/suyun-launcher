using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SuyunCraft
{
    static class Json
    {
        [ThreadStatic] static JavaScriptSerializer _ser;
        public static JavaScriptSerializer Ser
        {
            get
            {
                if (_ser == null) { _ser = new JavaScriptSerializer(); _ser.MaxJsonLength = 32 * 1024 * 1024; _ser.RecursionLimit = 128; }
                return _ser;
            }
        }
        public static object Parse(string s) { return Ser.DeserializeObject(s); }
        public static string Dump(object o) { return Ser.Serialize(o); }
        public static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
        public static object[] A(object o) { return o as object[]; }
        public static object O(object o, string k)
        {
            var d = o as Dictionary<string, object>;
            if (d != null && d.ContainsKey(k)) return d[k];
            return null;
        }
        public static string S(object o, string k, string def)
        {
            object v = O(o, k);
            return v is string ? (string)v : def;
        }
        public static long L(object o, string k, long def)
        {
            object v = O(o, k);
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is double) return (long)(double)v;
            return def;
        }
        public static bool B(object o, string key, bool fallback)
        {
            object value = O(o, key);
            if (value is bool) return (bool)value;
            bool parsed;
            return value is string && bool.TryParse((string)value, out parsed) ? parsed : fallback;
        }
        public static bool Has(object o, string k) { object v = O(o, k); return v != null; }
    }

    class Config
    {
        public string GameDir = "";
        public string JavaPath = "";
        public string UserName = "Player";
        public int MemoryGb = 4;
        public bool AutoMemory = true;
        public string Mirror = Mirrors.Bmclapi;      // official | bmclapi
        public string ExtraJvm = "";
        public bool VersionIsolation = false;       // 版本隔离：每个版本独立 mods/configs
        public int WindowWidth = 854;
        public int WindowHeight = 480;
        public bool CaptureGameLog = true;
        public string LastVersion = "";

        // All configuration members are scalars/strings; isolate an in-flight launch from UI edits.
        public Config Snapshot() { return (Config)MemberwiseClone(); }

        public static string BaseDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuyunCraft"); }
        }
        public static string ConfigPath { get { return Path.Combine(BaseDir, "launcher_config.json"); } }
        public static string DefaultGameDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"); }
        }

        public static Config Load(string path = null)
        {
            path = path ?? ConfigPath;
            Config c = new Config();
            try
            {
                if (File.Exists(path))
                {
                    var d = Json.D(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                    if (d != null)
                    {
                        c.GameDir = Json.S(d, "GameDir", ""); c.JavaPath = Json.S(d, "JavaPath", "");
                        c.UserName = Json.S(d, "UserName", "Player"); c.MemoryGb = (int)Json.L(d, "MemoryGb", 4);
                        c.AutoMemory = Json.B(d, "AutoMemory", true); // bool 序列化
                        c.Mirror = Json.S(d, "Mirror", Mirrors.Bmclapi); c.ExtraJvm = Json.S(d, "ExtraJvm", "");
                        c.VersionIsolation = Json.B(d, "VersionIsolation", false);
                        c.WindowWidth = (int)Json.L(d, "WindowWidth", 854); c.WindowHeight = (int)Json.L(d, "WindowHeight", 480);
                        c.CaptureGameLog = Json.B(d, "CaptureGameLog", true);
                        c.LastVersion = Json.S(d, "LastVersion", "");
                    }
                }
            }
            catch { }
            if (string.IsNullOrEmpty(c.GameDir))
                c.GameDir = DefaultGameDir;
            c.MemoryGb = Math.Max(1, Math.Min(256, c.MemoryGb));
            c.WindowWidth = Math.Max(0, Math.Min(7680, c.WindowWidth));
            c.WindowHeight = Math.Max(0, Math.Min(4320, c.WindowHeight));
            if (c.Mirror != Mirrors.Official && c.Mirror != Mirrors.Bmclapi) c.Mirror = Mirrors.Bmclapi;
            return c;
        }
        public void Save(string path = null)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                FileSafety.WriteText(path ?? ConfigPath, Json.Dump(this));
            }
            catch (Exception ex) { MessageBox.Show("配置保存失败：" + ex.Message, "SuyunCraft"); }
        }
    }
}
