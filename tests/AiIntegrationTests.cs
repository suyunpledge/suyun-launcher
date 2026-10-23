using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
namespace SuyunCraft
{
    static class AiIntegrationTests
    {
        static void Jar(string path, string id)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("fabric.mod.json").Open()))
                w.Write("{\"schemaVersion\":1,\"id\":\"" + id + "\",\"version\":\"1\"}");
        }
        public static void Run(string root, Action<bool, string> check)
        {
            string bundle = Path.Combine(root, "bundle"), game = Path.Combine(root, "game");
            Jar(Path.Combine(bundle, "dist", "suyuncraft-ai-0.3.0.jar"), "suyuncraft_ai");
            Jar(Path.Combine(bundle, "dist", "fabric-api-0.106.0+1.21.1.jar"), "fabric-api");
            FileSafety.WriteText(FileSafety.VersionFile(game, "1.21.1", ".json"), "{\"id\":\"1.21.1\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
            FileSafety.WriteText(FileSafety.VersionFile(game, "fabric-ai", ".json"), "{\"id\":\"fabric-ai\",\"inheritsFrom\":\"1.21.1\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\"}");
            var config = new Config { GameDir = game, VersionIsolation = true };
            string path = AiIntegration.Install(config, "fabric-ai", bundle);
            string isolated = Path.GetDirectoryName(FileSafety.VersionFile(game, "fabric-ai", ".json"));
            check(path.StartsWith(isolated) && !Directory.Exists(Path.Combine(game, "mods")), "AI install respects selected version isolation");
            var json = Json.Parse(File.ReadAllText(path));
            string token = Json.S(json, "token", "");
            check(token.Length == 64 && !Json.B(json, "allowWorldInteraction", true), "AI install generates token and disables world interaction by default");
            check(Directory.GetFiles(Path.Combine(isolated, "mods"), "*.jar").Length == 2, "AI install supplies Fabric API dependency");
            string replacement = Path.Combine(isolated, "mods", "custom-api.jar");
            File.Move(Path.Combine(isolated, "mods", "fabric-api-0.106.0+1.21.1.jar"), replacement);
            AiIntegration.Install(config, "fabric-ai", bundle);
            check(Directory.GetFiles(Path.Combine(isolated, "mods"), "*.jar").Length == 2, "AI reinstall preserves existing Fabric API without duplicates");
            check(Json.S(Json.Parse(File.ReadAllText(path)), "token", "") == token, "AI reinstall preserves local token and settings");
            Jar(Path.Combine(isolated, "mods", "suyuncraft-ai-0.1.0.jar"), "suyuncraft_ai");
            AiIntegration.Install(config, "fabric-ai", bundle);
            check(!File.Exists(Path.Combine(isolated,"mods","suyuncraft-ai-0.1.0.jar")) &&
                Directory.GetFiles(Path.Combine(isolated,"suyuncraft-ai-backups"),"*.jar",SearchOption.AllDirectories).Length==1,
                "AI upgrade quarantines old version instead of duplicating mod IDs");
            bool rejected = false;
            try { AiIntegration.Install(config, "1.21.1", bundle); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "AI installer rejects a vanilla profile");
        }
    }
}
