using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace SuyunCraft
{
    static class Program
    {
        public const string AppName = "SuyunCraft";
        public const string AppVersion = "1.5";

        [STAThread]
        static void Main(string[] args)
        {
            ConfigureRuntime();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 32;
            ServicePointManager.Expect100Continue = false;

            if (args != null && args.Length > 0)
            {
                if (args[0] == "--dry-run") { Cli.DryRun(args); return; }
                if (args[0] == "--manifest") { Cli.ManifestTest(); return; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public static void ConfigureRuntime()
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        }
    }

    static class Cli
    {
        public static void DryRun(string[] args)
        {
            try
            {
                string vid = args.Length > 1 ? args[1] : "test-1.21";
                string gameDir = null, java = null; int mem = 4; string user = "Tester";
                for (int i = 2; i + 1 < args.Length; i += 2)
                {
                    if (args[i] == "--gamedir") gameDir = args[i + 1];
                    else if (args[i] == "--java") java = args[i + 1];
                    else if (args[i] == "--memory") int.TryParse(args[i + 1], out mem);
                    else if (args[i] == "--user") user = args[i + 1];
                }
                if (gameDir == null) gameDir = Config.DefaultGameDir;
                var cfg = new Config();
                cfg.GameDir = gameDir; cfg.JavaPath = java ?? ""; cfg.MemoryGb = mem; cfg.UserName = user;
                var plan = LaunchPlanner.Build(cfg, vid, mem, user, false, 0, 0, false);
                string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dryrun_cmd.txt");
                string text = "JAVA=" + plan.JavaExe + "\r\nCWD=" + plan.WorkingDir + "\r\nCMD=" + plan.CommandText + "\r\n";
                File.WriteAllText(outPath, text, Encoding.UTF8);
                Console.WriteLine(text);
                Console.WriteLine("[OK] dry-run 写入 " + outPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        public static void ManifestTest()
        {
            try
            {
                var list = Net.GetVersions();
                var latest = list.FirstOrDefault(v => v.Type == "release");
                Console.WriteLine("[OK] 版本清单获取成功，共 " + list.Count + " 个版本，最新正式版: " + (latest != null ? latest.Id : "?"));
                foreach (var v in list.Take(5)) Console.WriteLine("  " + v.Id + "  (" + v.Type + ")");
            }
            catch (Exception ex) { Console.WriteLine("[FAIL] " + ex.Message); Environment.ExitCode = 1; }
        }
    }
}
