using System;
using TaleWorlds.Localization;

namespace GameInterface.Services.UI;

/// <summary>
/// KaiTOR-owned localization facade. English is always embedded as a fallback while
/// language XML files can override the stable kaitor_* ids.
/// </summary>
public static class KaiTORUiText
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
            {
                text.SetTextVariable(variable.Name, variable.Value);
            }
        }

        return text.ToString();
    }
}
