using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace KaiTORLoadMonitor
{
    internal sealed class FirstLaunchWarmupResult
    {
        public int Files { get; set; }
        public long Bytes { get; set; }
        public int Errors { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    internal static class FirstLaunchWarmup
    {
        private const long MaxTotalBytes = 384L * 1024L * 1024L;
        private const long MaxFileBytes = 16L * 1024L * 1024L;
        private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".xml", ".json", ".txt", ".rs", ".rsh"
        };

        internal static FirstLaunchWarmupResult Prepare(string gameRoot)
        {
            var result = new FirstLaunchWarmupResult();
            var sw = Stopwatch.StartNew();
            try
            {
                foreach (var moduleName in new[] { "TOR_Core", "TOR_Armory", "TOR_Environment" })
                {
                    var root = Path.Combine(gameRoot ?? string.Empty, "Modules", moduleName);
                    if (!Directory.Exists(root)) continue;

                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories); }
                    catch { continue; }

                    foreach (var path in files)
                    {
                        if (result.Bytes >= MaxTotalBytes) break;
                        try
                        {
                            var ext = Path.GetExtension(path);
                            if (!Extensions.Contains(ext)) continue;
                            var info = new FileInfo(path);
                            if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes) continue;
                            if (result.Bytes + info.Length > MaxTotalBytes) continue;
                            if (WarmSequential(path))
                            {
                                result.Files++;
                                result.Bytes += info.Length;
                            }
                        }
                        catch { result.Errors++; }
                    }
                }
                return result;
            }
            finally
            {
                sw.Stop();
                result.Elapsed = sw.Elapsed;
            }
        }

        private static bool WarmSequential(string path)
        {
            var buffer = new byte[128 * 1024];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, buffer.Length, FileOptions.SequentialScan))
            {
                while (stream.Read(buffer, 0, buffer.Length) > 0) { }
            }
            return true;
        }
    }
}
