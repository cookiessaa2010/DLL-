using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

public sealed class KaiDiplomacyBehavior : CampaignBehaviorBase
{
    public const int DefaultNapDays = 90;
    public const int NapProposalInfluenceCost = 150;
    public const int NapBreakInfluenceCost = 100;
    public const int AiInfluenceReserve = 300;

    private const int CurrentSaveSchemaVersion = 1;
    private const int NaturalExpiryTrustBonus = 5;
    private const int VoluntaryBreakTrustPenalty = 10;
    private const int WarBreachTrustPenalty = 30;
    private const int NaturalExpiryRelationBonus = 3;
    private const int VoluntaryBreakRelationPenalty = 10;
    private const int WarBreachRelationPenalty = 30;
    private const int VoluntaryBreakCooldownDays = 10;
    private const int WarBreachCooldownDays = 30;

    private Dictionary<string, double> _nonAggressionExpiryDays = new();
    private Dictionary<string, int> _breachCounts = new();
    private Dictionary<string, int> _diplomaticTrust = new();
    private Dictionary<string, double> _napCooldownExpiryDays = new();
    private int _saveSchemaVersion;
    private bool _saveSchemaCompatible = true;
    private bool _runtimeEnabled;

    public bool RuntimeEnabled => _runtimeEnabled;
    public int SaveSchemaVersion => _saveSchemaVersion;
    public bool SaveSchemaCompatible => _saveSchemaCompatible;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
        CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("kaitor_diplomacy_nap_expiry_days_v2", ref _nonAggressionExpiryDays);
        dataStore.SyncData("kaitor_diplomacy_breach_counts", ref _breachCounts);
        dataStore.SyncData("kaitor_diplomacy_trust", ref _diplomaticTrust);
        dataStore.SyncData("kaitor_diplomacy_nap_cooldown_expiry_days", ref _napCooldownExpiryDays);
        dataStore.SyncData("kaitor_diplomacy_save_schema", ref _saveSchemaVersion);

        _nonAggressionExpiryDays ??= new Dictionary<string, double>();
        _breachCounts ??= new Dictionary<string, int>();
        _diplomaticTrust ??= new Dictionary<string, int>();
        _napCooldownExpiryDays ??= new Dictionary<string, double>();
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        ValidateAndMigrateSaveSchema();
        if (!_saveSchemaCompatible)
        {
            _runtimeEnabled = false;
            InformationManager.DisplayMessage(new InformationMessage(
                "Дополнительные дипломатические действия отключены: сохранение создано более новой версией системы договоров."));
            return;
        }

