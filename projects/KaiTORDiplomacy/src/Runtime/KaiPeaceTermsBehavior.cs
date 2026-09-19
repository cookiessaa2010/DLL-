using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Negotiated peace frontend. TOR remains the strategic owner of peace willingness;
/// KaiTOR reads TOR's active DiplomacyModel score and, only after both rulers accept,
/// executes terms through Bannerlord Actions.
/// </summary>
public sealed class KaiPeaceTermsBehavior : CampaignBehaviorBase
{
    private const string StartOwnerClanSaveKey = "kaitor_peace_terms_v1_start_owner_clan";
    private const string StartOwnerKingdomSaveKey = "kaitor_peace_terms_v1_start_owner_kingdom";
    private const string CooldownSaveKey = "kaitor_peace_terms_v1_cooldown";
    private const string LastTermSaveKey = "kaitor_peace_terms_v1_last_term";

    private const int ProposalCooldownDays = 7;
    private const int PostWarNapDays = 90;

    private Dictionary<string, string> _startOwnerClan = new();
    private Dictionary<string, string> _startOwnerKingdom = new();
    private Dictionary<string, double> _cooldownUntil = new();
    private Dictionary<string, string> _lastTerm = new();

    private static Settlement _returnCandidate;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
        CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(StartOwnerClanSaveKey, ref _startOwnerClan);
        dataStore.SyncData(StartOwnerKingdomSaveKey, ref _startOwnerKingdom);
        dataStore.SyncData(CooldownSaveKey, ref _cooldownUntil);
        dataStore.SyncData(LastTermSaveKey, ref _lastTerm);

        _startOwnerClan ??= new Dictionary<string, string>();
        _startOwnerKingdom ??= new Dictionary<string, string>();
        _cooldownUntil ??= new Dictionary<string, double>();
        _lastTerm ??= new Dictionary<string, string>();
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        starter.AddPlayerLine(
            "kaitor_peace_terms_open",
            "hero_main_options",
            "kaitor_peace_terms_reply",
            "{=kaitor_diplomacy_ui_peace_open}I want to discuss terms for ending this war.",
            CanOpenNegotiation,
            null,
            114,
            null,
            null);

        starter.AddDialogLine(
            "kaitor_peace_terms_reply",
            "kaitor_peace_terms_reply",
            "kaitor_peace_terms_options",
            "{=kaitor_diplomacy_ui_peace_reply}State your terms. I will judge whether peace is worth the price.",
            CanOpenNegotiation,
            null,
            100,
            null);

