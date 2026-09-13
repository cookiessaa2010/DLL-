using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KaiTORLoadMonitor
{
    internal sealed class ShaderSourcePrestageProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public string FileName { get; set; }
    }

    internal sealed class ShaderSourcePrestageResult
    {
        public bool SourceFound { get; set; }
        public bool TargetReady { get; set; }
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public int TotalFiles { get; set; }
        public int CopiedFiles { get; set; }
        public int SkippedFiles { get; set; }
        public int WarmedFiles { get; set; }
        public int Errors { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    /// <summary>
    /// Mirrors TOR's shader-source copy rule before Bannerlord starts and then reads the
    /// resulting target files once with SequentialScan. When successful, TOR's later
    /// synchronous CopyShaderSourcesToGame pass should have no files left to copy, and
    /// Windows already has the small .rs/.rsh source set warm in the filesystem cache.
    /// This never touches the compiled shader cache, so it is compatible with both the
    /// standard ProgramData cache and Kai Shader Cache Redirector.
    /// </summary>
    internal static class ShaderSourcePrestage
    {
        public static ShaderSourcePrestageResult Prepare(
            string gameRoot,
            Action<ShaderSourcePrestageProgress> progress)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ShaderSourcePrestageResult();

            try
            {
                result.SourcePath = FindTorArmoryShaderSource(gameRoot);
                result.TargetPath = Path.Combine(gameRoot, "Shaders", "Sources");
                result.SourceFound = !string.IsNullOrWhiteSpace(result.SourcePath) && Directory.Exists(result.SourcePath);

                if (!result.SourceFound)
                {
                    return result;
                }

                try
                {
                    Directory.CreateDirectory(result.TargetPath);
                    result.TargetReady = Directory.Exists(result.TargetPath);
                }
                catch
                {
                    result.TargetReady = false;
                }

                if (!result.TargetReady)
                {
                    return result;
                }

                var files = Directory.GetFiles(result.SourcePath, "*.rs", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(result.SourcePath, "*.rsh", SearchOption.TopDirectoryOnly))
                    .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                result.TotalFiles = files.Length;
                if (files.Length == 0)
                {
                    return result;
                }

                var copied = 0;
                var skipped = 0;
                var warmed = 0;
                var errors = 0;
                var completed = 0;

                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount))
                };

                Parallel.ForEach(files, options, sourceFile =>
                {
                    var targetFile = Path.Combine(result.TargetPath, Path.GetFileName(sourceFile));
                    try
                    {
                        var needsCopy = NeedsCopy(sourceFile, targetFile);
                        if (needsCopy)
                        {
                            File.Copy(sourceFile, targetFile, true);
                            Interlocked.Increment(ref copied);
                        }
                        else
                        {
                            Interlocked.Increment(ref skipped);
                        }

                        if (WarmSequential(targetFile))
                        {
                            Interlocked.Increment(ref warmed);
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref errors);
                    }
                    finally
                    {
                        var done = Interlocked.Increment(ref completed);
                        if (progress != null)
                        {
                            try
                            {
                                progress(new ShaderSourcePrestageProgress
                                {
                                    Completed = done,
                                    Total = files.Length,
                                    FileName = Path.GetFileName(sourceFile)
                                });
                            }
                            catch
                            {
                                // UI progress is best effort and must never stop preparation.
                            }
                        }
                    }
                });

                result.CopiedFiles = copied;
                result.SkippedFiles = skipped;
                result.WarmedFiles = warmed;
                result.Errors = errors;
                return result;
            }
            finally
            {
                stopwatch.Stop();
                result.Elapsed = stopwatch.Elapsed;
            }
        }

        internal static bool SelfTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "KaiTORPrestage_" + Guid.NewGuid().ToString("N"));
            try
            {
                var source = Path.Combine(root, "Modules", "TOR_Armory", "Shaders", "Sources");
                var target = Path.Combine(root, "Shaders", "Sources");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(target);

                File.WriteAllText(Path.Combine(source, "kaitor_test_a.rs"), "shader-a");
                File.WriteAllText(Path.Combine(source, "kaitor_test_b.rsh"), "shader-b");

                var first = Prepare(root, null);
                if (!first.SourceFound || !first.TargetReady || first.TotalFiles != 2 || first.CopiedFiles != 2 || first.Errors != 0)
                {
                    return false;
                }

                if (File.ReadAllText(Path.Combine(target, "kaitor_test_a.rs")) != "shader-a" ||
                    File.ReadAllText(Path.Combine(target, "kaitor_test_b.rsh")) != "shader-b")
                {
                    return false;
                }

                var second = Prepare(root, null);
                return second.TotalFiles == 2 && second.CopiedFiles == 0 && second.SkippedFiles == 2 && second.Errors == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
                catch { }
            }
        }

        private static bool NeedsCopy(string sourceFile, string targetFile)
        {
            if (!File.Exists(targetFile)) return true;

            var sourceInfo = new FileInfo(sourceFile);
            var targetInfo = new FileInfo(targetFile);
            return sourceInfo.Length != targetInfo.Length ||
                   sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc;
        }

        private static bool WarmSequential(string path)
        {
            if (!File.Exists(path)) return false;

            var buffer = new byte[64 * 1024];
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                buffer.Length,
                FileOptions.SequentialScan))
            {
                while (stream.Read(buffer, 0, buffer.Length) > 0)
                {
                    // Intentional read-through to warm the target file in Windows cache.
                }
            }
            return true;
        }

        private static string FindTorArmoryShaderSource(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) return null;

            var direct = Path.Combine(gameRoot, "Modules", "TOR_Armory", "Shaders", "Sources");
            if (Directory.Exists(direct)) return direct;

            var modulesRoot = Path.Combine(gameRoot, "Modules");
            var found = FindInModuleContainer(modulesRoot);
            if (found != null) return found;

            try
            {
                var gameDir = new DirectoryInfo(gameRoot);
                var commonDir = gameDir.Parent;
                var steamAppsDir = commonDir != null ? commonDir.Parent : null;
                if (steamAppsDir != null)
                {
                    var workshopRoot = Path.Combine(steamAppsDir.FullName, "workshop", "content", "261550");
                    found = FindInModuleContainer(workshopRoot);
                    if (found != null) return found;
                }
            }
            catch
            {
                // Non-Steam installs simply skip workshop discovery.
            }

            return null;
        }

        private static string FindInModuleContainer(string container)
        {
            if (string.IsNullOrWhiteSpace(container) || !Directory.Exists(container)) return null;

            try
            {
                foreach (var moduleDir in Directory.GetDirectories(container))
                {
                    var source = Path.Combine(moduleDir, "Shaders", "Sources");
                    if (!Directory.Exists(source)) continue;
                    if (LooksLikeTorArmory(moduleDir)) return source;
                }
            }
            catch { }
            return null;
        }

        private static bool LooksLikeTorArmory(string moduleDir)
        {
            try
            {
                if (string.Equals(new DirectoryInfo(moduleDir).Name, "TOR_Armory", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var xml = Path.Combine(moduleDir, "SubModule.xml");
                if (!File.Exists(xml)) return false;
                var text = File.ReadAllText(xml);
                return text.IndexOf("TOR_Armory", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
