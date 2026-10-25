using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace SuyunCraft
{
    static class JavaUtil
    {
        static readonly ConcurrentDictionary<string, Tuple<string, int>> probes = new ConcurrentDictionary<string, Tuple<string, int>>(StringComparer.OrdinalIgnoreCase);
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

        public static int GetMajor(string javawPath, CancellationToken cancellation = default(CancellationToken))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                string consoleJava = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(javawPath)), "java.exe");
                if (!File.Exists(consoleJava)) consoleJava = javawPath;
                var executable = new FileInfo(consoleJava);
                var release = new FileInfo(Path.Combine(executable.Directory.Parent.FullName, "release"));
                string fingerprint = executable.Length + "|" + executable.LastWriteTimeUtc.Ticks + "|" +
                    (release.Exists ? release.Length + "|" + release.LastWriteTimeUtc.Ticks : "");
                Tuple<string, int> cached;
                if (probes.TryGetValue(executable.FullName, out cached) && cached.Item1 == fingerprint) return cached.Item2;
                var info = new ProcessStartInfo(consoleJava, "-version") {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true, RedirectStandardOutput = true
                };
                using (var process = Process.Start(info))
                {
                    try
                    {
                        Task<string> stderr = process.StandardError.ReadToEndAsync();
                        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                        var watch = Stopwatch.StartNew();
                        while (!process.WaitForExit(100))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (watch.ElapsedMilliseconds >= 8000) return -1;
                        }
                        cancellation.ThrowIfCancellationRequested();
                        if (!Task.WaitAll(new Task[] { stderr, stdout }, 500, cancellation)) return -1;
                        string output = stderr.Result + " " + stdout.Result;
                        var match = System.Text.RegularExpressions.Regex.Match(output, "version\\s+\"(1\\.)?([0-9]+)");
                        int major;
                        if (process.ExitCode != 0 || !match.Success || !int.TryParse(match.Groups[2].Value, out major)) return -1;
                        if (probes.Count >= 128) probes.Clear();
                        probes[executable.FullName] = Tuple.Create(fingerprint, major);
                        return major;
                    }
                    finally { try { if (!process.HasExited) process.Kill(); } catch { } }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { return -1; }
        }

        public static string Select(string preferred, int required, CancellationToken cancellation = default(CancellationToken))
        {
            cancellation.ThrowIfCancellationRequested();
            // The usual path needs one probe, without scanning all runtime installation directories.
            int preferredMajor = !string.IsNullOrEmpty(preferred) && File.Exists(preferred) ? GetMajor(preferred, cancellation) : -1;
            if (preferredMajor == required) return preferred;
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred)) candidates.Add(preferred);
            candidates.AddRange(Detect());
            string newer = null;
            foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellation.ThrowIfCancellationRequested();
                int major = string.Equals(path, preferred, StringComparison.OrdinalIgnoreCase) ? preferredMajor : GetMajor(path, cancellation);
                if (major == required) return path;
                if (required > 8 && major > required && newer == null) newer = path;
            }
            if (newer != null) return newer;
            throw new InvalidOperationException("No matching Java found " + required + "。Download or select a matching Java in Settings.");
        }

    }
}
