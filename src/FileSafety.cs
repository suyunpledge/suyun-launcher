using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SuyunCraft
{
    static class FileSafety
    {
        // Per-collection-pass verification state: avoids re-hashing unchanged files and
        // re-walking ancestor directories. Never persisted, never shared across launches.
        public sealed class VerifyCache
        {
            internal readonly Dictionary<string, bool> MatchResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> SafeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public static string Id(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".." ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.EndsWith(".") ||
                value.EndsWith(" ") || Regex.IsMatch(value, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("无效的版本或文件标识: " + value);
            return value;
        }

        public static string Under(string root, string relative, VerifyCache cache = null)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new InvalidDataException("拒绝不安全路径: " + relative);
            string[] parts = relative.Replace('\\', '/').Split('/');
            foreach (string part in parts)
            {
                if (part.Length == 0) continue;
                Id(part);
            }
            string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("路径超出目标目录: " + relative);
            // Junctions and symbolic links must not redirect a metadata-controlled path outside the root.
            string cursor = target;
            while (cursor != null && !cursor.Equals(basePath, StringComparison.OrdinalIgnoreCase))
            {
                if (cache != null && cache.SafeDirs.Contains(cursor)) break;
                if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                    (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("拒绝通过链接写入: " + cursor);
                if (cache != null) cache.SafeDirs.Add(cursor);
                cursor = Path.GetDirectoryName(cursor);
            }
            return target;
        }

        public static string VersionFile(string gameDir, string id, string extension)
        {
            Id(id);
            return Under(gameDir, "versions/" + id + "/" + id + extension);
        }

        public static bool IsHash(string hash, int length)
        {
            return hash != null && Regex.IsMatch(hash, "\\A[0-9a-fA-F]{" + length + "}\\z");
        }

        public static string Hash(string path, bool sha256)
        {
            using (HashAlgorithm algorithm = sha256 ? (HashAlgorithm)SHA256.Create() : SHA1.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static bool Matches(string path, string sha1, long size, VerifyCache cache = null)
        {
            if (cache == null) return MatchesCore(path, sha1, size);
            var info = new FileInfo(path);
            string key = Path.GetFullPath(path) + "|" + sha1 + "|" + size + "|" +
                (info.Exists ? info.Length : -1) + "|" + (info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
            bool cached;
            if (cache.MatchResults.TryGetValue(key, out cached)) return cached;
            // 持久层：上次完整校验后文件未变 → 免哈希
            if (VerifyStore.Lookup(path, sha1, size))
            {
                cache.MatchResults[key] = true;
                return true;
            }
            bool result = MatchesCore(path, sha1, size);
            cache.MatchResults[key] = result;
            if (result) VerifyStore.Record(path, sha1, size);
            return result;
        }

        static bool MatchesCore(string path, string sha1, long size)
        {
            if (!File.Exists(path)) return false;
            long actual = new FileInfo(path).Length;
            if (actual == 0 || (size > 0 && actual != size)) return false;
            return string.IsNullOrEmpty(sha1) ||
                (IsHash(sha1, 40) && string.Equals(Hash(path, false), sha1, StringComparison.OrdinalIgnoreCase));
        }

        public static void Commit(string temp, string destination)
        {
            if (File.Exists(destination)) File.Replace(temp, destination, null);
            else File.Move(temp, destination);
        }

        public static void WriteViaTemp(string destination, string extension, Action<string> writeTemp)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
            string temp = destination + "." + Guid.NewGuid().ToString("N") + extension;
            try { writeTemp(temp); Commit(temp, destination); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static void WriteText(string destination, string content)
        {
            WriteViaTemp(destination, ".tmp", temp => File.WriteAllText(temp, content, new UTF8Encoding(false)));
        }

        public static void ExtractZip(string archive, string destination)
        {
            Directory.CreateDirectory(destination);
            using (var zip = ZipFile.OpenRead(archive))
            {
                long expanded = 0;
                foreach (var entry in zip.Entries)
                {
                    string target = Under(destination, entry.FullName);
                    expanded = checked(expanded + entry.Length);
                    if (expanded > 2L * 1024 * 1024 * 1024 || zip.Entries.Count > 100000)
                        throw new InvalidDataException("压缩包展开大小超过限制");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, false);
                }
            }
        }
    }
}