        starter.AddPlayerLine(
            "kaitor_peace_status_quo",
            "kaitor_peace_terms_options",
            "close_window",
            "{=kaitor_diplomacy_ui_peace_status_quo}Peace without additional demands.",
            () => CanOffer(TermKind.StatusQuo),
            () => ResolveOffer(TermKind.StatusQuo),
            120,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_peace_demand_reparations",
            "kaitor_peace_terms_options",
            "close_window",
            "{=kaitor_diplomacy_ui_peace_demand_reparations}You will pay reparations for this war.",
            () => CanOffer(TermKind.DemandReparations),
            () => ResolveOffer(TermKind.DemandReparations),
            119,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_peace_offer_reparations",
            "kaitor_peace_terms_options",
            "close_window",
            "{=kaitor_diplomacy_ui_peace_offer_reparations}We will pay reparations to secure peace.",
            () => CanOffer(TermKind.OfferReparations),
            () => ResolveOffer(TermKind.OfferReparations),
            118,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_peace_return_fief",
            "kaitor_peace_terms_options",
            "close_window",
            "{=kaitor_diplomacy_ui_peace_return_fief}Return one of the holdings taken from our realm.",
            () => CanOffer(TermKind.ReturnFief),
            () => ResolveOffer(TermKind.ReturnFief),
            117,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_peace_nap",
            "kaitor_peace_terms_options",
            "close_window",
            "{=kaitor_diplomacy_ui_peace_nap}Peace, followed by a ninety-day non-aggression pact.",
            () => CanOffer(TermKind.PeaceAndNap),
            () => ResolveOffer(TermKind.PeaceAndNap),
            116,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_peace_back",
            "kaitor_peace_terms_options",
            "hero_main_options",
            "{=kaitor_diplomacy_ui_peace_back}Not now.",
            null,
            null,
            90,
            null,
            null);
    }

    public IEnumerable<string> DescribeStatus()
    {
        var source = Clan.PlayerClan?.Kingdom;
        if (source == null)
        {
            yield return "Peace terms: player has no kingdom.";
            yield break;
        }

        var wars = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k != source && source.IsAtWarWith(k))
            .OrderBy(k => k.StringId, StringComparer.Ordinal)
            .ToArray();

        if (wars.Length == 0)
        {
            yield return "Peace terms: no active player-kingdom wars.";
            yield break;
        }

        foreach (var enemy in wars)
        {
            var targetScore = GetTargetPeaceScore(enemy, source);
            var candidate = FindReturnCandidate(source, enemy);
            yield return
                $"{source.StringId}<->{enemy.StringId}: targetPeaceScore={targetScore:0.0}; " +
                $"returnCandidate={candidate?.StringId ?? "none"}; cooldown={GetCooldownDays(source, enemy):0.0}d";
        }
    }

    private static bool CanOpenNegotiation()
    {
        var source = Clan.PlayerClan?.Kingdom;
        var targetHero = Hero.OneToOneConversationHero;
        var target = targetHero?.MapFaction as Kingdom;
        if (source == null || target == null || target == source || !source.IsAtWarWith(target))
            return false;
        if (source.RulingClan != Clan.PlayerClan || Hero.MainHero != Clan.PlayerClan.Leader)
            return false;
        if (target.Leader != targetHero)
            return false;

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiPeaceTermsBehavior>();
        return behavior != null && behavior.GetCooldownDays(source, target) <= 0f;
    }

    private static bool CanOffer(TermKind kind)
    {
        if (!CanOpenNegotiation())
            return false;

        var source = Clan.PlayerClan.Kingdom;
        var target = Hero.OneToOneConversationHero.MapFaction as Kingdom;
        if (target == null)
            return false;

        var behavior = Campaign.Current.GetCampaignBehavior<KaiPeaceTermsBehavior>();
        if (behavior == null)
            return false;

        if (kind == TermKind.ReturnFief)
        {
            _returnCandidate = behavior.FindReturnCandidate(source, target);
            return _returnCandidate != null;
        }

        var amount = behavior.CalculateReparations(source, target);
        if (kind == TermKind.DemandReparations)
            return target.RulingClan?.Leader != null && target.RulingClan.Leader.Gold >= amount;
        if (kind == TermKind.OfferReparations)
            return source.RulingClan?.Leader != null && source.RulingClan.Leader.Gold >= amount;

        return true;
    }

    private static void ResolveOffer(TermKind kind)
    {
        var source = Clan.PlayerClan?.Kingdom;
        var target = Hero.OneToOneConversationHero?.MapFaction as Kingdom;
        var behavior = Campaign.Current?.GetCampaignBehavior<KaiPeaceTermsBehavior>();
        if (source == null || target == null || behavior == null || !source.IsAtWarWith(target))
            return;

        var score = behavior.GetTargetPeaceScore(target, source);
        var amount = behavior.CalculateReparations(source, target);
        var returnFief = kind == TermKind.ReturnFief
            ? (_returnCandidate ?? behavior.FindReturnCandidate(source, target))
            : null;

        var required = behavior.GetRequiredPeaceScore(kind, amount, returnFief);
        var accepted = score >= required;

        KaiRuntimeLog.Write(
            "PEACE_TERMS_PROPOSED",
            $"source={source.StringId}; target={target.StringId}; kind={kind}; targetScore={score:0.0}; required={required:0.0}; amount={amount}; fief={returnFief?.StringId ?? "none"}; accepted={accepted}");

        behavior.SetCooldown(source, target);

        if (!accepted)
        {
            MBInformationManager.AddQuickInformation(
                new TextObject("{=kaitor_diplomacy_ui_peace_rejected}The opposing ruler rejects these terms."),
                3500,
                target.Leader?.CharacterObject,
                null,
                string.Empty);
            return;
        }

        behavior.ApplyAcceptedTerms(source, target, kind, amount, returnFief);
    }

    private void ApplyAcceptedTerms(
        Kingdom source,
        Kingdom target,
        TermKind kind,
        int reparations,
        Settlement returnFief)
    {
        // Resolve the original owner before MakePeace fires OnPeaceMade and clears the snapshot.
        var originalOwner = returnFief == null ? null : ResolveStartOwner(returnFief, source, target);

        MakePeaceAction.Apply(source, target);

        if (kind == TermKind.DemandReparations &&
            target.RulingClan?.Leader != null &&
            source.RulingClan?.Leader != null)
        {
            GiveGoldAction.ApplyBetweenCharacters(
                target.RulingClan.Leader,
                source.RulingClan.Leader,
                reparations,
                true);
        }
        else if (kind == TermKind.OfferReparations &&
                 source.RulingClan?.Leader != null &&
                 target.RulingClan?.Leader != null)
        {
            GiveGoldAction.ApplyBetweenCharacters(
                source.RulingClan.Leader,
                target.RulingClan.Leader,
                reparations,
                true);
        }
        else if (kind == TermKind.ReturnFief &&
                 returnFief != null &&
                 originalOwner?.Leader != null &&
                 originalOwner.Kingdom == source)
        {
            ChangeOwnerOfSettlementAction.ApplyByBarter(originalOwner.Leader, returnFief);
        }
        else if (kind == TermKind.PeaceAndNap)
        {
            var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
            if (diplomacy != null &&
                !diplomacy.TryCreateNonAggressionPact(source, target, PostWarNapDays, out var reason))
            {
                KaiRuntimeLog.Write(
                    "PEACE_TERMS_NAP_FAILED",
                    $"source={source.StringId}; target={target.StringId}; reason={reason}");
            }
        }

        var key = PairKey(source, target);
        _lastTerm[key] = kind.ToString();

        KaiRuntimeLog.Write(
            "PEACE_TERMS_ACCEPTED",
            $"source={source.StringId}; target={target.StringId}; kind={kind}; reparations={reparations}; fief={returnFief?.StringId ?? "none"}");

        MBInformationManager.AddQuickInformation(
            new TextObject("{=kaitor_diplomacy_ui_peace_accepted}Peace terms have been accepted."),
            3500,
            target.Leader?.CharacterObject,
            null,
            string.Empty);
    }

    private void OnWarDeclared(
        IFaction firstFaction,
        IFaction secondFaction,
        DeclareWarAction.DeclareWarDetail detail)
    {
        if (firstFaction is not Kingdom first || secondFaction is not Kingdom second || first == second)
            return;

        var pair = PairKey(first, second);
        RemoveSnapshot(pair);

        foreach (var settlement in Settlement.All.Where(s =>
                     s != null &&
                     s.IsFortification &&
                     s.OwnerClan != null &&
                     (s.OwnerClan.Kingdom == first || s.OwnerClan.Kingdom == second)))
        {
            var key = SnapshotKey(pair, settlement.StringId);
            _startOwnerClan[key] = settlement.OwnerClan.StringId;
            _startOwnerKingdom[key] = settlement.OwnerClan.Kingdom?.StringId ?? "none";
        }
    }

    private void OnPeaceMade(
        IFaction firstFaction,
        IFaction secondFaction,
        MakePeaceAction.MakePeaceDetail detail)
    {
        if (firstFaction is not Kingdom first || secondFaction is not Kingdom second || first == second)
            return;

        RemoveSnapshot(PairKey(first, second));
    }

    private Settlement FindReturnCandidate(Kingdom source, Kingdom target)
    {
        if (source == null || target == null)
            return null;

        var pair = PairKey(source, target);
        return Settlement.All
            .Where(s =>
                s != null &&
                s.IsFortification &&
                s.OwnerClan?.Kingdom == target &&
                WasOwnedByKingdomAtWarStart(pair, s.StringId, source.StringId))
            .OrderByDescending(s => s.IsTown)
            .ThenByDescending(s => s.Town?.Prosperity ?? 0f)
            .FirstOrDefault();
    }

    private Clan ResolveStartOwner(Settlement settlement, Kingdom source, Kingdom target)
    {
        if (settlement == null)
            return null;

        var key = SnapshotKey(PairKey(source, target), settlement.StringId);
        if (_startOwnerClan.TryGetValue(key, out var clanId))
        {
            var clan = Clan.All.FirstOrDefault(c =>
                c != null && string.Equals(c.StringId, clanId, StringComparison.Ordinal));
            if (clan != null && !clan.IsEliminated && clan.Kingdom == source)
                return clan;
        }

        return source.RulingClan;
    }

    private bool WasOwnedByKingdomAtWarStart(string pair, string settlementId, string kingdomId)
    {
        var key = SnapshotKey(pair, settlementId);
        return _startOwnerKingdom.TryGetValue(key, out var stored) &&
               string.Equals(stored, kingdomId, StringComparison.Ordinal);
    }

    private float GetTargetPeaceScore(Kingdom target, Kingdom source)
    {
        if (target == null || source == null || Campaign.Current?.Models?.DiplomacyModel == null)
            return float.MinValue;

        return Campaign.Current.Models.DiplomacyModel.GetScoreOfDeclaringPeace(target, source);
    }

    private int CalculateReparations(Kingdom source, Kingdom target)
    {
        var war = Campaign.Current?.GetCampaignBehavior<KaiWarExhaustionBehavior>();
        var sourceExhaustion = war?.GetExhaustion(source, target) ?? 0f;
        var targetExhaustion = war?.GetExhaustion(target, source) ?? 0f;
        var difference = Math.Abs(targetExhaustion - sourceExhaustion);

        return Math.Max(10000, Math.Min(100000, 15000 + (int)Math.Round(difference * 850f)));
    }

    private float GetRequiredPeaceScore(TermKind kind, int amount, Settlement returnFief)
    {
        switch (kind)
        {
            case TermKind.StatusQuo:
                return 0f;
            case TermKind.PeaceAndNap:
                return 10f;
            case TermKind.DemandReparations:
                return 20f + amount / 2500f;
            case TermKind.OfferReparations:
                return -Math.Min(60f, amount / 1800f);
            case TermKind.ReturnFief:
                return returnFief?.IsTown == true ? 70f : 45f;
            default:
                return 0f;
        }
    }

    private float GetCooldownDays(Kingdom first, Kingdom second)
    {
        var key = PairKey(first, second);
        if (!_cooldownUntil.TryGetValue(key, out var until))
            return 0f;
        return (float)Math.Max(0d, until - CampaignTime.Now.ToDays);
    }

    private void SetCooldown(Kingdom first, Kingdom second)
        => _cooldownUntil[PairKey(first, second)] =
            CampaignTime.Now.ToDays + ProposalCooldownDays;

    private void RemoveSnapshot(string pair)
    {
        var prefix = pair + "|";
        foreach (var key in _startOwnerClan.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _startOwnerClan.Remove(key);
        foreach (var key in _startOwnerKingdom.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _startOwnerKingdom.Remove(key);
    }

    private static string SnapshotKey(string pair, string settlementId)
        => pair + "|" + settlementId;

    private static string PairKey(Kingdom first, Kingdom second)
        => string.CompareOrdinal(first.StringId, second.StringId) <= 0
            ? first.StringId + "<->" + second.StringId
            : second.StringId + "<->" + first.StringId;

    private enum TermKind
    {
        StatusQuo,
        DemandReparations,
        OfferReparations,
        ReturnFief,
        PeaceAndNap
    }
}
