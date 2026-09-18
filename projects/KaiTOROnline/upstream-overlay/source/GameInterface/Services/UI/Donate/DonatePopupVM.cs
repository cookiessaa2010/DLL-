using System;
using TaleWorlds.Library;

namespace GameInterface.Services.UI.Donate;

/// <summary>
/// Backs the donation popup: a prompt above a vertical list of platform buttons and a close button.
/// </summary>
public class DonatePopupVM : ViewModel
{
    private readonly Action close;

    public DonatePopupVM(Action close)
    {
        this.close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public string PromptText => global::GameInterface.Services.UI.KaiTORUiText.Get(
        "kaitor_donate_prompt",
        "Bannerlord Coop upstream is free and developed by volunteers.\n\nDonations help its maintainers cover servers, development tools, and other project costs.\n\nIf you want to support the upstream project, choose a platform below.");
    public string PayPalButtonText => "PayPal";
    public string BuyMeACoffeeButtonText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_buy_coffee", "Buy a Coffee");
    public string AfdianButtonText => "Afdian";
    public string BoostyButtonText => "Boosty";
    public string CloseButtonText => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_close", "Close");

    public void ActionPayPal()
    {
        System.Diagnostics.Process.Start("https://www.paypal.com/donate/?hosted_button_id=KHBSK4FXQ9GKS");
    }

    public void ActionBuyMeACoffee()
    {
        System.Diagnostics.Process.Start("https://buymeacoffee.com/bannerlordcoop");
    }

    public void ActionAfdian()
    {
        System.Diagnostics.Process.Start("https://ifdian.net/a/BannerlordCoop");
    }

    public void ActionBoosty()
    {
        System.Diagnostics.Process.Start("https://boosty.to/bannerlordcoop/donate");
    }

    public void ActionClose()
    {
        close();
    }
}
