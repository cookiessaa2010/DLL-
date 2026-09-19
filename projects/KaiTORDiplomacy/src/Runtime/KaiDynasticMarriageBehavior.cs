using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Political marriage contract layered over Bannerlord's native marriage barter.
/// 500,000 denars are held in a small persisted escrow while barter is open. The
/// accepted MarriageAction consumes the escrow and creates a 180-day Dynastic Bond.
/// Cancellation or any failed marriage path refunds the escrow in full.
/// </summary>
public sealed class KaiDynasticMarriageBehavior : CampaignBehaviorBase
{
    public const int PoliticalMarriageCost = 500000;
    public const int DynasticBondDays = 180;
    public const int DynasticRelationBonus = 20;
    public const int DynasticTrustBonus = 30;

    private const string BondExpirySaveKey = "kaitor_dynastic_bond_expiry_v1";
    private const string PendingMemberSaveKey = "kaitor_dynastic_pending_member_v1";
    private const string PendingTargetSaveKey = "kaitor_dynastic_pending_target_v1";
    private const string PendingTargetClanSaveKey = "kaitor_dynastic_pending_target_clan_v1";
    private const string EscrowSaveKey = "kaitor_dynastic_escrow_v1";
    private const string DynasticStrengthSaveKey = "kaitor_dynastic_strength_v2";
    private const string DynasticTouchedSaveKey = "kaitor_dynastic_touched_v2";

    private const float DynasticDailyDecay = 0.02f;

