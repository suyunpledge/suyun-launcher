using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SuyunCraft
{
    static class FileSafety
    {
        // Per-collection-pass verification state: avoids re-hashing unchanged files and
        // re-walking ancestor directories. Never persisted, never shared across launches.
        public sealed class VerifyCache
        {
            public CancellationToken Cancellation;
            internal readonly Dictionary<string, bool> MatchResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> SafeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public static string Id(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".." ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.EndsWith(".") ||
                value.EndsWith(" ") || Regex.IsMatch(value, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Invalid version or file id: " + value);
            return value;
        }

        public static string Under(string root, string relative, VerifyCache cache = null)
        {
            if (cache != null) cache.Cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new InvalidDataException("Rejecting unsafe path: " + relative);
            string[] parts = relative.Replace('\\', '/').Split('/');
            foreach (string part in parts)
            {
                if (part.Length == 0) continue;
                Id(part);
            }
            string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Path escapes the target directory: " + relative);
            // Junctions and symbolic links must not redirect a metadata-controlled path outside the root.
            string cursor = target;
            while (cursor != null && !cursor.Equals(basePath, StringComparison.OrdinalIgnoreCase))
            {
                if (cache != null && cache.SafeDirs.Contains(cursor)) break;
                if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                    (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Rejecting writes through links: " + cursor);
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

        public static string Hash(string path, bool sha256, CancellationToken cancellation = default(CancellationToken))
        {
            using (HashAlgorithm algorithm = sha256 ? (HashAlgorithm)SHA256.Create() : SHA1.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
            {
                byte[] buffer = new byte[65536];
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    algorithm.TransformBlock(buffer, 0, read, buffer, 0);
                }
                cancellation.ThrowIfCancellationRequested();
                algorithm.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(algorithm.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static bool Matches(string path, string sha1, long size, VerifyCache cache = null)
        {
            if (cache == null) return MatchesCore(path, sha1, size);
            cache.Cancellation.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            string key = Path.GetFullPath(path) + "|" + sha1 + "|" + size + "|" +
                (info.Exists ? info.Length : -1) + "|" + (info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
            bool cached;
            if (cache.MatchResults.TryGetValue(key, out cached)) return cached;
            // Persistent layer: file unchanged since last full verify → skips hashing
            if (VerifyStore.Lookup(path, sha1, size))
            {
                cache.MatchResults[key] = true;
                return true;
            }
            bool result = MatchesCore(path, sha1, size, cache.Cancellation);
            cache.MatchResults[key] = result;
            if (result) VerifyStore.Record(path, sha1, size);
            return result;
        }

        static bool MatchesCore(string path, string sha1, long size, CancellationToken cancellation = default(CancellationToken))
        {
            if (!File.Exists(path)) return false;
            long actual = new FileInfo(path).Length;
            if (actual == 0 || (size > 0 && actual != size)) return false;
            return string.IsNullOrEmpty(sha1) ||
                (IsHash(sha1, 40) && string.Equals(Hash(path, false, cancellation), sha1, StringComparison.OrdinalIgnoreCase));
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
                        throw new InvalidDataException("Archive expansion exceeds the limit");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, false);
                }
            }
        }
    }
}
