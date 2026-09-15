using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

public sealed class KaiCultureAssimilationBehavior : CampaignBehaviorBase
{
    public const int CultureChangeCost = 100000;
    public const int RequiredClanTier = 3;
    private const string TorSpecialSettlementId = "castle_BK1";

    private bool _runtimeEnabled;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Settlement culture persistence remains owned by TOR's AssimilationCampaignBehavior.
        // KaiTOR deliberately does not create a competing settlement-culture save table.
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        _runtimeEnabled = TorCompatibilityGate.TryValidate(out _);
        RegisterMenuOptions(starter);
    }

    private void RegisterMenuOptions(CampaignGameStarter starter)
    {
        starter.AddGameMenuOption(
            "town",
            "kaitor_change_town_culture",
            "KaiTOR: Convert settlement to your clan culture (100,000 denars)",
            CultureMenuCondition,
            CultureMenuConsequence,
            false,
            7);

        starter.AddGameMenuOption(
            "castle",
            "kaitor_change_castle_culture",
            "KaiTOR: Convert settlement to your clan culture (100,000 denars)",
            CultureMenuCondition,
            CultureMenuConsequence,
            false,
            7);
    }

    private bool CultureMenuCondition(MenuCallbackArgs args)
    {
        var settlement = Settlement.CurrentSettlement;
        if (!_runtimeEnabled || settlement == null || !settlement.IsFortification)
            return false;

        if (settlement.OwnerClan != Clan.PlayerClan)
            return false;

        args.optionLeaveType = GameMenuOption.LeaveType.Manage;

        if (IsTorSpecialSettlement(settlement))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("TOR marks this settlement as special and excludes it from normal cultural assimilation.");
            return true;
        }

        if (settlement.IsUnderSiege)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Settlement culture cannot be converted during a siege.");
            return true;
        }

        if (Clan.PlayerClan == null || Clan.PlayerClan.Tier < RequiredClanTier)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"Clan tier {RequiredClanTier} or higher is required to convert a settlement.");
            return true;
        }

        var targetCulture = GetPlayerClanCulture();
        if (targetCulture == null)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("KaiTOR could not determine your clan culture.");
            return true;
        }

        if (settlement.Culture == targetCulture)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("This settlement already uses your clan culture.");
            return true;
        }

        if (!TorSettlementCultureBridge.ValidateFullConversion(targetCulture, settlement, out var supportReason))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Full TOR culture conversion is not safe here: " + supportReason);
            return true;
        }

        if (Hero.MainHero == null || Hero.MainHero.Gold < CultureChangeCost)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"You need {CultureChangeCost:N0} denars.");
            return true;
        }

        args.Tooltip = new TextObject(
            $"Immediately rebuild {settlement.Name} as a {targetCulture.Name} settlement. " +
            "The town/castle, bound villages, notables, volunteer pools, tavern mercenaries, caravan guards, militia/scene population and future tavern wanderers will use your clan culture. " +
            "Existing named lords and members of other clans are not rewritten.");
        return true;
    }

    private void CultureMenuConsequence(MenuCallbackArgs args)
    {
        var settlement = Settlement.CurrentSettlement;
        var targetCulture = GetPlayerClanCulture();
        if (settlement == null || targetCulture == null) return;

        var title = "KaiTOR full settlement conversion";
        var body = $"Convert {settlement.Name} completely to {targetCulture.Name}?\n\n" +
                   $"Cost: {CultureChangeCost:N0} denars\n" +
                   $"Requirement: clan tier {RequiredClanTier}+\n\n" +
                   "This is intended to behave like the settlement had been assigned your clan's race/culture from the moment of conquest: " +
                   "bound villages and local notables are rebuilt, volunteer recruitment is refreshed, town tavern mercenaries are regenerated, " +
                   "wrong-culture tavern wanderers are replaced, and home caravans refresh their culture-specific guards/mercenaries.\n\n" +
                   "Named lords and heroes belonging to other clans are not converted. If the settlement is captured later, normal TOR assimilation may change it again.";

        InformationManager.ShowInquiry(
            new InquiryData(
                title,
                body,
                true,
                true,
                "Convert",
                "Cancel",
                () =>
                {
                    if (TryChangeCultureImmediately(settlement, out var reason))
                        InformationManager.DisplayMessage(new InformationMessage(reason));
                    else
                        InformationManager.DisplayMessage(new InformationMessage("KaiTOR culture conversion refused: " + reason));
                },
                null,
                string.Empty,
                0f,
                null,
                null,
                null),
            false,
            false);
    }

    public bool TryChangeCultureImmediately(Settlement settlement, out string reason)
    {
        reason = string.Empty;

        if (!_runtimeEnabled)
        {
            reason = "TOR compatibility gate is not active.";
            return false;
        }

        if (settlement == null || !settlement.IsFortification)
        {
            reason = "A town or castle is required.";
            return false;
        }

        if (settlement.OwnerClan != Clan.PlayerClan)
        {
            reason = "You can only convert settlements owned by your clan.";
            return false;
        }

        if (IsTorSpecialSettlement(settlement))
        {
            reason = "TOR marks this settlement as special and excludes it from normal assimilation.";
            return false;
        }

        if (settlement.IsUnderSiege)
        {
            reason = "Settlement culture cannot be converted during a siege.";
            return false;
        }

        if (Clan.PlayerClan == null || Clan.PlayerClan.Tier < RequiredClanTier)
        {
            reason = $"Clan tier {RequiredClanTier} or higher is required.";
            return false;
        }

        var targetCulture = GetPlayerClanCulture();
        if (targetCulture == null)
        {
            reason = "KaiTOR could not determine your clan culture.";
            return false;
        }

        if (settlement.Culture == targetCulture)
        {
            reason = "The settlement already has your clan culture.";
            return false;
        }

        if (Hero.MainHero == null || Hero.MainHero.Gold < CultureChangeCost)
        {
            reason = $"You need {CultureChangeCost:N0} denars.";
            return false;
        }

        // Full fail-closed preflight before any notable is removed or any money is charged.
        if (!TorSettlementCultureBridge.ValidateFullConversion(targetCulture, settlement, out reason))
            return false;

        var affected = GetAffectedSettlements(settlement).ToArray();
        foreach (var affectedSettlement in affected)
            ApplyCultureToSettlementAndNotables(affectedSettlement, targetCulture);

        if (!TorSettlementCultureBridge.RefreshAfterCultureChange(settlement, targetCulture, out reason))
        {
            reason = "Culture fields changed, but a TOR subsystem refresh failed. Save diagnostics before continuing: " + reason;
            return false;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, CultureChangeCost, false);

        reason = $"{settlement.Name} fully converted to {targetCulture.Name}. {CultureChangeCost:N0} denars paid. " +
                 "Villages, notables, volunteers, tavern mercenaries, culture-specific wanderers and caravan recruitment were refreshed.";
        return true;
    }

    public string DescribeCurrentSettlement()
    {
        var settlement = Settlement.CurrentSettlement;
        if (settlement == null) return "No settlement is currently open.";
        var clanCulture = GetPlayerClanCulture();
        return $"{settlement.StringId}: settlement culture={settlement.Culture?.StringId ?? "<null>"}, " +
               $"player clan culture={clanCulture?.StringId ?? "<null>"}, owner clan={settlement.OwnerClan?.StringId ?? "<null>"}, " +
               $"player clan tier={Clan.PlayerClan?.Tier.ToString() ?? "<null>"}.";
    }

    public IEnumerable<string> DescribeCultureSupport()
        => TorSettlementCultureBridge.DescribePlayableCultureSupport();

    private static CultureObject GetPlayerClanCulture()
        => Clan.PlayerClan?.Culture ?? Hero.MainHero?.Culture;

    private static IEnumerable<Settlement> GetAffectedSettlements(Settlement settlement)
    {
        yield return settlement;
        if (settlement.BoundVillages == null) yield break;
        foreach (var village in settlement.BoundVillages)
            if (village?.Settlement != null)
                yield return village.Settlement;
    }

    private static void ApplyCultureToSettlementAndNotables(Settlement settlement, CultureObject targetCulture)
    {
        settlement.Culture = targetCulture;

        foreach (var notable in settlement.Notables.ToList())
        {
            if (notable == null || notable.Culture == targetCulture) continue;

            var occupation = notable.Occupation;
            KillCharacterAction.ApplyByRemove(notable);
            var replacement = HeroCreator.CreateNotable(occupation, settlement);
            if (replacement != null)
                EnterSettlementAction.ApplyForCharacterOnly(replacement, settlement);
        }
    }

    private static bool IsTorSpecialSettlement(Settlement settlement)
        => string.Equals(settlement?.StringId, TorSpecialSettlementId, StringComparison.Ordinal);
}
