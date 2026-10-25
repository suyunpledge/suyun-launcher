using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuyunCraft
{
    enum LaunchStage { Metadata, Preflight, Java, Files, Plan, Ready }

    sealed class LaunchProgress
    {
        public LaunchStage Stage;
        public string Message;
    }

    sealed class LaunchIssue
    {
        public string Code, Message;
        public bool BlocksLaunch;
        public LaunchIssue(string code, string message, bool blocks = false)
        { Code = code; Message = message; BlocksLaunch = blocks; }
    }

    sealed class LaunchReport
    {
        public string Version, Loader, Status = "preparing", FailedStage;
        public int RequiredJava;
        public long PreparationMilliseconds;
        public readonly Dictionary<string, long> StageMilliseconds = new Dictionary<string, long>();
        public readonly List<LaunchIssue> Issues = new List<LaunchIssue>();
        // Deliberately excludes command lines, account data and tokens.
        public void Save(string path) { FileSafety.WriteText(path, Json.Dump(this)); }
    }

    sealed class LaunchContext
    {
        public readonly string Version, GameDirectory, Loader;
        public LaunchContext(string version, string gameDirectory, string loader)
        { Version = version; GameDirectory = gameDirectory; Loader = loader; }
    }

    interface ILaunchPreflightCheck
    {
        IEnumerable<LaunchIssue> Inspect(LaunchContext context, CancellationToken cancellation);
    }

    // Extension point for loader recognition. Installation/processors belong in a separate installer.
    interface ILoaderDetector
    {
        string Detect(Dictionary<string, object> metadata);
    }

    sealed class MinecraftLoaderDetector : ILoaderDetector
    {
        public string Detect(Dictionary<string, object> metadata)
        {
            var names = (Json.A(Json.O(metadata, "libraries")) ?? new object[0])
                .Select(lib => Json.S(lib, "name", "")).ToArray();
            if (names.Any(n => n.StartsWith("net.neoforged:", StringComparison.Ordinal))) return "neoforge";
            if (names.Any(n => n.StartsWith("net.minecraftforge:", StringComparison.Ordinal))) return "forge";
            if (names.Any(n => n.StartsWith("org.quiltmc:quilt-loader:", StringComparison.Ordinal))) return "quilt";
            if (names.Any(n => n.StartsWith("net.fabricmc:fabric-loader:", StringComparison.Ordinal))) return "fabric";
            string main = Json.S(metadata, "mainClass", "");
            if (main.StartsWith("org.quiltmc.loader.", StringComparison.Ordinal)) return "quilt";
            if (main.StartsWith("net.fabricmc.loader.", StringComparison.Ordinal)) return "fabric";
            return main == "net.minecraft.client.main.Main" || main == "net.minecraft.launchwrapper.Launch" ? "vanilla" : "unknown";
        }
    }

    static class VersionFiles
    {
        public static async Task EnsureAsync(string root, string id, Dictionary<string, object> metadata,
            Func<List<DlItem>, CancellationToken, Task> download, CancellationToken cancellation)
        {
            var cache = new FileSafety.VerifyCache { Cancellation = cancellation };
            for (int pass = 0; pass < 4; pass++)
            {
                cancellation.ThrowIfCancellationRequested();
                cache.SafeDirs.Clear(); // A download can create directories that did not exist during the previous pass.
                var missing = await Task.Run(() => LaunchPlanner.CollectMissing(root, metadata, id, cache), cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (missing.Count == 0) return;
                if (pass == 3) break; // Always verify after the final download.
                await download(missing, cancellation).ConfigureAwait(false);
                cache.MatchResults.Clear(); // Do not reuse pre-write observations after mutating the filesystem.
            }
            throw new InvalidDataException("Downloadstill missing after download; reinstall this version");
        }
    }

    sealed class PreparedLaunch
    {
        public LaunchPlanner.LaunchPlan Plan;
        public LaunchReport Report;
    }

    // One instance per launch. No WinForms, Process.Start, configuration persistence or account login here.
    sealed class LaunchPreparationService
    {
        readonly ILoaderDetector loader;
        readonly ILaunchPreflightCheck[] checks;
        readonly Func<string, int, string> selectJava;
        readonly Func<List<DlItem>, CancellationToken, Task> download;
        public LaunchReport LastReport { get; private set; }

        public LaunchPreparationService(ILoaderDetector loader = null, IEnumerable<ILaunchPreflightCheck> checks = null,
            Func<string, int, string> selectJava = null, Func<List<DlItem>, CancellationToken, Task> download = null)
        {
            this.loader = loader ?? new MinecraftLoaderDetector();
            this.checks = (checks ?? new ILaunchPreflightCheck[] { new ModPreflightCheck() }).ToArray();
            this.selectJava = selectJava;
            this.download = download;
        }

        public async Task<PreparedLaunch> PrepareAsync(Config configuration, string version,
            IProgress<LaunchProgress> progress, CancellationToken cancellation)
        {
            Config cfg = configuration.Snapshot();
            LastReport = new LaunchReport { Version = version };
            var total = Stopwatch.StartNew();
            try
            {
                var metadata = await Stage(LaunchStage.Metadata, "Reading version metadata…", () => {
                    FileSafety.Id(version);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(cfg.UserName ?? "", @"\A[A-Za-z0-9_]{1,16}\z"))
                        throw new ArgumentException("Offline player name must be 1-16 letters, digits or underscores");
                    return LaunchPlanner.LoadMerged(cfg.GameDir, version);
                }, progress, cancellation).ConfigureAwait(false);
                LastReport.Loader = loader.Detect(metadata);
                LastReport.RequiredJava = (int)Json.L(Json.O(metadata, "javaVersion"), "majorVersion", 8);
                await Stage(LaunchStage.Preflight, "Checking mod metadata…", () => {
                    string gameDirectory = GameDirectories.Resolve(cfg, version);
                    SaveIsolation.CheckWorlds(gameDirectory, !string.IsNullOrEmpty(cfg.SaveInstanceId), true);
                    var context = new LaunchContext(version, gameDirectory, LastReport.Loader);
                    string mcVersion = ModManagement.GameVersion(metadata, version);
                    foreach (var check in checks)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var modCheck = check as ModPreflightCheck;
                        if (modCheck != null)
                            LastReport.Issues.AddRange(modCheck.Inspect(context, cancellation, mcVersion, LastReport.RequiredJava));
                        else
                            LastReport.Issues.AddRange(check.Inspect(context, cancellation));
                    }
                    var errors = LastReport.Issues.Where(i => i.BlocksLaunch).ToArray();
                    if (errors.Length > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(i => i.Message)));
                    return true;
                }, progress, cancellation).ConfigureAwait(false);
                cfg.JavaPath = await Stage(LaunchStage.Java, "Selecting Java…", () => selectJava == null ? JavaUtil.Select(cfg.JavaPath, LastReport.RequiredJava, cancellation) : selectJava(cfg.JavaPath, LastReport.RequiredJava), progress, cancellation).ConfigureAwait(false);
                Notify(progress, LaunchStage.Files, "Verifying and completing game files…");
                LastReport.FailedStage = LaunchStage.Files.ToString();
                var filesWatch = Stopwatch.StartNew();
                try
                {
                    await VersionFiles.EnsureAsync(cfg.GameDir, version, metadata, download ?? (async (items, token) => {
                        var manager = new DownloadManager { Mirror = cfg.Mirror };
                        foreach (var item in items) manager.Queue.Enqueue(item);
                        await manager.RunAsync(token).ConfigureAwait(false);
                        if (manager.Error != null) throw manager.Error;
                        if (manager.Cancelled) throw new OperationCanceledException();
                    }), cancellation).ConfigureAwait(false);
                }
                finally { LastReport.StageMilliseconds["Files"] = filesWatch.ElapsedMilliseconds; }
                var plan = await Stage(LaunchStage.Plan, "Building launch arguments…", () => LaunchPlanner.Build(cfg, metadata, version,
                    cfg.MemoryGb, cfg.UserName, cfg.VersionIsolation, cfg.WindowWidth, cfg.WindowHeight, true, cancellation), progress, cancellation).ConfigureAwait(false);
                LastReport.Status = "ready"; LastReport.FailedStage = null;
                Notify(progress, LaunchStage.Ready, "LaunchPreparation done");
                return new PreparedLaunch { Plan = plan, Report = LastReport };
            }
            catch (OperationCanceledException) { LastReport.Status = "cancelled"; throw; }
            catch { LastReport.Status = "failed"; throw; }
            finally { LastReport.PreparationMilliseconds = total.ElapsedMilliseconds; }
        }

        async Task<T> Stage<T>(LaunchStage stage, string message, Func<T> action, IProgress<LaunchProgress> progress, CancellationToken token)
        {
            LastReport.FailedStage = stage.ToString();
            token.ThrowIfCancellationRequested();
            Notify(progress, stage, message);
            var watch = Stopwatch.StartNew();
            try
            {
                T result = await Task.Run(action, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return result;
            }
            finally { LastReport.StageMilliseconds[stage.ToString()] = watch.ElapsedMilliseconds; }
        }

        static void Notify(IProgress<LaunchProgress> progress, LaunchStage stage, string message)
        { if (progress != null) progress.Report(new LaunchProgress { Stage = stage, Message = message }); }
    }
}
