using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PCLDownloadCenter
{
    public static class FileUtil
    {
        public static void EnsureDir(string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);
        }
        public static string ReadAllText(string path)
        {
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8);
        }
        /// <summary>原子写：先写 .tmp 再替换，避免 PCL 读到半个文件。</summary>
        public static void WriteAllTextAtomic(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            EnsureDir(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content ?? "", new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; } catch { }
                try { File.Delete(path); } catch { }
            }
            File.Move(tmp, path);
        }
        public static void WriteBytesAtomic(string path, byte[] data)
        {
            var dir = Path.GetDirectoryName(path);
            EnsureDir(dir);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data ?? new byte[0]);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; } catch { }
                try { File.Delete(path); } catch { }
            }
            File.Move(tmp, path);
        }
        public static string Sha1OfFile(string path)
        {
            using (var sha = SHA1.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128))
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
        public static string Sha1OfBytes(byte[] data)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(data ?? new byte[0]);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
        public static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
        public static void SafeDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
        public static void CopyDir(string source, string target, bool overwrite = true)
        {
            EnsureDir(target);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(source.Length).TrimStart('\\', '/');
                var dest = Path.Combine(target, rel);
                EnsureDir(Path.GetDirectoryName(dest));
                File.Copy(file, dest, overwrite);
            }
        }
        public static long SizeOf(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : -1; } catch { return -1; }
        }
        /// <summary>把 Windows 路径统一成 / 分隔，便于比较。</summary>
        public static string Normalize(string path)
        {
            return (path ?? "").Replace('\\', '/');
        }
    }
}
