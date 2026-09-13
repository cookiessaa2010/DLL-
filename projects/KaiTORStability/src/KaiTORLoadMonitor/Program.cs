using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KaiTORLoadMonitor
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var requested = ParseGameRoot(args);
            var gameRoot = GameLocator.Resolve(requested);
            if (gameRoot == null)
            {
                using (var picker = new FolderBrowserDialog())
                {
                    picker.Description = "Выберите папку Mount & Blade II Bannerlord";
                    if (picker.ShowDialog() != DialogResult.OK || !GameLocator.IsBannerlordRoot(picker.SelectedPath))
                    {
                        MessageBox.Show("Папка Bannerlord не найдена.", "KaiTOR Load Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    gameRoot = picker.SelectedPath;
                }
            }

            Application.Run(new LoadMonitorForm(gameRoot));
        }

        private static string ParseGameRoot(string[] args)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--game-root", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }

    internal static class GameLocator
    {
        private const string GameFolderName = "Mount & Blade II Bannerlord";

        public static string Resolve(string requested)
        {
            if (IsBannerlordRoot(requested)) return Path.GetFullPath(requested);

            var env = Environment.GetEnvironmentVariable("KAITOR_BANNERLORD_ROOT");
            if (IsBannerlordRoot(env)) return Path.GetFullPath(env);

            foreach (var library in GetSteamLibraries())
            {
                var candidate = Path.Combine(library, "steamapps", "common", GameFolderName);
                if (IsBannerlordRoot(candidate)) return candidate;
            }

            var defaultSteam = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", GameFolderName);
            return IsBannerlordRoot(defaultSteam) ? defaultSteam : null;
        }

        public static bool IsBannerlordRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return File.Exists(Path.Combine(path, "bin", "Win64_Shipping_Client", "TaleWorlds.MountAndBlade.Launcher.exe"));
        }

        private static IEnumerable<string> GetSteamLibraries()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string steamPath = null;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    steamPath = key?.GetValue("SteamPath") as string;
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(steamPath) && Directory.Exists(steamPath) && seen.Add(steamPath))
            {
                yield return steamPath;
            }

            if (string.IsNullOrWhiteSpace(steamPath)) yield break;
            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;

            Regex regex = new Regex("\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            foreach (var line in File.ReadLines(vdf))
            {
                var match = regex.Match(line);
                if (!match.Success) continue;
                var path = match.Groups["path"].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path) && seen.Add(path)) yield return path;
            }
        }
    }
}
