using System;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

internal static class TreatyKey
{
    private const char Separator = '|';

    public static string For(Kingdom first, Kingdom second)
    {
        if (first == null) throw new ArgumentNullException(nameof(first));
        if (second == null) throw new ArgumentNullException(nameof(second));

        var a = first.StringId ?? string.Empty;
        var b = second.StringId ?? string.Empty;
        return string.CompareOrdinal(a, b) <= 0
            ? a + Separator + b
            : b + Separator + a;
    }

    public static bool TrySplit(string key, out string firstId, out string secondId)
    {
        firstId = null;
        secondId = null;
        if (string.IsNullOrWhiteSpace(key)) return false;

        var index = key.IndexOf(Separator);
        if (index <= 0 || index >= key.Length - 1) return false;
        if (key.IndexOf(Separator, index + 1) >= 0) return false;

        firstId = key.Substring(0, index);
        secondId = key.Substring(index + 1);
        return firstId.Length > 0 && secondId.Length > 0;
    }
}
