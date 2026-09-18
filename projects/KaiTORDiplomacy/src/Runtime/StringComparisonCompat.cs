using System;

namespace KaiTOR.Diplomacy.Runtime;

internal static class StringComparisonCompat
{
    public static bool Contains(this string source, string value, StringComparison comparisonType)
        => source != null && value != null && source.IndexOf(value, comparisonType) >= 0;
}
