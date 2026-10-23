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
            return File.Exists(Path.Combine(local, "dist", "suyuncraft-ai-0.1.0.jar")) ? local : Path.Combine(Config.BaseDir, "ai-mod");
        }

        public static string GameDirectory(Config cfg, string version)
        {
            var metadata = LaunchPlanner.LoadMerged(cfg.GameDir, version);
            string mc = Json.S(metadata, "jar", version);
            if (new MinecraftLoaderDetector().Detect(metadata) != "fabric" || mc != "1.21.1")
                throw new InvalidOperationException("AI 模组目前要求 Minecraft 1.21.1 + Fabric；请在下载页安装并选择对应版本。");
            return cfg.VersionIsolation ? Path.GetDirectoryName(FileSafety.VersionFile(cfg.GameDir, version, ".json")) : Path.GetFullPath(cfg.GameDir);
        }

        public static string Install(Config cfg, string version, string bundle = null)
        {
            string root = GameDirectory(cfg, version); bundle = bundle ?? BundleDirectory();
            string jar = Path.Combine(bundle, "dist", "suyuncraft-ai-0.1.0.jar");
            string api = Path.Combine(bundle, "dist", "fabric-api-0.106.0+1.21.1.jar");
            if (!File.Exists(jar) || !File.Exists(api)) throw new FileNotFoundException("AI 模组尚未构建，请先运行 ai-mod/build-mod.ps1。");
            string destination = FileSafety.Under(root, "mods/suyuncraft-ai-0.1.0.jar");
            FileSafety.WriteViaTemp(destination, ".tmp", t => File.Copy(jar, t));
            // Preserve an existing Fabric API installation instead of creating duplicate mod IDs.
            bool hasApi = Directory.EnumerateFiles(Path.GetDirectoryName(destination), "*.jar").Any(path => {
                using (var zip = ZipFile.OpenRead(FileSafety.Under(root, "mods/" + Path.GetFileName(path))))
                {
                    if (zip.Entries.Count > 100000) throw new InvalidDataException("模组 ZIP 条目数超过限制");
                    var mod = new FabricModMetadataReader().Read(zip);
                    return mod != null && mod.Id == "fabric-api";
                }
            });
            if (!hasApi) FileSafety.WriteViaTemp(FileSafety.Under(root, "mods/" + Path.GetFileName(api)), ".tmp", t => File.Copy(api, t));
            string config = FileSafety.Under(root, "config/suyuncraft-ai.json");
            if (!File.Exists(config))
            {
                byte[] bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                FileSafety.WriteText(config, Json.Dump(new {
                    endpoint = "http://127.0.0.1:18765/plan", token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(),
                    allowWorldInteraction = false, cooldownSeconds = 10, dailyRequests = 30, maxOutputTokens = 384
                }));
            }
            return config;
        }
    }
}