    private Dictionary<string, double> _bondExpiryDays = new();
    private Dictionary<string, float> _dynasticStrength = new();
    private Dictionary<string, double> _dynasticTouchedDay = new();
    private string _pendingMemberId;
    private string _pendingTargetId;
    private string _pendingTargetClanId;
    private int _escrowGold;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnBeforeHeroesMarried);
        CampaignEvents.OnBarterCanceledEvent.AddNonSerializedListener(this, OnBarterCanceled);
        CampaignEvents.OnBarterAcceptedEvent.AddNonSerializedListener(this, OnBarterAccepted);
        CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(BondExpirySaveKey, ref _bondExpiryDays);
        dataStore.SyncData(PendingMemberSaveKey, ref _pendingMemberId);
        dataStore.SyncData(PendingTargetSaveKey, ref _pendingTargetId);
        dataStore.SyncData(PendingTargetClanSaveKey, ref _pendingTargetClanId);
        dataStore.SyncData(EscrowSaveKey, ref _escrowGold);
        dataStore.SyncData(DynasticStrengthSaveKey, ref _dynasticStrength);
        dataStore.SyncData(DynasticTouchedSaveKey, ref _dynasticTouchedDay);

        _bondExpiryDays ??= new Dictionary<string, double>();
        _dynasticStrength ??= new Dictionary<string, float>();
        _dynasticTouchedDay ??= new Dictionary<string, double>();
        if (_escrowGold < 0 || _escrowGold > PoliticalMarriageCost)
            _escrowGold = 0;
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        CleanupExpiredBonds();

        // Native barter is not restored as an in-progress transaction from a save.
        // A persisted escrow therefore means the prior negotiation did not finish.
        if (_escrowGold > 0 || HasPendingContract)
            RefundAndClear("session_recovery");
    }

    public bool CanBeginPoliticalMarriage(Hero member, Hero target, Clan targetClan, out string reason)
    {
        reason = string.Empty;
        if (Hero.MainHero == null || Clan.PlayerClan == null || Campaign.Current == null)
        {
            reason = "Династические переговоры сейчас недоступны.";
            return false;
        }
        if (HasPendingContract || _escrowGold > 0)
        {
            reason = "Другие династические переговоры ещё не завершены.";
            return false;
        }
        if (Hero.MainHero.Gold < PoliticalMarriageCost)
        {
            reason = $"Для политического брака требуется {PoliticalMarriageCost:N0} динаров.";
            return false;
        }
        if (member == null || member.Clan != Clan.PlayerClan || target == null || targetClan == null || target.Clan != targetClan)
        {
            reason = "Выбранная брачная пара больше недоступна.";
            return false;
        }

        var model = Campaign.Current.Models.MarriageModel;
        if (model == null || !model.IsSuitableForMarriage(member) || !model.IsSuitableForMarriage(target) ||
            !model.IsCoupleSuitableForMarriage(member, target))
        {
            reason = "Эта пара не соответствует условиям брака.";
            return false;
        }

        if (targetClan.Leader == null || !targetClan.Leader.IsAlive || targetClan.Leader.IsPrisoner)
        {
            reason = "Глава другого дома сейчас не может заключить династический договор.";
            return false;
        }

        return true;
    }

    public bool BeginPoliticalMarriage(Hero member, Hero target, Clan targetClan, out string reason)
    {
        if (!CanBeginPoliticalMarriage(member, target, targetClan, out reason))
            return false;

        // Reserve the fixed contract cost before opening barter. It is either paid to
        // the other house when MarriageAction fires or refunded on cancel/failure.
        Hero.MainHero.ChangeHeroGold(-PoliticalMarriageCost);
        _escrowGold = PoliticalMarriageCost;
        _pendingMemberId = member.StringId;
        _pendingTargetId = target.StringId;
        _pendingTargetClanId = targetClan.StringId;

        KaiRuntimeLog.Write(
            "DYNASTIC_ESCROW_HELD",
            $"member={member.StringId}; target={target.StringId}; clan={targetClan.StringId}; amount={PoliticalMarriageCost}");

        if (KaiMarriageBarterBridge.TryStart(member, target, targetClan, out reason))
            return true;

        RefundAndClear("barter_start_failed");
        return false;
    }

    public bool IsDynasticBondActive(Clan first, Clan second)
    {
        if (first == null || second == null || first == second)
            return false;
        var key = ClanPairKey(first, second);
        return _bondExpiryDays.TryGetValue(key, out var expiry) && expiry > CampaignTime.Now.ToDays;
    }

    private void OnBeforeHeroesMarried(Hero firstHero, Hero secondHero, bool showNotification)
    {
        RecordDynasticMarriage(firstHero, secondHero);

        if (!MatchesPendingPair(firstHero, secondHero))
            return;

        var targetClan = Clan.All.FirstOrDefault(c => c != null && string.Equals(c.StringId, _pendingTargetClanId, StringComparison.Ordinal));
        if (targetClan == null || targetClan.IsEliminated)
        {
            RefundAndClear("target_clan_missing_at_marriage");
            return;
        }

        var playerClan = Clan.PlayerClan;
        var recipient = targetClan.Leader;
        if (playerClan == null || recipient == null || !recipient.IsAlive)
        {
            RefundAndClear("contract_parties_missing_at_marriage");
            return;
        }

        var paid = _escrowGold;
        if (paid > 0)
            recipient.ChangeHeroGold(paid);

        _escrowGold = 0;
        var memberId = _pendingMemberId;
        var targetId = _pendingTargetId;
        ClearPendingIds();

        var bondKey = ClanPairKey(playerClan, targetClan);
        _bondExpiryDays[bondKey] = CampaignTime.Now.ToDays + DynasticBondDays;
        SetDynasticStrengthAtLeast(playerClan, targetClan, 100f, "political_marriage");

        if (playerClan.Leader != null && targetClan.Leader != null)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(playerClan.Leader, targetClan.Leader, DynasticRelationBonus, true);

        var firstKingdom = playerClan.Kingdom;
        var secondKingdom = targetClan.Kingdom;
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        var napResult = "not_applicable";

        if (diplomacy != null && firstKingdom != null && secondKingdom != null && firstKingdom != secondKingdom)
        {
            diplomacy.AdjustTrust(firstKingdom, secondKingdom, DynasticTrustBonus);
            if (diplomacy.EnsureNonAggressionPactAtLeast(firstKingdom, secondKingdom, DynasticBondDays, out var napReason))
                napResult = "active_180d";
            else
                napResult = "unavailable:" + napReason;
        }

        KaiRuntimeLog.Write(
            "DYNASTIC_BOND_CREATED",
            $"member={memberId}; target={targetId}; playerClan={playerClan.StringId}; targetClan={targetClan.StringId}; paid={paid}; relation=+{DynasticRelationBonus}; trust=+{DynasticTrustBonus}; nap={napResult}; duration={DynasticBondDays}d");

        MBInformationManager.AddQuickInformation(
            new TextObject($"Династический брак заключён. Дом {targetClan.Name} получил {paid:N0} динаров; династические узы действуют {DynasticBondDays} дней."),
            5000,
            targetClan.Leader?.CharacterObject,
            null,
            string.Empty);
    }

    private void OnBarterCanceled(Hero offerer, Hero other, List<Barterable> barters)
    {
        if (!HasPendingContract)
            return;
        if (ContainsPendingMarriage(barters))
            RefundAndClear("barter_cancelled");
    }

    private void OnBarterAccepted(Hero offerer, Hero other, List<Barterable> barters)
    {
        if (!HasPendingContract)
            return;

        // BarterManager applies MarriageBarterable before raising this event. If the
        // pending contract still exists here, MarriageAction did not consume it.
        if (ContainsPendingMarriage(barters))
            RefundAndClear("barter_accepted_without_marriage");
    }

    private void OnWarDeclared(IFaction firstFaction, IFaction secondFaction, DeclareWarAction.DeclareWarDetail detail)
    {
        if (firstFaction is not Kingdom first || secondFaction is not Kingdom second || _bondExpiryDays.Count == 0)
            return;

        foreach (var key in _bondExpiryDays.Keys.ToArray())
        {
            if (!TryResolveClanPair(key, out var firstClan, out var secondClan))
            {
                _bondExpiryDays.Remove(key);
                continue;
            }

            var k1 = firstClan.Kingdom;
            var k2 = secondClan.Kingdom;
            if (k1 == null || k2 == null)
                continue;

            if ((k1 == first && k2 == second) || (k1 == second && k2 == first))
            {
                _bondExpiryDays.Remove(key);

                // A Dynastic Bond is a political promise of its own. NAP may also
                // apply its normal war-breach penalty, but the marriage contract must
                // still have a consequence when no NAP could be created (or if event
                // ordering has already removed it). Revoke the bond's +30 trust.
                var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
                diplomacy?.AdjustTrust(k1, k2, -DynasticTrustBonus);
                AdjustDynasticStrength(firstClan, secondClan, -25f, "war_between_dynasties");

                KaiRuntimeLog.Write(
                    "DYNASTIC_BOND_BROKEN",
                    $"clans={key}; reason=war; kingdoms={first.StringId}/{second.StringId}; dynasticTrustPenalty=-{DynasticTrustBonus}");
            }
        }
    }

    private void OnDailyTick()
    {
        CleanupExpiredBonds();
        DecayDynasticMemory();
    }

    private void CleanupExpiredBonds()
    {
        _bondExpiryDays ??= new Dictionary<string, double>();
        var now = CampaignTime.Now.ToDays;
        foreach (var key in _bondExpiryDays
                     .Where(x => x.Value <= now || double.IsNaN(x.Value) || double.IsInfinity(x.Value))
                     .Select(x => x.Key)
                     .ToArray())
        {
            _bondExpiryDays.Remove(key);
            KaiRuntimeLog.Write("DYNASTIC_BOND_EXPIRED", $"clans={key}");
        }
    }

    private void DecayDynasticMemory()
    {
        _dynasticStrength ??= new Dictionary<string, float>();
        _dynasticTouchedDay ??= new Dictionary<string, double>();

        foreach (var key in _dynasticStrength.Keys.ToArray())
        {
            var value = _dynasticStrength[key];
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
            {
                _dynasticStrength.Remove(key);
                _dynasticTouchedDay.Remove(key);
                continue;
            }

            value = Math.Max(0f, value - DynasticDailyDecay);
            if (value <= 0.01f)
            {
                _dynasticStrength.Remove(key);
                _dynasticTouchedDay.Remove(key);
            }
            else
            {
                _dynasticStrength[key] = value;
            }
        }
    }

    public float GetDynasticStrength(Kingdom first, Kingdom second)
    {
        if (first?.RulingClan == null || second?.RulingClan == null || first == second)
            return 0f;

        var key = ClanPairKey(first.RulingClan, second.RulingClan);
        return _dynasticStrength.TryGetValue(key, out var value)
            ? Math.Max(0f, Math.Min(100f, value))
            : 0f;
    }

    public IEnumerable<string> DescribeDynasticBonds()
    {
        var now = CampaignTime.Now.ToDays;
        var keys = new HashSet<string>(_dynasticStrength.Keys, StringComparer.Ordinal);
        foreach (var key in _bondExpiryDays.Keys)
            keys.Add(key);

        if (keys.Count == 0)
        {
            yield return "Dynastic diplomacy: no recorded bonds.";
            yield break;
        }

        foreach (var key in keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            _dynasticStrength.TryGetValue(key, out var strength);
            _bondExpiryDays.TryGetValue(key, out var expiry);
            var activeDays = expiry > now ? Math.Ceiling(expiry - now) : 0d;
            yield return $"{key}: strength={strength:0.0}; activeContractDays={activeDays:0}";
        }
    }

    private void RecordDynasticMarriage(Hero firstHero, Hero secondHero)
    {
        var firstClan = firstHero?.Clan;
        var secondClan = secondHero?.Clan;
        if (firstClan == null || secondClan == null || firstClan == secondClan ||
            firstClan.IsEliminated || secondClan.IsEliminated ||
            firstClan.Kingdom == null || secondClan.Kingdom == null)
            return;

        var firstWeight = GetKinWeight(firstHero, firstClan);
        var secondWeight = GetKinWeight(secondHero, secondClan);
        var strength = Math.Max(35f, Math.Min(100f, (firstWeight + secondWeight) * 0.5f));

        SetDynasticStrengthAtLeast(firstClan, secondClan, strength, "marriage");
    }

    private static float GetKinWeight(Hero hero, Clan clan)
    {
        var leader = clan?.Leader;
        if (hero == null || leader == null)
            return 35f;
        if (hero == leader)
            return 100f;
        if (hero.Father == leader || hero.Mother == leader ||
            leader.Father == hero || leader.Mother == hero)
            return 90f;
        if ((hero.Father != null && hero.Father == leader.Father) ||
            (hero.Mother != null && hero.Mother == leader.Mother))
            return 70f;
        return 50f;
    }

    private void SetDynasticStrengthAtLeast(Clan first, Clan second, float value, string reason)
    {
        if (first == null || second == null || first == second)
            return;

        var key = ClanPairKey(first, second);
        var before = _dynasticStrength.TryGetValue(key, out var current) ? current : 0f;
        var after = Math.Max(before, Math.Max(0f, Math.Min(100f, value)));
        _dynasticStrength[key] = after;
        _dynasticTouchedDay[key] = CampaignTime.Now.ToDays;

        if (after > before + 0.01f)
            KaiRuntimeLog.Write(
                "DYNASTIC_MODIFIER",
                $"clans={key}; reason={reason}; before={before:0.0}; after={after:0.0}");
    }

    private void AdjustDynasticStrength(Clan first, Clan second, float delta, string reason)
    {
        if (first == null || second == null || first == second || delta == 0f)
            return;

        var key = ClanPairKey(first, second);
        var before = _dynasticStrength.TryGetValue(key, out var current) ? current : 0f;
        var after = Math.Max(0f, Math.Min(100f, before + delta));
        if (after <= 0.01f)
        {
            _dynasticStrength.Remove(key);
            _dynasticTouchedDay.Remove(key);
        }
        else
        {
            _dynasticStrength[key] = after;
            _dynasticTouchedDay[key] = CampaignTime.Now.ToDays;
        }

        KaiRuntimeLog.Write(
            "DYNASTIC_MODIFIER",
            $"clans={key}; reason={reason}; before={before:0.0}; after={after:0.0}");
    }

    private bool ContainsPendingMarriage(IEnumerable<Barterable> barters)
        => barters != null && barters
            .OfType<MarriageBarterable>()
            .Any(x => MatchesPendingPair(x.HeroBeingProposedTo, x.ProposingHero));

    private bool MatchesPendingPair(Hero first, Hero second)
    {
        if (!HasPendingContract || first == null || second == null)
            return false;
        return (string.Equals(first.StringId, _pendingMemberId, StringComparison.Ordinal) &&
                string.Equals(second.StringId, _pendingTargetId, StringComparison.Ordinal)) ||
               (string.Equals(second.StringId, _pendingMemberId, StringComparison.Ordinal) &&
                string.Equals(first.StringId, _pendingTargetId, StringComparison.Ordinal));
    }

    private bool HasPendingContract
        => !string.IsNullOrWhiteSpace(_pendingMemberId) &&
           !string.IsNullOrWhiteSpace(_pendingTargetId) &&
           !string.IsNullOrWhiteSpace(_pendingTargetClanId);

    private void RefundAndClear(string reason)
    {
        var refund = _escrowGold;
        if (refund > 0 && Hero.MainHero != null)
            Hero.MainHero.ChangeHeroGold(refund);

        KaiRuntimeLog.Write(
            "DYNASTIC_ESCROW_REFUND",
            $"reason={reason}; amount={refund}; member={_pendingMemberId ?? "none"}; target={_pendingTargetId ?? "none"}");

        _escrowGold = 0;
        ClearPendingIds();
    }

    private void ClearPendingIds()
    {
        _pendingMemberId = null;
        _pendingTargetId = null;
        _pendingTargetClanId = null;
    }

    private static string ClanPairKey(Clan first, Clan second)
        => string.CompareOrdinal(first.StringId, second.StringId) <= 0
            ? first.StringId + "|" + second.StringId
            : second.StringId + "|" + first.StringId;

    private static bool TryResolveClanPair(string key, out Clan first, out Clan second)
    {
        first = null;
        second = null;
        if (string.IsNullOrWhiteSpace(key))
            return false;
        var split = key.Split('|');
        if (split.Length != 2)
            return false;
        first = Clan.All.FirstOrDefault(c => c != null && string.Equals(c.StringId, split[0], StringComparison.Ordinal));
        second = Clan.All.FirstOrDefault(c => c != null && string.Equals(c.StringId, split[1], StringComparison.Ordinal));
        return first != null && second != null;
    }
}
