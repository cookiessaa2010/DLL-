using System;
using System.IO;

namespace KaiTOR.Diplomacy.Runtime;

internal static class KaiRuntimeLog
{
    private static readonly object Sync = new();

    public static void Write(string stage, string details = null)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KaiTORDiplomacy");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "KaiTOR.log");
            var safeStage = string.IsNullOrWhiteSpace(stage) ? "UNKNOWN" : Sanitize(stage);
            var safeDetails = string.IsNullOrWhiteSpace(details) ? string.Empty : Sanitize(details);
            var line = $"{DateTime.UtcNow:O}|{safeStage}|{safeDetails}{Environment.NewLine}";

            lock (Sync)
                File.AppendAllText(path, line);
        }
        catch
        {
            // Diagnostics must never be able to break campaign simulation.
        }
    }

    public static void Exception(string stage, Exception ex, string details = null)
    {
        var message = details ?? string.Empty;
        if (ex != null)
        {
            if (!string.IsNullOrWhiteSpace(message))
                message += "; ";
            message += $"type={ex.GetType().FullName}; message={ex.Message}";
        }
        Write(stage, message);
    }

    private static string Sanitize(string value)
        => value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
}
