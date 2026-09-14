using System;
using System.Globalization;
using System.IO;

namespace KaiTORPortraitFix
{
    internal static class PortraitFixLog
    {
        private static readonly object Gate = new object();
        private static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiTORPortraitFix");
        internal static readonly string PathName = Path.Combine(Root, "KaiTORPortraitFix.log");

        internal static void Event(string name, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Root);
                    File.AppendAllText(
                        PathName,
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "|" +
                        (name ?? string.Empty) + "|" + (message ?? string.Empty) + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never block the main-menu renderer.
            }
        }
    }
}
