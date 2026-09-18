using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace GameInterface.Services.UI.Credits;

/// <summary>
/// Backs the credits popup: a title above named sections (sponsors, supporters, contributors,
/// community) and a close button. Section names come from <see cref="CreditsRoster"/>.
/// </summary>
public class CreditsPopupVM : ViewModel
{
    internal static string EmptySectionText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_coming_soon", "Coming soon");

    private readonly Action close;

    public CreditsPopupVM(Action close)
    {
        this.close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public string TitleText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_credits", "Credits");
    public string SponsorsHeaderText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_sponsors", "Sponsors");
    public string SupportersHeaderText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_supporters", "Supporters");
    public string ContributorsHeaderText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_contributors", "Contributors");
    public string CommunityHeaderText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_community", "Community");
    public string SponsorsText => FormatNames(CreditsRoster.Sponsors);
    public string SupportersText => FormatNames(CreditsRoster.Supporters);
    public string ContributorsText => FormatNames(CreditsRoster.Contributors);
    public string CommunityText => FormatNames(CreditsRoster.Community);
    public string CloseButtonText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_close", "Close");

    public void ActionClose()
    {
        close();
    }

    private static string FormatNames(IReadOnlyList<string> names)
    {
        return names.Count == 0 ? EmptySectionText : string.Join("\n", names);
    }
}
