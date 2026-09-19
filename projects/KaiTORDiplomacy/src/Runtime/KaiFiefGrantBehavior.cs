using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Player-ruler fief grant frontend. Ownership transfer itself is fully owned by
/// Bannerlord's KingdomManager/ChangeOwnerOfSettlementAction pipeline.
/// </summary>
public sealed class KaiFiefGrantBehavior : CampaignBehaviorBase
{
    private const float GrantInfluenceCost = 25f;
    private const float RecipientInfluenceBonus = 10f;

    public override void RegisterEvents() { }
    public override void SyncData(IDataStore dataStore) { }

    public bool CanGrant(out string reason)
    {
        reason = string.Empty;
        var clan = Clan.PlayerClan;
        var kingdom = clan?.Kingdom;
        if (Campaign.Current == null || Hero.MainHero == null || clan == null || kingdom == null)
        {
            reason = "Player kingdom is unavailable.";
            return false;
        }
        if (kingdom.RulingClan != clan || clan.Leader != Hero.MainHero)
        {
            reason = "Only the ruler may grant a fief.";
            return false;
        }
        if (clan.Influence < GrantInfluenceCost)
        {
            reason = $"At least {GrantInfluenceCost:0} influence is required.";
            return false;
        }
        if (!GetGrantableSettlements().Any())
        {
            reason = "No safe player-clan fortification is available.";
            return false;
        }
        if (!GetEligibleRecipientClans(null).Any())
        {
            reason = "No eligible recipient clan is available.";
            return false;
        }
        return true;
    }

    public void OpenGrantDialog()
    {
        if (!CanGrant(out var reason))
        {
            MBInformationManager.AddQuickInformation(new TextObject(reason), 3500, null, null, string.Empty);
            return;
        }

        var settlements = GetGrantableSettlements()
            .Select(s => new InquiryElement(
                s,
                s.Name.ToString(),
                null,
                true,
                $"{(s.IsTown ? "Town" : "Castle")} | owner: {s.OwnerClan?.Name}"))
            .ToList();

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Grant a fief",
                "Choose one of your clan's safe fortifications.",
                settlements,
                true,
                1,
                1,
                "Choose",
                "Cancel",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Settlement settlement)
                        return;
                    OpenRecipientDialog(settlement);
                },
                null),
            true,
            true);
    }

    public IEnumerable<string> DescribeStatus()
    {
        var clan = Clan.PlayerClan;
        var kingdom = clan?.Kingdom;
        if (kingdom == null)
        {
            yield return "Fief grant: player has no kingdom.";
            yield break;
        }

        yield return
            $"Fief grant: ruler={(kingdom.RulingClan == clan && clan.Leader == Hero.MainHero)}; " +
            $"grantable={GetGrantableSettlements().Count()}; " +
            $"recipientClans={GetEligibleRecipientClans(null).Count()}; " +
            $"influence={clan.Influence:0.0}; cost={GrantInfluenceCost:0}.";
    }

    private void OpenRecipientDialog(Settlement settlement)
    {
        if (settlement == null || !GetGrantableSettlements().Contains(settlement))
            return;

        var recipients = GetEligibleRecipientClans(settlement)
            .Select(clan => new InquiryElement(
                clan,
                clan.Name.ToString(),
                null,
                clan.Leader != null && clan.Leader.IsAlive,
                $"Leader: {clan.Leader?.Name} | fiefs: {clan.Fiefs.Count()}"))
            .ToList();

        if (recipients.Count == 0)
        {
            MBInformationManager.AddQuickInformation(
                new TextObject("No eligible recipient clan is available."),
                3500,
                null,
                null,
                string.Empty);
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Grant a fief",
                $"Choose the house that will receive {settlement.Name}.",
                recipients,
                true,
                1,
                1,
                "Grant",
                "Cancel",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Clan recipient)
                        return;
                    TryGrant(settlement, recipient, out _);
                },
                null),
            true,
            true);
    }

    public bool TryGrant(Settlement settlement, Clan recipient, out string reason)
    {
        reason = string.Empty;
        if (!CanGrant(out reason))
            return false;

        var playerClan = Clan.PlayerClan;
        var kingdom = playerClan.Kingdom;

        if (settlement == null ||
            !settlement.IsFortification ||
            settlement.IsUnderSiege ||
            settlement.OwnerClan != playerClan ||
            settlement.MapFaction != kingdom)
        {
            reason = "The selected fief is no longer safe to grant.";
            return false;
        }

        if (recipient == null ||
            recipient == playerClan ||
            recipient.Kingdom != kingdom ||
            recipient.IsEliminated ||
            recipient.IsUnderMercenaryService ||
            recipient.IsClanTypeMercenary ||
            recipient.Leader == null ||
            !recipient.Leader.IsAlive)
        {
            reason = "The selected house is not eligible.";
            return false;
        }

        try
        {
            ChangeClanInfluenceAction.Apply(playerClan, -GrantInfluenceCost);
            Campaign.Current.KingdomManager.GiftSettlementOwnership(settlement, recipient);
            ChangeClanInfluenceAction.Apply(recipient, RecipientInfluenceBonus);

            KaiRuntimeLog.Write(
                "FIEF_GRANTED",
                $"settlement={settlement.StringId}; from={playerClan.StringId}; to={recipient.StringId}; " +
                $"influenceCost={GrantInfluenceCost:0}; recipientInfluence=+{RecipientInfluenceBonus:0}");

            MBInformationManager.AddQuickInformation(
                new TextObject($"{settlement.Name} has been granted to {recipient.Name}."),
                4000,
                recipient.Leader.CharacterObject,
                null,
                string.Empty);

            return true;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception(
                "FIEF_GRANT_FAILED",
                ex,
                $"settlement={settlement?.StringId ?? "none"}; recipient={recipient?.StringId ?? "none"}");
            reason = ex.GetType().Name;
            return false;
        }
    }

    private static IEnumerable<Settlement> GetGrantableSettlements()
        => Clan.PlayerClan?.Fiefs
            .Where(s =>
                s != null &&
                s.IsFortification &&
                !s.IsUnderSiege &&
                s.OwnerClan == Clan.PlayerClan)
            .OrderByDescending(s => s.IsTown)
            .ThenBy(s => s.StringId, StringComparer.Ordinal)
            ?? Enumerable.Empty<Settlement>();

    private static IEnumerable<Clan> GetEligibleRecipientClans(Settlement settlement)
    {
        var kingdom = Clan.PlayerClan?.Kingdom;
        if (kingdom == null)
            return Enumerable.Empty<Clan>();

        return kingdom.Clans
            .Where(c =>
                c != null &&
                c != Clan.PlayerClan &&
                !c.IsEliminated &&
                !c.IsUnderMercenaryService &&
                !c.IsClanTypeMercenary &&
                !c.IsBanditFaction &&
                !c.IsRebelClan &&
                c.Leader != null &&
                c.Leader.IsAlive)
            .OrderBy(c => c.Fiefs.Count())
            .ThenByDescending(c => c.Tier)
            .ThenBy(c => c.StringId, StringComparer.Ordinal);
    }
}
