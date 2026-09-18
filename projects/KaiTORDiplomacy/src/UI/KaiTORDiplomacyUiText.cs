using System;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.UI;

internal static class KaiTORDiplomacyUiText
{
    public static string Get(string id, string englishFallback)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Localization id is required.", nameof(id));

        return new TextObject($"{{={id}}}{englishFallback ?? string.Empty}").ToString();
    }

    public static TextObject Object(string id, string englishFallback)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Localization id is required.", nameof(id));

        return new TextObject($"{{={id}}}{englishFallback ?? string.Empty}");
    }

    public static string Format(string id, string englishFallback, params (string Name, object Value)[] variables)
    {
        var text = Object(id, englishFallback);
        if (variables != null)
        {
            foreach (var variable in variables)
                text.SetTextVariable(variable.Name, new TextObject(variable.Value?.ToString() ?? string.Empty));
        }

        return text.ToString();
    }
}
