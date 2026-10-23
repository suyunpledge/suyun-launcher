using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuyunCraft
{
    static class RegressionTests
    {
        static int passed;
        static string root;
        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception) { rejected = true; }
            Check(rejected, name);
        }
        static Dictionary<string, object> Parse(string json) { return Json.D(Json.Parse(json)); }
        static void WriteVersion(string id, string json)
        {
            FileSafety.WriteText(FileSafety.VersionFile(root, id, ".json"), json);
        }
        static LaunchPlanner.LaunchPlan Plan(string id, int width, int height, bool prepareNatives = true)
        {
            return LaunchPlanner.Build(new Config { GameDir = root, JavaPath = @"C:\Program Files\Java\bin\javaw.exe" },
                id, 2, "Test_Player", false, width, height, prepareNatives);
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--echo")
            {
                foreach (string arg in args.Skip(1)) Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(arg)));
                return 0;
            }
            try
            {
                Program.ConfigureRuntime();
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 32;
                if (args.Contains("--direct")) WebRequest.DefaultWebProxy = null;
                root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                if (args.Contains("--ui-smoke")) { UiSmoke(); return 0; }
                Offline();
                if (args.Contains("--integration")) Integration();
                Console.WriteLine("RESULT: " + passed + " checks passed. Fixtures: " + root);
                return 0;
            }
            catch (Exception ex) { Console.WriteLine(ex); return 1; }
        }

        static void UiSmoke()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            using (var form = new MainForm())
            {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Check(form.Controls.Find("aiInstallButton", true).Length == 1 && form.Controls.Find("aiBridgeButton", true).Length == 1,
                    "AI install and bridge controls are attached to settings");
                var createDownload = typeof(MainForm).GetMethod("CreateDownloadCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var download = (Func<List<DlItem>, CancellationToken, Task>)createDownload.Invoke(form, null);
                var adapterTask = Task.Run(() => download(new List<DlItem>(), CancellationToken.None));
                var adapterWatch = Stopwatch.StartNew();
                while (!adapterTask.IsCompleted && adapterWatch.ElapsedMilliseconds < 5000)
                { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
                Check(adapterTask.IsCompleted, "Background backend download adapter returns to UI without deadlock");
                adapterTask.GetAwaiter().GetResult();
                var field = typeof(MainForm).GetField("cmbVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var combo = (System.Windows.Forms.ComboBox)field.GetValue(form);
                combo.Items.Clear(); combo.Items.Add("[F] fabric-loader-test"); combo.SelectedIndex = 0;
                var selected = typeof(MainForm).GetMethod("SelectedVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Check((string)selected.Invoke(form, null) == "fabric-loader-test", "Fabric UI selection strips full emoji prefix");
                var switchPage = typeof(MainForm).GetMethod("SwitchPage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                foreach (string page in new[] { "home", "dl", "set" })
                {
                    switchPage.Invoke(form, new object[] { page });
                    using (var image = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-" + page + ".png"));
                    }
                }
                foreach (string control in new[] { "numW", "numH" })
                {
                    var item = (System.Windows.Forms.Control)typeof(MainForm).GetField(control, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(form);
                    Check(item.Parent != null, "Window size input visible in control tree " + control);
                }
            }
            Console.WriteLine("UI RESULT: " + passed + " checks passed");
        }

        static void Offline()
        {
            BackendRegressionTests.Run(Path.Combine(root, "backend"), Check);
            AiIntegrationTests.Run(Path.Combine(root, "ai-integration"), Check);
            ModResolutionRegressionTests.Run(Path.Combine(root, "modres"), Check);
            VerifyStoreRegressionTests.Run(Path.Combine(root, "verifystore"), Check);
            string configPath = Path.Combine(root, "config.json");
            var config = new Config { AutoMemory = false, VersionIsolation = true, CaptureGameLog = false,
                GameDir = root, MemoryGb = 6, WindowWidth = 1234, WindowHeight = 777 };
            config.Save(configPath);
            Config loaded = Config.Load(configPath);
            Check(!loaded.AutoMemory && loaded.VersionIsolation && !loaded.CaptureGameLog, "Boolean settings survive save/load");
            Check(loaded.MemoryGb == 6 && loaded.WindowWidth == 1234 && loaded.WindowHeight == 777, "Numeric settings survive save/load");
            File.WriteAllText(configPath, "{\"AutoMemory\":\"False\",\"VersionIsolation\":\"True\",\"CaptureGameLog\":\"false\",\"MemoryGb\":-1,\"WindowHeight\":-3}");
            loaded = Config.Load(configPath);
            Check(!loaded.AutoMemory && loaded.VersionIsolation && !loaded.CaptureGameLog, "Legacy string booleans accepted");
            Check(loaded.MemoryGb == 1 && loaded.WindowHeight == 0, "Invalid numeric settings clamped");

            foreach (string path in new[] { "../outside", "a/../../outside", @"C:\outside", @"\\server\share", "file:stream", "a/CON.txt", "a/b. " })
                Reject(() => FileSafety.Under(root, path), "Reject unsafe path " + path);
            Reject(() => FileSafety.VersionFile(root, "../bad", ".json"), "Reject unsafe version id");
            Check(FileSafety.Under(root, "libraries/a/b.jar").StartsWith(root), "Normal library path accepted");
            Reject(() => Net.Open("http://localhost/file", CancellationToken.None), "Reject plaintext downloads");
            Reject(() => Net.Open("file:///C:/Windows/win.ini", CancellationToken.None), "Reject file URLs");
            string good = Path.Combine(root, "good.bin"); File.WriteAllText(good, "ABCD");
            string hash = FileSafety.Hash(good, false);
            File.WriteAllText(good, "EFGH");
            Check(!FileSafety.Matches(good, hash, 4), "Detect equal-size file corruption using SHA1");
            Check(!FileSafety.Matches(good, null, 3), "Detect incorrect file length");
            FileSafety.WriteText(good, "replaced");
            Check(File.ReadAllText(good) == "replaced", "Atomic replacement of existing files");
            string longFile = Path.Combine(root, new string('a', 90), new string('b', 90), "long-path-test.txt");
            FileSafety.WriteText(longFile, "long-path-ok");
            Check(longFile.Length > 260 && File.ReadAllText(longFile) == "long-path-ok", "Paths longer than 260 characters work");

            string maliciousZip = Path.Combine(root, "traversal.zip");
            using (var zip = ZipFile.Open(maliciousZip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open())) writer.Write("unsafe");
            Reject(() => FileSafety.ExtractZip(maliciousZip, Path.Combine(root, "unpacked")), "ZIP traversal rejected");
            Check(!File.Exists(Path.Combine(root, "escaped.txt")), "ZIP cannot overwrite outside its destination");

            string[] arguments = { "", "simple", "with space", "tab\there", "引号中文", "a\"b", @"C:\space here\", "\\\"", "--name", "-Dfile=x\"y" };
            var info = new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,
                CommandLine.Join(new[] { "--echo" }.Concat(arguments))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var process = Process.Start(info))
            {
                string[] actual = process.StandardOutput.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                process.WaitForExit();
                for (int i = 0; i < arguments.Length; i++)
                    Check(Encoding.UTF8.GetString(Convert.FromBase64String(actual[i])) == arguments[i], "Windows process argument roundtrip " + i);
            }
            Check(CommandLine.Split("-Dpath=\"C:\\Game Files\\a\" \"\"").SequenceEqual(new[] { "-Dpath=C:\\Game Files\\a", "" }), "Parse quoted JVM values and empty arguments");
            Reject(() => CommandLine.Split("\"unclosed"), "Reject unclosed JVM quote");

            Check(LaunchPlanner.RulesPass(null), "Absent rules allow library");
            Check(!LaunchPlanner.RulesPass(Json.Parse("[{\"action\":\"allow\",\"os\":{\"name\":\"osx\"}}]")), "macOS-only library excluded on Windows");
            Check(!LaunchPlanner.RulesPass(Json.Parse("[]")), "Empty explicit rules disallow");
            Check(LaunchPlanner.RulesPass(Json.Parse("[{\"action\":\"allow\"},{\"action\":\"disallow\",\"os\":{\"name\":\"osx\"}}]")), "Last matching rule wins");
            Check(!LaunchPlanner.RulesPass(Json.Parse("[{\"action\":\"allow\",\"os\":{\"version\":\"^999\\\\.\"}}]")), "OS version expression evaluated");
            Check(LaunchPlanner.RulesPass(Json.Parse("[{\"action\":\"allow\",\"features\":{\"is_demo_user\":false}}]")), "False feature requirements match default state");
            Check(LaunchPlanner.MavenPath("g.a:lib:1:natives-windows@zip") == "g/a/lib/1/lib-1-natives-windows.zip", "Maven classifier and extension");
            Reject(() => LaunchPlanner.MavenPath("g:../bad:1"), "Maven traversal rejected");

            WriteVersion("base", "{\"id\":\"base\",\"mainClass\":\"test.Main\",\"libraries\":[{\"name\":\"g:lib:1\"}],\"arguments\":{\"jvm\":[\"-cp\",\"${classpath}\"],\"game\":[\"--base\",{\"rules\":[{\"action\":\"allow\",\"features\":{\"has_custom_resolution\":true}}],\"value\":[\"--width\",\"${resolution_width}\",\"--height\",\"${resolution_height}\"]}]}}");
            WriteVersion("fabric", "{\"id\":\"fabric\",\"inheritsFrom\":\"base\",\"libraries\":[{\"name\":\"g:lib:2\"}],\"arguments\":{\"game\":[\"--child\"]}}");
            var merged = LaunchPlanner.LoadMerged(root, "fabric");
            Check(Json.S(merged, "jar", "") == "base", "Fabric uses parent client jar");
            Check(Json.A(Json.O(merged, "libraries")).Length == 1 && Json.S(Json.A(Json.O(merged, "libraries"))[0], "name", "") == "g:lib:2", "Child overrides parent library version");
            Check((string)Json.A(Json.O(Json.O(merged, "arguments"), "game"))[0] == "--base", "Parent arguments precede child arguments");
            var plan = Plan("fabric", 1000, 700);
            Check(plan.JvmArgs.Any(arg => arg.Contains("base.jar")) && !plan.JvmArgs.Any(arg => arg.Contains("fabric.jar")), "Inherited jar on classpath");
            Check(plan.GameArgs.Contains("1000") && plan.GameArgs.Count(arg => arg == "--width") == 1, "Custom resolution feature expanded once");
            Reject(() => LaunchPlanner.Build(new Config { GameDir = root }, "base", 2, "bad name", false, 0, 0), "Invalid offline player name rejected");
            WriteVersion("cycle-a", "{\"inheritsFrom\":\"cycle-b\"}"); WriteVersion("cycle-b", "{\"inheritsFrom\":\"cycle-a\"}");
            Reject(() => LaunchPlanner.LoadMerged(root, "cycle-a"), "Cyclic version inheritance rejected");
            WriteVersion("legacy", "{\"mainClass\":\"test.Main\",\"minecraftArguments\":\"--gameDir ${game_directory}\"}");
            var legacy = Plan("legacy", 0, 0);
            Check(legacy.GameArgs.Count == 2 && legacy.GameArgs[1] == root, "Legacy game directory remains one argument");

            // A legacy LWJGL entry can contain both the Java artifact and a platform native.
            FileSafety.WriteText(FileSafety.VersionFile(root, "libs", ".jar"), "client");
            WriteVersion("libs", "{\"libraries\":[{\"name\":\"org.lwjgl:lwjgl:2\",\"natives\":{\"windows\":\"natives-windows-${arch}\"},\"downloads\":{\"artifact\":{\"path\":\"lwjgl.jar\",\"url\":\"https://example.com/lwjgl.jar\"},\"classifiers\":{\"natives-windows-64\":{\"path\":\"win64.jar\",\"url\":\"https://example.com/win64.jar\"},\"natives-windows-32\":{\"path\":\"win32.jar\",\"url\":\"https://example.com/win32.jar\"}}}}]}");
            var missing = LaunchPlanner.CollectMissing(root, LaunchPlanner.LoadMerged(root, "libs"), "libs");
            Check(missing.Count == 2 && missing.Any(item => item.Dest.EndsWith("lwjgl.jar")), "Collect both Java artifact and native");
            Check(missing.Any(item => item.Dest.EndsWith(Environment.Is64BitOperatingSystem ? "win64.jar" : "win32.jar")), "Native architecture placeholder resolved");
            string nativeArchive = FileSafety.Under(root, "libraries/evil-native.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(nativeArchive));
            using (var zip = ZipFile.Open(nativeArchive, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("../escaped-native.dll").Open())) writer.Write("unsafe");
            WriteVersion("evil-native", "{\"jar\":\"libs\",\"libraries\":[{\"name\":\"g:n:1\",\"natives\":{\"windows\":\"natives-windows\"},\"downloads\":{\"classifiers\":{\"natives-windows\":{\"path\":\"evil-native.jar\",\"url\":\"https://example.com/native.jar\"}}}}]}");
            Reject(() => Plan("evil-native", 0, 0), "Native ZIP traversal fails launch plan");

            string objectSource = Path.Combine(root, "object-source"); File.WriteAllText(objectSource, "asset-data");
            string objectHash = FileSafety.Hash(objectSource, false);
            string indexJson = "{\"virtual\":true,\"map_to_resources\":true,\"objects\":{\"sounds/test.ogg\":{\"hash\":\"" + objectHash + "\",\"size\":10}}}";
            string indexPath = FileSafety.Under(root, "assets/indexes/test.json"); FileSafety.WriteText(indexPath, indexJson);
            string indexHash = FileSafety.Hash(indexPath, false);
            File.Move(indexPath, indexPath + ".saved");
            WriteVersion("assets", "{\"jar\":\"libs\",\"assetIndex\":{\"id\":\"test\",\"url\":\"https://example.com/index.json\",\"sha1\":\"" + indexHash + "\"}}");
            merged = LaunchPlanner.LoadMerged(root, "assets");
            Check(LaunchPlanner.CollectMissing(root, merged, "assets").Single().Dest == indexPath, "First asset phase discovers index");
            File.Move(indexPath + ".saved", indexPath);
            missing = LaunchPlanner.CollectMissing(root, merged, "assets");
            Check(missing.Count == 1 && missing[0].Sha1 == objectHash, "Second asset phase discovers hashed objects");
            Directory.CreateDirectory(Path.GetDirectoryName(missing[0].Dest)); File.Copy(objectSource, missing[0].Dest);
            Check(LaunchPlanner.CollectMissing(root, merged, "assets").Count == 0, "All assets complete after object download");
            Check(File.Exists(FileSafety.Under(root, "assets/virtual/test/sounds/test.ogg")) && File.Exists(FileSafety.Under(root, "resources/sounds/test.ogg")), "Legacy virtual and resource maps materialized");

            var manager = new DownloadManager { Mirror = "official" };
            manager.Queue.Enqueue(new DlItem { Url = "http://localhost/rejected", Dest = good, Size = 8, Desc = "invalid transport" });
            manager.RunAsync().GetAwaiter().GetResult();
            Check(manager.Finished && manager.Error != null && manager.DoneFiles == 0, "Failed worker reports completion and error");
            Check(File.ReadAllText(good) == "replaced" && manager.InflightBytes == 0, "Failed download preserves destination and progress");
            Check(!Directory.GetFiles(root, "*.part", SearchOption.AllDirectories).Any(), "Failed download leaves no partial files");
            manager = new DownloadManager { Cancelled = true };
            manager.RunAsync().GetAwaiter().GetResult();
            Check(manager.Finished && manager.Cancelled, "Pre-cancelled manager completes");
            Parallel.For(0, 100, i => { if (Json.S(Json.Parse("{\"id\":\"parallel\"}"), "id", "") != "parallel") throw new Exception(); });
            Check(true, "Concurrent JSON parsing");
        }

        static void Integration()
        {
            var manifest = Net.GetVersions("official");
            Check(manifest.Count > 100, "Live Mojang manifest");
            foreach (string id in new[] { "1.7.10", "1.12.2", "1.16.5", "1.20.4", "1.21.1", manifest.First(version => version.Type == "release").Id }.Distinct())
            {
                var entry = manifest.First(version => version.Id == id);
                string destination = FileSafety.VersionFile(root, id, ".json");
                var manager = new DownloadManager { Mirror = "official" };
                manager.Queue.Enqueue(new DlItem { Dest = destination, Url = entry.Url, Sha1 = entry.Sha1, Desc = id });
                manager.RunAsync().GetAwaiter().GetResult();
                Check(manager.Error == null && manager.DoneFiles == 1 && FileSafety.Matches(destination, entry.Sha1, 0), "Live verified version metadata " + id);
                var merged = LaunchPlanner.LoadMerged(root, id);
                var missing = LaunchPlanner.CollectMissing(root, merged, id);
                Check(missing.Count > 5 && !missing.Any(item => item.Dest.IndexOf("natives-linux", StringComparison.OrdinalIgnoreCase) >= 0 || item.Dest.IndexOf("natives-macos", StringComparison.OrdinalIgnoreCase) >= 0), "Windows dependency selection " + id);
                var natives = new DownloadManager { Mirror = "official" };
                foreach (var item in missing.Where(item => item.Dest.IndexOf("natives-windows", StringComparison.OrdinalIgnoreCase) >= 0 && item.Size < 2000000).Take(1))
                    natives.Queue.Enqueue(item);
                natives.RunAsync().GetAwaiter().GetResult();
                if (natives.Error != null) throw natives.Error;
                Check(natives.Error == null && natives.DoneFiles == natives.TotalFiles, "Live verified Windows natives " + id);
                var plan = Plan(id, 854, 480, false);
                Check(plan.GameArgs.All(arg => !arg.Contains("${")) && plan.JvmArgs.All(arg => !arg.Contains("${")), "Live version launch placeholders " + id);
            }
            string loaderJson = Net.DownloadString("https://meta.fabricmc.net/v2/versions/loader/1.21.1", "official");
            string loader = Json.S(Json.O(Json.A(Json.Parse(loaderJson))[0], "loader"), "version", "");
            string profile = Net.DownloadString("https://meta.fabricmc.net/v2/versions/loader/1.21.1/" + loader + "/profile/json", "official");
            string fabricId = Json.S(Json.Parse(profile), "id", "");
            WriteVersion(fabricId, profile);
            var fabricPlan = Plan(fabricId, 854, 480, false);
            Check(fabricPlan.MainClass.Contains("fabric") && fabricPlan.JvmArgs.Any(arg => arg.Contains("1.21.1.jar")), "Live Fabric profile and parent client");
            string releases = Net.DownloadString("https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse", "official");
            Check(FileSafety.IsHash(Json.S(Json.O(Json.O(Json.A(Json.Parse(releases))[0], "binary"), "package"), "checksum", ""), 64), "Live Adoptium SHA256 metadata");
            foreach (string java in JavaUtil.Detect().Take(3)) Check(JavaUtil.GetMajor(java) > 0, "Installed Java probe " + java);
        }
    }
}