        NormalizeLoadedState();
        _runtimeEnabled = TorCompatibilityGate.TryValidate(out _);
        if (_runtimeEnabled)
        {
            CleanupExpiredPacts();
            CleanupExpiredCooldowns();
        }
        else
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "Дополнительные дипломатические действия сейчас недоступны."));
        }
    }

    private void OnDailyTick()
    {
        if (!_runtimeEnabled) return;
        CleanupExpiredPacts();
        CleanupExpiredCooldowns();
    }

    private void OnWarDeclared(IFaction firstFaction, IFaction secondFaction, DeclareWarAction.DeclareWarDetail detail)
    {
        if (!_runtimeEnabled || firstFaction is not Kingdom first || secondFaction is not Kingdom second)
            return;

        ApplyWarBreach(first, second, detail.ToString(), forced: false);
    }

    public bool ForceBreakNonAggressionPactForWar(
        Kingdom aggressor,
        Kingdom target,
        string reason)
    {
        if (!_runtimeEnabled ||
            aggressor == null ||
            target == null ||
            !IsNonAggressionPactActive(aggressor, target))
            return false;

        ApplyWarBreach(
            aggressor,
            target,
            string.IsNullOrWhiteSpace(reason) ? "forced" : reason,
            forced: true);
        return true;
    }

    private void ApplyWarBreach(
        Kingdom aggressor,
        Kingdom target,
        string reason,
        bool forced)
    {
        if (aggressor == null || target == null || !IsNonAggressionPactActive(aggressor, target))
            return;

        var key = TreatyKey.For(aggressor, target);
        _nonAggressionExpiryDays.Remove(key);
        _breachCounts.TryGetValue(key, out var current);
        _breachCounts[key] = current + 1;
        ChangeTrust(key, -WarBreachTrustPenalty);
        ChangeRulerRelation(aggressor, target, -WarBreachRelationPenalty);
        _napCooldownExpiryDays[key] = CampaignTime.Now.ToDays + WarBreachCooldownDays;

        Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>()
            ?.RecordTreatyBreach(aggressor, target, reason);
        Campaign.Current?.GetCampaignBehavior<KaiGrievanceBehavior>()
            ?.AddNapBreach(aggressor, target);

        KaiRuntimeLog.Write(
            forced ? "NAP_FORCED_BREACH" : "NAP_WAR_BREACH",
            $"source={aggressor.StringId}; target={target.StringId}; reason={reason}; " +
            $"trust=-{WarBreachTrustPenalty}; relation=-{WarBreachRelationPenalty}; cooldown={WarBreachCooldownDays}");

        InformationManager.DisplayMessage(new InformationMessage(
            $"Пакт о ненападении между {aggressor.Name} и {target.Name} нарушен объявлением войны. " +
            $"Отношения правящих домов серьёзно ухудшились. Новый пакт будет недоступен {WarBreachCooldownDays} дней."));
    }

    private void OnPeaceMade(IFaction firstFaction, IFaction secondFaction, MakePeaceAction.MakePeaceDetail detail)
    {
        // Peace stays fully owned by Bannerlord/TOR. A NAP is never created automatically after peace.
    }

    public bool CanCreateNonAggressionPact(Kingdom first, Kingdom second, int durationDays, out string reason)
    {
        reason = string.Empty;
        if (!_runtimeEnabled)
        {
            reason = "Дополнительные дипломатические действия сейчас недоступны.";
            return false;
        }
        if (first == null || second == null)
        {
            reason = "Необходимо выбрать две державы.";
            return false;
        }
        if (ReferenceEquals(first, second))
        {
            reason = "Держава не может заключить пакт сама с собой.";
            return false;
        }
        if (first.IsEliminated || second.IsEliminated)
        {
            reason = "Уничтоженные державы не могут заключать договоры.";
            return false;
        }
        if (durationDays < 1 || durationDays > 365)
        {
            reason = "Срок договора должен составлять от 1 до 365 дней.";
            return false;
        }
        if (FactionManager.IsAtWarAgainstFaction(first, second))
        {
            reason = "Державы находятся в состоянии войны. Сначала необходимо заключить мир.";
            return false;
        }
        if (first.IsAllyWith(second))
        {
            reason = "Державы уже связаны более тесным союзным договором.";
            return false;
        }

        var key = TreatyKey.For(first, second);
        ReconcilePair(key);
        if (IsNonAggressionPactActive(first, second))
        {
            reason = $"Пакт о ненападении уже действует ещё {GetRemainingDays(first, second)} дн.";
            return false;
        }
        var cooldown = GetCooldownRemainingDays(key);
        if (cooldown > 0)
        {
            reason = $"После прежнего разрыва или нарушения новый пакт будет доступен через {cooldown} дн.";
            return false;
        }
        if (!TorAllowsDiplomaticCompatibility(first, second, out var worldReason))
        {
            reason = string.IsNullOrWhiteSpace(worldReason)
                ? "Правила мира не допускают мирных договорённостей между этими державами."
                : worldReason;
            return false;
        }
        return true;
    }

    public bool TryCreateNonAggressionPact(Kingdom first, Kingdom second, int durationDays, out string reason)
    {
        if (!CanCreateNonAggressionPact(first, second, durationDays, out reason)) return false;
        _nonAggressionExpiryDays[TreatyKey.For(first, second)] = CampaignTime.Now.ToDays + durationDays;
        reason = $"Пакт о ненападении заключён на {durationDays} дней.";
        return true;
    }

    public int GetNapAcceptanceScore(Kingdom proposer, Kingdom target)
    {
        if (proposer == null || target == null || proposer.IsEliminated || target.IsEliminated) return -100;

        var score = GetTrust(proposer, target);
        if (proposer.RulingClan != null && target.RulingClan != null)
            score += target.RulingClan.GetRelationWithClan(proposer.RulingClan);

        if (proposer.Culture == target.Culture) score += 10;

        var dynastic = Campaign.Current?.GetCampaignBehavior<KaiDynasticMarriageBehavior>();
        if (dynastic != null)
            score += (int)Math.Round(dynastic.GetDynasticStrength(proposer, target) * 0.50f);

        var threat = Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>();
        if (threat != null)
            score -= (int)Math.Round(threat.GetNapPenalty(proposer));

        var commonEnemies = proposer.FactionsAtWarWith
            .OfType<Kingdom>()
            .Count(enemy => target.FactionsAtWarWith.Contains(enemy));
        score += Math.Min(30, commonEnemies * 10);

        if (target.FactionsAtWarWith.Count() >= 2) score += 10;

        try
        {
            if (Campaign.Current?.Models?.AllianceModel != null && target.RulingClan != null)
            {
                var affinity = Campaign.Current.Models.AllianceModel
                    .GetScoreOfStartingAlliance(target, proposer, target.RulingClan, out _, false).ResultNumber;
                score += (int)Math.Round((affinity - 50f) * 0.3f);
            }
        }
        catch
        {
            // Optional TOR affinity input only; the NAP remains functional without it.
        }

        return Math.Max(-100, Math.Min(100, score));
    }

    public float GetNapCouncilSupportScore(Kingdom proposer, Kingdom target, Clan evaluatingClan)
    {
        if (proposer == null || target == null || evaluatingClan == null) return 0f;
        var score = 50f + GetNapAcceptanceScore(proposer, target) * 0.35f;
        if (target.RulingClan != null)
            score += target.RulingClan.GetRelationWithClan(evaluatingClan) * 0.25f;
        return Math.Max(0f, Math.Min(100f, score));
    }

    public float GetNapBreakCouncilSupportScore(Kingdom first, Kingdom second, Clan evaluatingClan)
    {
        if (first == null || second == null || evaluatingClan == null) return 0f;
        var score = 50f - GetTrust(first, second) * 0.30f;
        if (second.RulingClan != null)
            score -= second.RulingClan.GetRelationWithClan(evaluatingClan) * 0.20f;
        return Math.Max(0f, Math.Min(100f, score));
    }

    public bool BreakNonAggressionPact(Kingdom first, Kingdom second)
    {
        if (!_runtimeEnabled || first == null || second == null) return false;
        var key = TreatyKey.For(first, second);
        ReconcilePair(key);
        if (!_nonAggressionExpiryDays.Remove(key)) return false;

        ChangeTrust(key, -VoluntaryBreakTrustPenalty);
        ChangeRulerRelation(first, second, -VoluntaryBreakRelationPenalty);
        _napCooldownExpiryDays[key] = CampaignTime.Now.ToDays + VoluntaryBreakCooldownDays;
        return true;
    }

    public bool IsNonAggressionPactActive(Kingdom first, Kingdom second)
    {
        if (first == null || second == null) return false;
        return _nonAggressionExpiryDays.TryGetValue(TreatyKey.For(first, second), out var expiryDay) && expiryDay > CampaignTime.Now.ToDays;
    }

    public int GetRemainingDays(Kingdom first, Kingdom second)
    {
        if (!IsNonAggressionPactActive(first, second)) return 0;
        var expiry = _nonAggressionExpiryDays[TreatyKey.For(first, second)];
        return Math.Max(1, (int)Math.Ceiling(expiry - CampaignTime.Now.ToDays));
    }

    public int GetBreachCount(Kingdom first, Kingdom second)
        => first == null || second == null ? 0 : (_breachCounts.TryGetValue(TreatyKey.For(first, second), out var count) ? count : 0);

    public int GetTrust(Kingdom first, Kingdom second)
        => first == null || second == null ? 0 : GetTrust(TreatyKey.For(first, second));

    public int GetNapCooldownRemainingDays(Kingdom first, Kingdom second)
        => first == null || second == null ? 0 : GetCooldownRemainingDays(TreatyKey.For(first, second));

    /// <summary>
    /// Additive hook for systems such as Dynastic Bond. It modifies the same trust
    /// dictionary used by NAP; no parallel diplomacy state is created.
    /// </summary>
    public void AdjustTrust(Kingdom first, Kingdom second, int delta)
    {
        if (!_runtimeEnabled || first == null || second == null || first == second || delta == 0)
            return;
        ChangeTrust(TreatyKey.For(first, second), delta);
    }

    /// <summary>
    /// Reuses the existing NAP backend for a treaty granted by another accepted
    /// contract. If a pact already exists, it is only extended when necessary.
    /// </summary>
    public bool EnsureNonAggressionPactAtLeast(Kingdom first, Kingdom second, int durationDays, out string reason)
    {
        reason = string.Empty;
        if (!_runtimeEnabled || first == null || second == null || durationDays < 1)
        {
            reason = "Пакт сейчас недоступен.";
            return false;
        }

        var key = TreatyKey.For(first, second);
        ReconcilePair(key);
        if (IsNonAggressionPactActive(first, second))
        {
            var requestedExpiry = CampaignTime.Now.ToDays + durationDays;
            if (_nonAggressionExpiryDays.TryGetValue(key, out var currentExpiry) && currentExpiry < requestedExpiry)
                _nonAggressionExpiryDays[key] = requestedExpiry;
            reason = $"Действующий пакт сохранён как минимум на {durationDays} дней.";
            return true;
        }

        return TryCreateNonAggressionPact(first, second, durationDays, out reason);
    }

    public string DescribeSaveCompatibility()
    {
        var status = _saveSchemaCompatible ? "PASS" : "BLOCKED";
        return $"KaiTOR save compatibility: {status}; schema={_saveSchemaVersion}; supported={CurrentSaveSchemaVersion}; state=primitive dictionaries only.";
    }

    public IEnumerable<string> DescribeActivePacts()
    {
        var currentDay = CampaignTime.Now.ToDays;
        foreach (var pair in _nonAggressionExpiryDays.Where(x => x.Value > currentDay).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!TreatyKey.TrySplit(pair.Key, out var firstId, out var secondId)) continue;
            var remaining = Math.Max(1, (int)Math.Ceiling(pair.Value - currentDay));
            _breachCounts.TryGetValue(pair.Key, out var breaches);
            yield return $"{firstId} <-> {secondId}: NAP, {remaining} day(s), trust {GetTrust(pair.Key)}, breaches {breaches}";
        }
    }

    public IEnumerable<string> DescribeDiplomaticHistory()
    {
        var keys = new HashSet<string>(_diplomaticTrust.Keys, StringComparer.Ordinal);
        keys.UnionWith(_breachCounts.Keys);
        keys.UnionWith(_napCooldownExpiryDays.Keys);
        foreach (var key in keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!TreatyKey.TrySplit(key, out var firstId, out var secondId)) continue;
            _breachCounts.TryGetValue(key, out var breaches);
            yield return $"{firstId} <-> {secondId}: trust {GetTrust(key)}, breaches {breaches}, NAP cooldown {GetCooldownRemainingDays(key)} day(s)";
        }
    }

    private void ValidateAndMigrateSaveSchema()
    {
        if (_saveSchemaVersion < 0) _saveSchemaVersion = 0;
        if (_saveSchemaVersion > CurrentSaveSchemaVersion)
        {
            _saveSchemaCompatible = false;
            return;
        }
        if (_saveSchemaVersion == 0) _saveSchemaVersion = CurrentSaveSchemaVersion;
        _saveSchemaCompatible = true;
    }

    private void NormalizeLoadedState()
    {
        var knownKingdomIds = new HashSet<string>(Kingdom.All.Where(x => x != null).Select(x => x.StringId), StringComparer.Ordinal);
        var allKeys = new HashSet<string>(_nonAggressionExpiryDays.Keys, StringComparer.Ordinal);
        allKeys.UnionWith(_breachCounts.Keys);
        allKeys.UnionWith(_diplomaticTrust.Keys);
        allKeys.UnionWith(_napCooldownExpiryDays.Keys);

        foreach (var key in allKeys)
        {
            if (!TreatyKey.TrySplit(key, out var firstId, out var secondId) ||
                string.Equals(firstId, secondId, StringComparison.Ordinal) ||
                !knownKingdomIds.Contains(firstId) || !knownKingdomIds.Contains(secondId))
                RemovePairState(key);
        }

        foreach (var pair in _nonAggressionExpiryDays.ToArray())
            if (double.IsNaN(pair.Value) || double.IsInfinity(pair.Value) || pair.Value <= 0d) _nonAggressionExpiryDays.Remove(pair.Key);
        foreach (var pair in _napCooldownExpiryDays.ToArray())
            if (double.IsNaN(pair.Value) || double.IsInfinity(pair.Value) || pair.Value <= 0d) _napCooldownExpiryDays.Remove(pair.Key);
        foreach (var pair in _breachCounts.ToArray())
            if (pair.Value <= 0) _breachCounts.Remove(pair.Key);
        foreach (var pair in _diplomaticTrust.ToArray())
        {
            var clamped = Math.Max(-100, Math.Min(100, pair.Value));
            if (clamped == 0) _diplomaticTrust.Remove(pair.Key); else _diplomaticTrust[pair.Key] = clamped;
        }
    }

    private void RemovePairState(string key)
    {
        _nonAggressionExpiryDays.Remove(key);
        _breachCounts.Remove(key);
        _diplomaticTrust.Remove(key);
        _napCooldownExpiryDays.Remove(key);
    }

    private static bool TorAllowsDiplomaticCompatibility(Kingdom first, Kingdom second, out string reason)
    {
        reason = string.Empty;
        var permissionModel = Campaign.Current?.Models?.KingdomDecisionPermissionModel;
        if (permissionModel == null)
        {
            reason = "Дипломатические правила мира сейчас недоступны.";
            return false;
        }

        if (!permissionModel.IsPeaceDecisionAllowedBetweenKingdoms(first, second, out TextObject worldReason))
        {
            reason = string.IsNullOrWhiteSpace(worldReason?.ToString())
                ? "Правила мира не допускают мирных договорённостей между этими державами."
                : worldReason.ToString();
            return false;
        }
        return true;
    }

    private void ReconcilePair(string key)
    {
        if (_nonAggressionExpiryDays.TryGetValue(key, out var expiryDay) && expiryDay <= CampaignTime.Now.ToDays)
            ExpirePactNaturally(key);
        if (_napCooldownExpiryDays.TryGetValue(key, out var cooldownExpiry) && cooldownExpiry <= CampaignTime.Now.ToDays)
            _napCooldownExpiryDays.Remove(key);
    }

    private void CleanupExpiredPacts()
    {
        if (_nonAggressionExpiryDays.Count == 0) return;
        var currentDay = CampaignTime.Now.ToDays;
        foreach (var key in _nonAggressionExpiryDays.Where(x => x.Value <= currentDay).Select(x => x.Key).ToArray())
            ExpirePactNaturally(key);
    }

    private void ExpirePactNaturally(string key)
    {
        if (!_nonAggressionExpiryDays.Remove(key)) return;

        var trustBonus = NaturalExpiryTrustBonus;
        if (TryResolveKingdomPair(key, out var first, out var second))
        {
            var dynastic = Campaign.Current?.GetCampaignBehavior<KaiDynasticMarriageBehavior>();
            if (dynastic != null)
                trustBonus += Math.Min(4, (int)Math.Round(dynastic.GetDynasticStrength(first, second) / 25f));

            ChangeRulerRelation(first, second, NaturalExpiryRelationBonus);
        }

        ChangeTrust(key, trustBonus);
    }

    private void CleanupExpiredCooldowns()
    {
        if (_napCooldownExpiryDays.Count == 0) return;
        var currentDay = CampaignTime.Now.ToDays;
        foreach (var key in _napCooldownExpiryDays.Where(x => x.Value <= currentDay).Select(x => x.Key).ToArray())
            _napCooldownExpiryDays.Remove(key);
    }

    private int GetCooldownRemainingDays(string key)
    {
        if (!_napCooldownExpiryDays.TryGetValue(key, out var expiryDay)) return 0;
        var remaining = expiryDay - CampaignTime.Now.ToDays;
        return remaining <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(remaining));
    }

    private int GetTrust(string key) => _diplomaticTrust.TryGetValue(key, out var trust) ? trust : 0;

    private void ChangeTrust(string key, int delta)
    {
        var next = Math.Max(-100, Math.Min(100, GetTrust(key) + delta));
        if (next == 0) _diplomaticTrust.Remove(key); else _diplomaticTrust[key] = next;
    }

    private static void ChangeRulerRelation(Kingdom first, Kingdom second, int delta)
    {
        if (delta == 0 || first?.Leader == null || second?.Leader == null) return;
        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(first.Leader, second.Leader, delta, true);
    }

    private static bool TryResolveKingdomPair(string key, out Kingdom first, out Kingdom second)
    {
        first = null;
        second = null;
        if (!TreatyKey.TrySplit(key, out var firstId, out var secondId)) return false;
        first = Kingdom.All.FirstOrDefault(x => x != null && x.StringId == firstId);
        second = Kingdom.All.FirstOrDefault(x => x != null && x.StringId == secondId);
        return first != null && second != null;
    }
}
