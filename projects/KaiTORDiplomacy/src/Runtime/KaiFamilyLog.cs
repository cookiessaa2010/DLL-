using System;
using System.IO;
using System.Text;

namespace KaiTOR.Diplomacy.Runtime;

internal static class KaiFamilyLog
{
    private static readonly object Sync = new();

    public static string FilePath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KaiTORDiplomacy");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "KaiTOR-Family.log");
        }
    }

    public static void Write(string stage, string details)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    FilePath,
                    $"{DateTime.UtcNow:O}|{Sanitize(stage)}|{Sanitize(details)}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never affect campaign simulation.
        }
    }

    public static void SessionStart()
        => Write("SESSION_START", $"campaignDay={TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays:0.00}");

    private static string Sanitize(string value)
        => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
}
