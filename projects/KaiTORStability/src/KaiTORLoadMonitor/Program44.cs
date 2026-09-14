using System;
using System.Windows.Forms;

namespace KaiTORLoadMonitor
{
    internal static class Program44
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (HasArg(args, "--self-test"))
            {
                var ok = ShaderCacheLocator.SelfTest() &&
                         ShaderSourcePrestage.SelfTest() &&
                         FirstLaunchWarmup.SelfTest() &&
                         RglTelemetry.SelfTest();
                Environment.Exit(ok ? 0 : 3);
                return;
            }

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
                        MessageBox.Show("Папка Bannerlord не найдена.", "KaiTOR Load Monitor 0.4.4", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    gameRoot = picker.SelectedPath;
                }
            }

            Application.Run(new LiveLoadMonitorForm44(gameRoot));
        }

        private static bool HasArg(string[] args, string value)
        {
            if (args == null) return false;
            foreach (var arg in args)
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ParseGameRoot(string[] args)
        {
            if (args == null) return null;
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--game-root", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
