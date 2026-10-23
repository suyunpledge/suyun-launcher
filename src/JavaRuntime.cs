using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuyunCraft
{
    static class JavaUtil
    {
        public static List<string> Detect()
        {
            List<string> roots = new List<string>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string runtime = Path.Combine(appData, ".minecraft", "runtime");
            AddBinDirs(runtime, 1, roots);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (string baseDir in new string[] { pf, pf86 })
                foreach (string sub in new string[] { "Java", "Eclipse Adoptium", "Microsoft", "Zulu", "Amazon Corretto", "BellSoft" })
                    AddBinDirs(Path.Combine(baseDir, sub), 1, roots);
            AddBinDirs(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"), 1, roots);
            string managed = Path.Combine(Config.BaseDir, "java");
            AddBinDirs(managed, 1, roots);
            string jh = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(jh)) roots.Add(Path.Combine(jh, "bin"));

            string searchPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string path in searchPath.Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(path)) roots.Add(path.Trim().Trim('"'));
            foreach (string family in SafeDirs(runtime)) AddBinDirs(family, 2, roots);
            foreach (string installation in SafeDirs(managed)) AddBinDirs(installation, 1, roots);

            List<string> found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string r in roots)
            {
                string jw = Path.Combine(r, "javaw.exe");
                if (File.Exists(jw) && seen.Add(jw)) found.Add(jw);
            }
            return found;
        }

        static void AddBinDirs(string root, int depth, List<string> roots)
        {
            foreach (string dir in SafeDirs(root))
            {
                roots.Add(Path.Combine(dir, "bin"));
                if (depth >= 2)
                    foreach (string child in SafeDirs(dir)) roots.Add(Path.Combine(child, "bin"));
            }
        }

        static string[] SafeDirs(string root)
        {
            try { return Directory.Exists(root) ? Directory.GetDirectories(root) : new string[0]; }
            catch { return new string[0]; }
        }

        public static int GetMajor(string javawPath)
        {
            try
            {
                string consoleJava = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(javawPath)), "java.exe");
                if (!File.Exists(consoleJava)) consoleJava = javawPath;
                var info = new ProcessStartInfo(consoleJava, "-version") {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true, RedirectStandardOutput = true
                };
                using (var process = Process.Start(info))
                {
                    Task<string> stderr = process.StandardError.ReadToEndAsync();
                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(8000)) { process.Kill(); process.WaitForExit(2000); return -1; }
                    string output = stderr.GetAwaiter().GetResult() + " " + stdout.GetAwaiter().GetResult();
                    var match = System.Text.RegularExpressions.Regex.Match(output, "version\\s+\"(1\\.)?([0-9]+)");
                    int major;
                    return match.Success && int.TryParse(match.Groups[2].Value, out major) ? major : -1;
                }
            }
            catch { return -1; }
        }

        public static string Select(string preferred, int required)
        {
            // The usual path needs one probe, without scanning all runtime installation directories.
            int preferredMajor = !string.IsNullOrEmpty(preferred) && File.Exists(preferred) ? GetMajor(preferred) : -1;
            if (preferredMajor == required) return preferred;
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred)) candidates.Add(preferred);
            candidates.AddRange(Detect());
            string newer = null;
            foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int major = string.Equals(path, preferred, StringComparison.OrdinalIgnoreCase) ? preferredMajor : GetMajor(path);
                if (major == required) return path;
                if (required > 8 && major > required && newer == null) newer = path;
            }
            if (newer != null) return newer;
            throw new InvalidOperationException("未找到匹配的 Java " + required + "。请到设置中下载或选择对应 Java。");
        }

    }
}
