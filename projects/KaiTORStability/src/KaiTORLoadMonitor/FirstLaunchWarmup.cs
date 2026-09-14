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
        public int ModuleRoots { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    /// <summary>
    /// Bounded first-launch read-through for the files TOR touches early in startup.
    /// 0.4.4 resolves both local Modules and Steam Workshop module roots; this matters
    /// for standard TOR Workshop installs where gameRoot\Modules\TOR_* may not exist.
    /// It intentionally avoids large TPAC/resource archives and never keeps file data in
    /// managed memory after the sequential read completes.
    /// </summary>
    internal static class FirstLaunchWarmup
    {
        private const long MaxTotalBytes = 512L * 1024L * 1024L;
        private const long MaxFileBytes = 24L * 1024L * 1024L;

        private static readonly HashSet<string> WarmExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".xml", ".json", ".txt", ".rs", ".rsh", ".mbproj", ".xscene"
        };

        private static readonly string[] TorIds =
        {
            "TOR_Core", "TOR_Armory", "TOR_Environment"
        };

        internal static FirstLaunchWarmupResult Prepare(string gameRoot)
        {
            var result = new FirstLaunchWarmupResult();
            var sw = Stopwatch.StartNew();
            try
            {
                var roots = ResolveTorModuleRoots(gameRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                result.ModuleRoots = roots.Length;

                foreach (var root in roots)
                {
                    if (result.Bytes >= MaxTotalBytes) break;
                    foreach (var path in EnumeratePriorityFiles(root))
                    {
                        if (result.Bytes >= MaxTotalBytes) break;
                        try
                        {
                            var ext = Path.GetExtension(path);
                            if (!WarmExtensions.Contains(ext)) continue;

                            var info = new FileInfo(path);
                            if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes) continue;
                            if (result.Bytes + info.Length > MaxTotalBytes) continue;

                            if (WarmSequential(path))
                            {
                                result.Files++;
                                result.Bytes += info.Length;
                            }
                        }
                        catch
                        {
                            result.Errors++;
                        }
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

        internal static bool SelfTest()
        {
            var temp = Path.Combine(Path.GetTempPath(), "KaiTORWarmup_" + Guid.NewGuid().ToString("N"));
            try
            {
                var root = Path.Combine(temp, "Modules", "TOR_Core");
                Directory.CreateDirectory(Path.Combine(root, "ModuleData"));
                Directory.CreateDirectory(Path.Combine(root, "bin", "Win64_Shipping_Client"));
                File.WriteAllText(Path.Combine(root, "SubModule.xml"), "<Module><Id value=\"TOR_Core\" /></Module>");
                File.WriteAllText(Path.Combine(root, "ModuleData", "test.xml"), "<x />");
                File.WriteAllBytes(Path.Combine(root, "bin", "Win64_Shipping_Client", "test.dll"), new byte[4096]);

                var result = Prepare(temp);
                return result.ModuleRoots == 1 && result.Files >= 2 && result.Bytes > 0 && result.Errors == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        private static IEnumerable<string> ResolveTorModuleRoots(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var modulesRoot = Path.Combine(gameRoot, "Modules");
            foreach (var root in FindTorRootsInContainer(modulesRoot))
            {
                if (seen.Add(root)) yield return root;
            }

            string workshopRoot = null;
            try
            {
                var gameDir = new DirectoryInfo(gameRoot);
                var commonDir = gameDir.Parent;
                var steamAppsDir = commonDir != null ? commonDir.Parent : null;
                if (steamAppsDir != null)
                    workshopRoot = Path.Combine(steamAppsDir.FullName, "workshop", "content", "261550");
            }
            catch { }

            foreach (var root in FindTorRootsInContainer(workshopRoot))
            {
                if (seen.Add(root)) yield return root;
            }
        }

        private static IEnumerable<string> FindTorRootsInContainer(string container)
        {
            if (string.IsNullOrWhiteSpace(container) || !Directory.Exists(container)) yield break;

            string[] dirs;
            try { dirs = Directory.GetDirectories(container); }
            catch { yield break; }

            foreach (var dir in dirs)
            {
                if (LooksLikeTorModule(dir)) yield return dir;
            }
        }

        private static bool LooksLikeTorModule(string moduleDir)
        {
            try
            {
                var name = new DirectoryInfo(moduleDir).Name;
                if (TorIds.Any(id => string.Equals(id, name, StringComparison.OrdinalIgnoreCase))) return true;

                var xmlPath = Path.Combine(moduleDir, "SubModule.xml");
                if (!File.Exists(xmlPath)) return false;
                var xml = File.ReadAllText(xmlPath);
                return TorIds.Any(id =>
                    xml.IndexOf("value=\"" + id + "\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    xml.IndexOf("value='" + id + "'", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> EnumeratePriorityFiles(string root)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var direct in new[]
            {
                Path.Combine(root, "SubModule.xml"),
                Path.Combine(root, "ModuleData", "project.mbproj"),
                Path.Combine(root, "Shaders", "Sources", "pbr_metallic_gbuffer.rs"),
                Path.Combine(root, "Shaders", "Sources", "pbr_metallic_shadowmap.rs"),
                Path.Combine(root, "SceneObj", "TOR_menuscene_01", "scene.xscene"),
                Path.Combine(root, "SceneObj", "TOR_menuscene_01", "atmosphere.xml")
            })
            {
                if (File.Exists(direct) && seen.Add(direct)) yield return direct;
            }

            foreach (var folder in new[]
            {
                Path.Combine(root, "bin", "Win64_Shipping_Client"),
                Path.Combine(root, "Shaders", "Sources"),
                Path.Combine(root, "ModuleData")
            })
            {
                if (!Directory.Exists(folder)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (var file in files)
                {
                    if (seen.Add(file)) yield return file;
                }
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
