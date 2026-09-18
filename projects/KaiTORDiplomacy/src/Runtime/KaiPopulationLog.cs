using System;
using System.IO;

namespace KaiTOR.Diplomacy.Runtime;

internal static class KaiPopulationLog
{
    private static readonly object Sync = new();

    public static void Write(string channel, string stage, string details = null)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KaiTORDiplomacy");
            Directory.CreateDirectory(directory);

            var safeChannel = string.Equals(channel, "vampire", StringComparison.OrdinalIgnoreCase)
                ? "KaiTORVampirePopulation.log"
                : "KaiTORGreenskinPopulation.log";
            var path = Path.Combine(directory, safeChannel);
            var safeStage = string.IsNullOrWhiteSpace(stage) ? "UNKNOWN" : Sanitize(stage);
            var safeDetails = string.IsNullOrWhiteSpace(details) ? string.Empty : Sanitize(details);
            var line = $"{DateTime.UtcNow:O}|{safeStage}|{safeDetails}{Environment.NewLine}";
            lock (Sync)
                File.AppendAllText(path, line);

            KaiLiveTestLog.Write("population:" + safeChannel, safeStage, safeDetails);
        }
        catch
        {
            // Population diagnostics must never affect the campaign.
        }
    }

    private static string Sanitize(string value)
        => value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
}
