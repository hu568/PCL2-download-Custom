using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PCLDownloadCenter
{
    public static class ZipUtil
    {
        public static ZipArchive Open(string path)
        {
            return ZipFile.OpenRead(path);
        }
        public static List<string> ListEntries(string path)
        {
            var list = new List<string>();
            using (var zip = ZipFile.OpenRead(path))
                foreach (var e in zip.Entries) list.Add(e.FullName);
            return list;
        }
        public static string ReadEntryText(string path, string entryName)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var entry = zip.GetEntry(entryName);
                if (entry == null) return null;
                using (var stream = entry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }
        public static string ReadEntryText(ZipArchive zip, string entryName)
        {
            var entry = zip.GetEntry(entryName);
            if (entry == null) return null;
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
        /// <summary>安全解压（阻止 ../ 路径穿越）。</summary>
        public static void ExtractTo(string zipPath, string targetDir, string entryPrefixToStrip = null)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
                ExtractTo(zip, targetDir, entryPrefixToStrip);
        }
        public static void ExtractTo(ZipArchive zip, string targetDir, string entryPrefixToStrip = null)
        {
            FileUtil.EnsureDir(targetDir);
            var rootFull = Path.GetFullPath(targetDir);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;   // 目录
                var name = entry.FullName.Replace('\\', '/');
                if (!string.IsNullOrEmpty(entryPrefixToStrip))
                {
                    if (!name.StartsWith(entryPrefixToStrip, StringComparison.OrdinalIgnoreCase)) continue;
                    name = name.Substring(entryPrefixToStrip.Length).TrimStart('/');
                }
                if (name.Length == 0) continue;
                var dest = Path.GetFullPath(Path.Combine(targetDir, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("压缩包内路径越界：" + entry.FullName);
                FileUtil.EnsureDir(Path.GetDirectoryName(dest));
                using (var src = entry.Open())
                using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                    src.CopyTo(dst);
            }
        }
        /// <summary>解压指定前缀的条目并去掉前缀（整合包 overrides）。</summary>
        public static int ExtractPrefix(string zipPath, string prefix, string targetDir)
        {
            int count = 0;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var rel = name.Substring(prefix.Length).TrimStart('/');
                    if (rel.Length == 0) continue;
                    var dest = Path.GetFullPath(Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar)));
                    var rootFull = Path.GetFullPath(targetDir);
                    if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("整合包 overrides 路径越界：" + entry.FullName);
                    FileUtil.EnsureDir(Path.GetDirectoryName(dest));
                    using (var src = entry.Open())
                    using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                        src.CopyTo(dst);
                    count++;
                }
            }
            return count;
        }
        public static bool LooksLikeZip(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                    return fs.Length > 4 && fs.ReadByte() == 0x50 && fs.ReadByte() == 0x4B;
            }
            catch { return false; }
        }
    }
}
