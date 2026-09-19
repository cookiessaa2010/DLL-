using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Persists a temporary conquest claim for the actual siege capturer. The claim only
/// modifies merit inside Bannerlord's normal SettlementClaimantDecision; it never assigns
/// settlement ownership directly.
/// </summary>
public sealed class KaiConquestClaimBehavior : CampaignBehaviorBase
{
    private const string ClanSaveKey = "kaitor_conquest_claim_v1_clan";
    private const string HeroSaveKey = "kaitor_conquest_claim_v1_hero";
    private const string DaySaveKey = "kaitor_conquest_claim_v1_day";
    private const string DeniedSaveKey = "kaitor_conquest_claim_v1_denied";
    private const int ClaimLifetimeDays = 60;
    private const float MeritMultiplier = 1.35f;

    private Dictionary<string, string> _claimClan = new();
    private Dictionary<string, string> _claimHero = new();
    private Dictionary<string, double> _claimDay = new();
    private Dictionary<string, int> _deniedCount = new();

    public override void RegisterEvents()
    {
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnDecisionConcluded);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, CleanupExpired);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(ClanSaveKey, ref _claimClan);
        dataStore.SyncData(HeroSaveKey, ref _claimHero);
        dataStore.SyncData(DaySaveKey, ref _claimDay);
        dataStore.SyncData(DeniedSaveKey, ref _deniedCount);

        _claimClan ??= new Dictionary<string, string>();
        _claimHero ??= new Dictionary<string, string>();
        _claimDay ??= new Dictionary<string, double>();
        _deniedCount ??= new Dictionary<string, int>();
    }

    public bool TryGetClaim(Settlement settlement, out Clan claimant, out Hero capturer)
    {
        claimant = null;
        capturer = null;
        if (settlement == null || !_claimClan.TryGetValue(settlement.StringId, out var clanId))
            return false;

        if (!_claimDay.TryGetValue(settlement.StringId, out var day) ||
            CampaignTime.Now.ToDays - day > ClaimLifetimeDays)
        {
            Clear(settlement.StringId);
            return false;
        }

        claimant = Clan.All.FirstOrDefault(c =>
            c != null &&
            !c.IsEliminated &&
            string.Equals(c.StringId, clanId, StringComparison.Ordinal));

        if (_claimHero.TryGetValue(settlement.StringId, out var heroId))
            capturer = Hero.AllAliveHeroes.FirstOrDefault(h =>
                h != null && string.Equals(h.StringId, heroId, StringComparison.Ordinal));

        if (claimant == null)
        {
            Clear(settlement.StringId);
            capturer = null;
            return false;
        }

        return true;
    }

    public float ApplyClaimMerit(Settlement settlement, Clan candidate, float baseMerit)
    {
        if (baseMerit <= 0f ||
            settlement == null ||
            candidate == null ||
            !TryGetClaim(settlement, out var claimant, out _) ||
            claimant != candidate)
            return baseMerit;

        return baseMerit * MeritMultiplier;
    }

    public IEnumerable<string> DescribeStatus()
    {
        CleanupExpired();

        if (_claimClan.Count == 0)
        {
            yield return "Conquest claims: none.";
            yield break;
        }

        foreach (var settlementId in _claimClan.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            _claimDay.TryGetValue(settlementId, out var day);
            _claimHero.TryGetValue(settlementId, out var hero);
            _deniedCount.TryGetValue(settlementId, out var denied);
            yield return
                $"{settlementId}: clan={_claimClan[settlementId]}; hero={hero ?? "none"}; " +
                $"age={Math.Max(0d, CampaignTime.Now.ToDays - day):0.0}d; denied={denied}; merit=x{MeritMultiplier:0.00}";
        }
    }

    private void OnSettlementOwnerChanged(
        Settlement settlement,
        bool openToClaim,
        Hero newOwner,
        Hero oldOwner,
        Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        if (settlement == null || !settlement.IsFortification)
            return;

        if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege ||
            !openToClaim ||
            capturerHero?.Clan == null ||
            newOwner?.Clan == null ||
            capturerHero.Clan.Kingdom == null ||
            newOwner.Clan.Kingdom != capturerHero.Clan.Kingdom)
        {
            // Non-siege ownership changes must not inherit stale military claims.
            if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision)
                Clear(settlement.StringId);
            return;
        }

        _claimClan[settlement.StringId] = capturerHero.Clan.StringId;
        _claimHero[settlement.StringId] = capturerHero.StringId;
        _claimDay[settlement.StringId] = CampaignTime.Now.ToDays;
        _deniedCount[settlement.StringId] = 0;

        KaiRuntimeLog.Write(
            "CONQUEST_CLAIM_CREATED",
            $"settlement={settlement.StringId}; claimantClan={capturerHero.Clan.StringId}; " +
            $"capturer={capturerHero.StringId}; kingdom={capturerHero.Clan.Kingdom.StringId}; lifetime={ClaimLifetimeDays}d");
    }

    private void OnDecisionConcluded(
        KingdomDecision decision,
        DecisionOutcome chosenOutcome,
        bool isPlayerInvolved)
    {
        if (decision is not SettlementClaimantDecision claimantDecision ||
            chosenOutcome is not SettlementClaimantDecision.ClanAsDecisionOutcome outcome ||
            claimantDecision.Settlement == null ||
            !TryGetClaim(claimantDecision.Settlement, out var claimant, out var capturer))
            return;

        var settlement = claimantDecision.Settlement;
        if (outcome.Clan == claimant)
        {
            KaiRuntimeLog.Write(
                "CONQUEST_CLAIM_RESOLVED",
                $"settlement={settlement.StringId}; claimant={claimant.StringId}; result=granted");
        }
        else
        {
            _deniedCount.TryGetValue(settlement.StringId, out var denied);
            _deniedCount[settlement.StringId] = denied + 1;

            KaiRuntimeLog.Write(
                "CONQUEST_CLAIM_RESOLVED",
                $"settlement={settlement.StringId}; claimant={claimant.StringId}; " +
                $"capturer={capturer?.StringId ?? "none"}; result=denied; chosen={outcome.Clan?.StringId ?? "none"}");

            KaiRuntimeLog.Write(
                "GRIEVANCE_DEFERRED",
                $"type=conquest_claim_denied; source={claimant.StringId}; " +
                $"target={claimantDecision.Kingdom?.RulingClan?.StringId ?? "none"}; settlement={settlement.StringId}");
        }

        Clear(settlement.StringId);
    }

    private void CleanupExpired()
    {
        if (_claimDay == null || _claimDay.Count == 0)
            return;

        var now = CampaignTime.Now.ToDays;
        foreach (var key in _claimDay
                     .Where(x => double.IsNaN(x.Value) ||
                                 double.IsInfinity(x.Value) ||
                                 now - x.Value > ClaimLifetimeDays)
                     .Select(x => x.Key)
                     .ToArray())
            Clear(key);
    }

    private void Clear(string settlementId)
    {
        if (string.IsNullOrWhiteSpace(settlementId))
            return;
        _claimClan.Remove(settlementId);
        _claimHero.Remove(settlementId);
        _claimDay.Remove(settlementId);
        _deniedCount.Remove(settlementId);
    }
}

[HarmonyPatch(typeof(SettlementClaimantDecision), nameof(SettlementClaimantDecision.CalculateMeritOfOutcome))]
internal static class KaiConquestClaimMeritPatch
{
    private static void Postfix(
        SettlementClaimantDecision __instance,
        DecisionOutcome candidateOutcome,
        ref float __result)
    {
        if (__instance?.Settlement == null ||
            candidateOutcome is not SettlementClaimantDecision.ClanAsDecisionOutcome outcome)
            return;

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiConquestClaimBehavior>();
        if (behavior == null)
            return;

        __result = behavior.ApplyClaimMerit(__instance.Settlement, outcome.Clan, __result);
    }
}
