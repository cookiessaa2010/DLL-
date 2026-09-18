using Common.Messaging;
using GameInterface.Services.UI;
using GameInterface.Services.UI.CoopOptions;
using GameInterface.Services.UI.CoopOptions.Providers;
using GameInterface.Services.UI.CoopOptions.Providers.KillFeedTab;
using GameInterface.Services.UI.Messages;
using TaleWorlds.Library;

namespace GameInterface.Services.UI.CoopOptions.Providers.KillFeedTab.Sections;

public class KillFeedSection : CoopOptionsSectionVM
{
    public const string SectionId = "KillFeedSection";

    private readonly IMessageBroker messageBroker;

    private PlayerKillFeedColor killFeedColor;

    public KillFeedSection(PlayerKillFeedColor killFeedColor, IMessageBroker messageBroker)
    {
        this.killFeedColor = killFeedColor;
        this.messageBroker = messageBroker;
    }

    public override string Id => SectionId;

    public string TitleText => GameInterface.Services.UI.KaiTORUiText.Get("kaitor_killfeed_color", KillFeedOptionsTabProvider.SectionTitleText);
    public string DescriptionText => GameInterface.Services.UI.KaiTORUiText.Get("kaitor_killfeed_desc", KillFeedOptionsTabProvider.SectionDescriptionText);
    public string KillFeedColorRedText => GameInterface.Services.UI.KaiTORUiText.Get("kaitor_red", KillFeedOptionsTabProvider.KillFeedColorRedText);
    public string KillFeedColorGreenText => GameInterface.Services.UI.KaiTORUiText.Get("kaitor_green", KillFeedOptionsTabProvider.KillFeedColorGreenText);
    public string KillFeedColorBlueText => GameInterface.Services.UI.KaiTORUiText.Get("kaitor_blue", KillFeedOptionsTabProvider.KillFeedColorBlueText);

    [DataSourceProperty]
    public int KillFeedColorRed
    {
        get => killFeedColor.Red;
        set => ApplyKillFeedColor(PlayerKillFeedColor.Clamp(value, killFeedColor.Green, killFeedColor.Blue));
    }

    [DataSourceProperty]
    public int KillFeedColorGreen
    {
        get => killFeedColor.Green;
        set => ApplyKillFeedColor(PlayerKillFeedColor.Clamp(killFeedColor.Red, value, killFeedColor.Blue));
    }

    [DataSourceProperty]
    public int KillFeedColorBlue
    {
        get => killFeedColor.Blue;
        set => ApplyKillFeedColor(PlayerKillFeedColor.Clamp(killFeedColor.Red, killFeedColor.Green, value));
    }

    [DataSourceProperty]
    public string KillFeedPreviewColorString => killFeedColor.ToColorString();

    public override void Apply(string tabId, CoopOptionsData options)
    {
        options.SetSection(tabId, Id, KillFeedSectionOptions.FromColor(killFeedColor));
    }

    public override void AfterApply()
    {
        messageBroker.Publish(this, new PlayerKillFeedColorSelected(killFeedColor));
    }

    private void ApplyKillFeedColor(PlayerKillFeedColor color)
    {
        if (killFeedColor.Equals(color)) return;

        killFeedColor = color;
        OnPropertyChanged(nameof(KillFeedColorRed));
        OnPropertyChanged(nameof(KillFeedColorGreen));
        OnPropertyChanged(nameof(KillFeedColorBlue));
        OnPropertyChanged(nameof(KillFeedPreviewColorString));
    }
}
