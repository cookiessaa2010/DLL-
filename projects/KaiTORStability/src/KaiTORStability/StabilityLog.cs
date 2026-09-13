using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace KaiTORStability
{
    internal static class StabilityLog
    {
        private static readonly object Sync = new object();
        private static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiTORStability");
        private static readonly string PathValue = Path.Combine(Root, "KaiTORStability.log");

        public static string LogPath => PathValue;

        public static void StartSession()
        {
            Event("SESSION_START", "pid=" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
        }

        public static void Event(string eventName, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Root);
                    File.AppendAllText(
                        PathValue,
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "|" + eventName + "|" + (message ?? string.Empty) + Environment.NewLine);
                }
            }
            catch
            {
                // Diagnostics must never make Bannerlord less stable.
            }
        }
    }
}
