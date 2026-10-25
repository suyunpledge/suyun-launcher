using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace SuyunCraft
{
    static class AiIntegration
    {
        public static string BundleDirectory()
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai-mod");
            return File.Exists(Path.Combine(local, "dist", "suyuncraft-ai-0.5.0.jar")) ? local : Path.Combine(Config.BaseDir, "ai-mod");
        }

        public static string GameDirectory(Config cfg, string version)
        {
            var metadata = LaunchPlanner.LoadMerged(cfg.GameDir, version);
            string mc = Json.S(metadata, "jar", version);
            if (new MinecraftLoaderDetector().Detect(metadata) != "fabric" || mc != "1.21.1")
                throw new InvalidOperationException("AI 模组目前要求 Minecraft 1.21.1 + Fabric；请在下载页安装并选择对应版本。");
            return GameDirectories.Resolve(cfg, version);
        }

        public static string Install(Config cfg, string version, string bundle = null)
        {
            using (GameDirectories.Acquire(cfg, version)) return InstallCore(cfg, version, bundle);
        }
        static string InstallCore(Config cfg, string version, string bundle)
        {
            string root = GameDirectory(cfg, version); bundle = bundle ?? BundleDirectory();
            string jar = Path.Combine(bundle, "dist", "suyuncraft-ai-0.5.0.jar");
            string api = Path.Combine(bundle, "dist", "fabric-api-0.106.0+1.21.1.jar");
            if (!File.Exists(jar) || !File.Exists(api)) throw new FileNotFoundException("AI 模组尚未构建，请先运行 ai-mod/build-mod.ps1。");
            if (ModManagement.ReadFile(jar, Path.GetFileName(jar)).Id != "suyuncraft_ai" || ModManagement.ReadFile(api, Path.GetFileName(api)).Id != "fabric-api")
                throw new InvalidDataException("AI 安装包中的模组 ID 不正确。");
            string destination = FileSafety.Under(root, "mods/suyuncraft-ai.jar");
            if (File.Exists(destination) && ModManagement.ReadFile(destination, "suyuncraft-ai.jar").Id != "suyuncraft_ai")
                throw new InvalidDataException("suyuncraft-ai.jar 已被其他或损坏的模组占用，请先在模组管理中停用。");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            // Quarantine previous versioned files to avoid duplicate mod IDs on upgrade.
            var migrated = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(destination)).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
                {
                    string safe = FileSafety.Under(root, "mods/" + Path.GetFileName(file));
                    if (string.Equals(safe, destination, StringComparison.OrdinalIgnoreCase)) continue;
                    if (ModManagement.ReadFile(safe, Path.GetFileName(file)).Id != "suyuncraft_ai") continue;
                    string saved = FileSafety.Under(root, "suyuncraft-ai-backups/" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(file));
                    Directory.CreateDirectory(Path.GetDirectoryName(saved));
                    File.Move(safe, saved);
                    migrated.Add(new System.Collections.Generic.KeyValuePair<string, string>(safe, saved));
                }
                FileSafety.WriteViaTemp(destination, ".tmp", t => File.Copy(jar, t));
            }
            catch
            {
                foreach (var moved in migrated) if (!File.Exists(moved.Key)) File.Move(moved.Value, moved.Key);
                throw;
            }
            // Preserve an existing Fabric API installation instead of creating duplicate mod IDs.
            bool hasApi = Directory.EnumerateFiles(Path.GetDirectoryName(destination)).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .Any(path => ModManagement.ReadFile(FileSafety.Under(root, "mods/" + Path.GetFileName(path)), Path.GetFileName(path)).Id == "fabric-api");
            if (!hasApi)
            {
                string apiDestination = FileSafety.Under(root, "mods/" + Path.GetFileName(api));
                if (File.Exists(apiDestination)) throw new InvalidDataException("Fabric API 文件名已被占用，请先停用该文件。");
                FileSafety.WriteViaTemp(apiDestination, ".tmp", t => File.Copy(api, t));
            }
            string config = FileSafety.Under(root, "config/suyuncraft-ai.json");
            if (!File.Exists(config))
            {
                byte[] bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                FileSafety.WriteText(config, Json.Dump(new {
                    endpoint = "http://127.0.0.1:18765/plan", token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(),
                    allowWorldInteraction = false, cooldownSeconds = 10, dailyRequests = 30, maxOutputTokens = 384,
                    showWorkHud = true, hudX = 6, hudY = 6
                }));
            }
            return config;
        }
    }
}
