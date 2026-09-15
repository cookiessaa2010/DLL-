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
        // Intentional: culture is persisted by TOR's own AssimilationCampaignBehavior.
        // KaiTOR does not maintain a competing settlement-culture save map.
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
            "KaiTOR: Change settlement culture to your clan (100,000 denars)",
            CultureMenuCondition,
            CultureMenuConsequence,
            false,
            7);

        starter.AddGameMenuOption(
            "castle",
            "kaitor_change_castle_culture",
            "KaiTOR: Change settlement culture to your clan (100,000 denars)",
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

        if (Clan.PlayerClan == null || Clan.PlayerClan.Tier < RequiredClanTier)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"Clan tier {RequiredClanTier} or higher is required to change settlement culture.");
            return true;
        }

        var targetCulture = GetPlayerClanCulture();
        if (!ValidateTargetCulture(targetCulture, settlement, out var cultureReason))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject(cultureReason);
            return true;
        }

        if (settlement.Culture == targetCulture)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("This settlement already uses your clan culture.");
            return true;
        }

        if (Hero.MainHero == null || Hero.MainHero.Gold < CultureChangeCost)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"You need {CultureChangeCost:N0} denars.");
            return true;
        }

        args.Tooltip = new TextObject(
            $"Immediately change {settlement.Name} and its bound villages to {targetCulture.Name}. " +
            "Local notables are rebuilt for that culture so TOR recruitment uses the correct troop pools. " +
            "Lords, companions, clan members, hero race and existing garrison troops are not changed.");
        return true;
    }

    private void CultureMenuConsequence(MenuCallbackArgs args)
    {
        var settlement = Settlement.CurrentSettlement;
        var targetCulture = GetPlayerClanCulture();
        if (settlement == null || targetCulture == null) return;

        var title = "KaiTOR settlement culture";
        var body = $"Immediately change {settlement.Name} to {targetCulture.Name}?\n\n" +
                   $"Cost: {CultureChangeCost:N0} denars\n" +
                   $"Requirement: clan tier {RequiredClanTier}+\n\n" +
                   "This changes the town/castle, its bound villages and their local notable/recruitment population. " +
                   "It does NOT change your lords, companions, clan members, hero race or existing armies. " +
                   "If the settlement is captured later, TOR can assimilate it again to the new owner's faction culture.";

        InformationManager.ShowInquiry(
            new InquiryData(
                title,
                body,
                true,
                true,
                "Change culture",
                "Cancel",
                () =>
                {
                    if (TryChangeCultureImmediately(settlement, out var reason))
                        InformationManager.DisplayMessage(new InformationMessage(reason));
                    else
                        InformationManager.DisplayMessage(new InformationMessage("KaiTOR culture change refused: " + reason));
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
            reason = "You can only change culture in settlements owned by your clan.";
            return false;
        }

        if (IsTorSpecialSettlement(settlement))
        {
            reason = "TOR marks this settlement as special and excludes it from normal assimilation.";
            return false;
        }

        if (Clan.PlayerClan == null || Clan.PlayerClan.Tier < RequiredClanTier)
        {
            reason = $"Clan tier {RequiredClanTier} or higher is required.";
            return false;
        }

        var targetCulture = GetPlayerClanCulture();
        if (!ValidateTargetCulture(targetCulture, settlement, out reason))
            return false;

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

        // Validate every affected settlement before changing any state or charging gold.
        var affected = GetAffectedSettlements(settlement).ToArray();
        foreach (var affectedSettlement in affected)
        {
            if (!ValidateNotableTemplates(targetCulture, affectedSettlement, out reason))
                return false;
        }

        foreach (var affectedSettlement in affected)
        {
            ApplyCultureToSettlementAndNotables(affectedSettlement, targetCulture);
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, CultureChangeCost, false);

        reason = $"{settlement.Name} changed culture to {targetCulture.Name}. {CultureChangeCost:N0} denars paid. " +
                 "Recruitment population refreshed; lords, companions and existing troops were left untouched.";
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

    private static CultureObject GetPlayerClanCulture()
        => Clan.PlayerClan?.Culture ?? Hero.MainHero?.Culture;

    private static bool ValidateTargetCulture(CultureObject targetCulture, Settlement settlement, out string reason)
    {
        if (targetCulture == null)
        {
            reason = "KaiTOR could not determine the player clan culture.";
            return false;
        }

        // Dynamic rather than hard-coded race list: this supports every TOR culture that
        // exposes a valid settlement recruitment tree, including cultures added by TOR later.
        if (targetCulture.BasicTroop == null)
        {
            reason = $"Culture '{targetCulture.StringId}' has no BasicTroop and cannot safely supply settlement recruits.";
            return false;
        }

        if (targetCulture.EliteBasicTroop == null && settlement?.BoundVillages?.Any(v => v?.Bound?.IsCastle == true) == true)
        {
            reason = $"Culture '{targetCulture.StringId}' has no EliteBasicTroop for castle-bound village recruitment.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool ValidateNotableTemplates(CultureObject targetCulture, Settlement settlement, out string reason)
    {
        foreach (var notable in settlement.Notables)
        {
            if (notable == null) continue;
            var occupation = notable.Occupation;
            if (!targetCulture.NotableTemplates.Any(template => template != null && template.Occupation == occupation))
            {
                reason = $"Culture '{targetCulture.StringId}' has no notable template for occupation '{occupation}' required by {settlement.Name}.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static IEnumerable<Settlement> GetAffectedSettlements(Settlement settlement)
    {
        yield return settlement;

        if (settlement.BoundVillages == null) yield break;
        foreach (var village in settlement.BoundVillages)
        {
            if (village?.Settlement != null)
                yield return village.Settlement;
        }
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
