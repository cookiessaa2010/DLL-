using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KaiTOR.Diplomacy.Runtime;

internal static class KaiLiveTestLog
{
    private static readonly object Sync = new();
    private static readonly string SessionId = Guid.NewGuid().ToString("N");

    public static string FilePath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KaiTORDiplomacy");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "KaiTOR-LiveTest.log");
        }
    }

    public static void Reset(string reason)
    {
        try
        {
            var header =
                $"{DateTime.UtcNow:O}|LIVE_TEST_START|session={SessionId}; reason={Sanitize(reason ?? "manual")}" +
                Environment.NewLine;
            lock (Sync)
                File.WriteAllText(FilePath, header);
        }
        catch
        {
            // Live-test diagnostics must never affect gameplay.
        }
    }

    public static void Write(string source, string stage, string details = null)
    {
        try
        {
            var safeSource = string.IsNullOrWhiteSpace(source) ? "unknown" : Sanitize(source);
            var safeStage = string.IsNullOrWhiteSpace(stage) ? "UNKNOWN" : Sanitize(stage);
            var safeDetails = string.IsNullOrWhiteSpace(details) ? string.Empty : Sanitize(details);
            var line =
                $"{DateTime.UtcNow:O}|EVENT|session={SessionId}; source={safeSource}; stage={safeStage}; {safeDetails}" +
                Environment.NewLine;
            lock (Sync)
                File.AppendAllText(FilePath, line);
        }
        catch
        {
            // Live-test diagnostics must never affect gameplay.
        }
    }

    public static void WriteSnapshot(string reason, IEnumerable<string> lines)
    {
        try
        {
            var now = DateTime.UtcNow;
            var safeReason = Sanitize(reason ?? "manual");
            var payload = new List<string>
            {
                $"{now:O}|SNAPSHOT_BEGIN|session={SessionId}; reason={safeReason}"
            };

            if (lines != null)
            {
                payload.AddRange(lines
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => $"{DateTime.UtcNow:O}|SNAPSHOT|session={SessionId}; {Sanitize(line)}"));
            }

            payload.Add($"{DateTime.UtcNow:O}|SNAPSHOT_END|session={SessionId}; reason={safeReason}");

            lock (Sync)
                File.AppendAllLines(FilePath, payload);
        }
        catch
        {
            // Snapshotting must never break the campaign.
        }
    }

    private static string Sanitize(string value)
        => (value ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("|", "/");
}
