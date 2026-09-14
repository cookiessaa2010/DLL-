using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

public sealed class KaiDiplomacyBehavior : CampaignBehaviorBase
{
    private const int NaturalExpiryTrustBonus = 5;
    private const int VoluntaryBreakTrustPenalty = 10;
    private const int WarBreachTrustPenalty = 30;
    private const int VoluntaryBreakCooldownDays = 10;
    private const int WarBreachCooldownDays = 30;

    private Dictionary<string, double> _nonAggressionExpiryDays = new();
    private Dictionary<string, int> _breachCounts = new();
    private Dictionary<string, int> _diplomaticTrust = new();
    private Dictionary<string, double> _napCooldownExpiryDays = new();
    private bool _runtimeEnabled;

    public bool RuntimeEnabled => _runtimeEnabled;

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

        _nonAggressionExpiryDays ??= new Dictionary<string, double>();
        _breachCounts ??= new Dictionary<string, int>();
        _diplomaticTrust ??= new Dictionary<string, int>();
        _napCooldownExpiryDays ??= new Dictionary<string, double>();
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        _runtimeEnabled = TorCompatibilityGate.TryValidate(out var reason);
        if (_runtimeEnabled)
        {
            CleanupExpiredPacts();
            CleanupExpiredCooldowns();
            InformationManager.DisplayMessage(new InformationMessage(
                "KaiTOR Diplomacy: TOR 1.3.15 compatibility gate PASS."));
        }
        else
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "KaiTOR Diplomacy disabled: " + reason));
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
        if (!_runtimeEnabled) return;
        if (firstFaction is not Kingdom first || secondFaction is not Kingdom second) return;

        var key = TreatyKey.For(first, second);
        if (!IsNonAggressionPactActive(first, second)) return;

        _nonAggressionExpiryDays.Remove(key);
        _breachCounts.TryGetValue(key, out var current);
        _breachCounts[key] = current + 1;
        ChangeTrust(key, -WarBreachTrustPenalty);
        _napCooldownExpiryDays[key] = CampaignTime.Now.ToDays + WarBreachCooldownDays;

        InformationManager.DisplayMessage(new InformationMessage(
            $"KaiTOR Diplomacy: non-aggression pact broken by war between {first.Name} and {second.Name}. " +
            $"Trust -{WarBreachTrustPenalty}; new NAP blocked for {WarBreachCooldownDays} days."));
    }

    private void OnPeaceMade(IFaction firstFaction, IFaction secondFaction, MakePeaceAction.MakePeaceDetail detail)
    {
        // TOR owns peace rules. We deliberately do not auto-create a pact here.
        // A treaty is an explicit layer above TOR peace, never a replacement for it.
    }

    public bool TryCreateNonAggressionPact(Kingdom first, Kingdom second, int durationDays, out string reason)
    {
        reason = string.Empty;

        if (!_runtimeEnabled)
        {
            reason = "KaiTOR Diplomacy runtime is disabled by the TOR compatibility gate.";
            return false;
        }

        if (first == null || second == null)
        {
            reason = "Both kingdoms are required.";
            return false;
        }

        if (ReferenceEquals(first, second))
        {
            reason = "A kingdom cannot make a pact with itself.";
            return false;
        }

        if (first.IsEliminated || second.IsEliminated)
        {
            reason = "Eliminated kingdoms cannot sign treaties.";
            return false;
        }

        if (durationDays < 1 || durationDays > 365)
        {
            reason = "Treaty duration must be between 1 and 365 days.";
            return false;
        }

        if (FactionManager.IsAtWarAgainstFaction(first, second))
        {
            reason = "The kingdoms are currently at war. TOR must resolve peace first.";
            return false;
        }

        var key = TreatyKey.For(first, second);
        if (IsNapCooldownActive(key, out var cooldownDays))
        {
            reason = $"A new non-aggression pact is blocked for another {cooldownDays} day(s) after the previous breach.";
            return false;
        }

        if (!TorAllowsDiplomaticCompatibility(first, second, out var torReason))
        {
            reason = string.IsNullOrWhiteSpace(torReason)
                ? "TOR diplomacy rules reject this pairing."
                : torReason;
            return false;
        }

        _nonAggressionExpiryDays[key] = CampaignTime.Now.ToDays + durationDays;
        reason = $"Non-aggression pact active for {durationDays} days. Current trust: {GetTrust(first, second)}.";
        return true;
    }

    public bool BreakNonAggressionPact(Kingdom first, Kingdom second)
    {
        if (first == null || second == null) return false;

        var key = TreatyKey.For(first, second);
        if (!_nonAggressionExpiryDays.Remove(key)) return false;

        ChangeTrust(key, -VoluntaryBreakTrustPenalty);
        _napCooldownExpiryDays[key] = CampaignTime.Now.ToDays + VoluntaryBreakCooldownDays;
        return true;
    }

    public bool IsNonAggressionPactActive(Kingdom first, Kingdom second)
    {
        if (first == null || second == null) return false;

        var key = TreatyKey.For(first, second);
        if (!_nonAggressionExpiryDays.TryGetValue(key, out var expiryDay)) return false;

        if (expiryDay <= CampaignTime.Now.ToDays)
        {
            ExpirePactNaturally(key);
            return false;
        }

        return true;
    }

    public int GetRemainingDays(Kingdom first, Kingdom second)
    {
        if (!IsNonAggressionPactActive(first, second)) return 0;
        var expiry = _nonAggressionExpiryDays[TreatyKey.For(first, second)];
        return Math.Max(1, (int)Math.Ceiling(expiry - CampaignTime.Now.ToDays));
    }

    public int GetBreachCount(Kingdom first, Kingdom second)
    {
        if (first == null || second == null) return 0;
        return _breachCounts.TryGetValue(TreatyKey.For(first, second), out var count) ? count : 0;
    }

    public int GetTrust(Kingdom first, Kingdom second)
    {
        if (first == null || second == null) return 0;
        return GetTrust(TreatyKey.For(first, second));
    }

    public int GetNapCooldownRemainingDays(Kingdom first, Kingdom second)
    {
        if (first == null || second == null) return 0;
        var key = TreatyKey.For(first, second);
        return IsNapCooldownActive(key, out var days) ? days : 0;
    }

    public IEnumerable<string> DescribeActivePacts()
    {
        CleanupExpiredPacts();
        CleanupExpiredCooldowns();

        foreach (var pair in _nonAggressionExpiryDays.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!TreatyKey.TrySplit(pair.Key, out var firstId, out var secondId)) continue;
            var remaining = Math.Max(1, (int)Math.Ceiling(pair.Value - CampaignTime.Now.ToDays));
            var trust = GetTrust(pair.Key);
            _breachCounts.TryGetValue(pair.Key, out var breaches);
            yield return $"{firstId} <-> {secondId}: NAP, {remaining} day(s), trust {trust}, breaches {breaches}";
        }
    }

    public IEnumerable<string> DescribeDiplomaticHistory()
    {
        CleanupExpiredCooldowns();

        var keys = new HashSet<string>(_diplomaticTrust.Keys, StringComparer.Ordinal);
        keys.UnionWith(_breachCounts.Keys);
        keys.UnionWith(_napCooldownExpiryDays.Keys);

        foreach (var key in keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!TreatyKey.TrySplit(key, out var firstId, out var secondId)) continue;
            _breachCounts.TryGetValue(key, out var breaches);
            var cooldown = GetCooldownRemainingDays(key);
            yield return $"{firstId} <-> {secondId}: trust {GetTrust(key)}, breaches {breaches}, NAP cooldown {cooldown} day(s)";
        }
    }

    private static bool TorAllowsDiplomaticCompatibility(Kingdom first, Kingdom second, out string reason)
    {
        reason = string.Empty;
        var permissionModel = Campaign.Current?.Models?.KingdomDecisionPermissionModel;
        if (permissionModel == null)
        {
            reason = "TOR kingdom decision permission model is unavailable.";
            return false;
        }

        if (!permissionModel.IsStartAllianceDecisionAllowedBetweenKingdoms(first, second, out TextObject torReason))
        {
            reason = torReason?.ToString() ?? string.Empty;
            return false;
        }

        return true;
    }

    private void CleanupExpiredPacts()
    {
        if (_nonAggressionExpiryDays.Count == 0) return;
        var currentDay = CampaignTime.Now.ToDays;
        var expired = _nonAggressionExpiryDays
            .Where(pair => pair.Value <= currentDay)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in expired)
        {
            ExpirePactNaturally(key);
        }
    }

    private void ExpirePactNaturally(string key)
    {
        if (!_nonAggressionExpiryDays.Remove(key)) return;
        ChangeTrust(key, NaturalExpiryTrustBonus);
    }

    private void CleanupExpiredCooldowns()
    {
        if (_napCooldownExpiryDays.Count == 0) return;
        var currentDay = CampaignTime.Now.ToDays;
        var expired = _napCooldownExpiryDays
            .Where(pair => pair.Value <= currentDay)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in expired)
        {
            _napCooldownExpiryDays.Remove(key);
        }
    }

    private bool IsNapCooldownActive(string key, out int remainingDays)
    {
        remainingDays = GetCooldownRemainingDays(key);
        return remainingDays > 0;
    }

    private int GetCooldownRemainingDays(string key)
    {
        if (!_napCooldownExpiryDays.TryGetValue(key, out var expiryDay)) return 0;
        var remaining = expiryDay - CampaignTime.Now.ToDays;
        if (remaining <= 0)
        {
            _napCooldownExpiryDays.Remove(key);
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(remaining));
    }

    private int GetTrust(string key)
        => _diplomaticTrust.TryGetValue(key, out var trust) ? trust : 0;

    private void ChangeTrust(string key, int delta)
    {
        var next = Math.Max(-100, Math.Min(100, GetTrust(key) + delta));
        if (next == 0)
        {
            _diplomaticTrust.Remove(key);
        }
        else
        {
            _diplomaticTrust[key] = next;
        }
    }
}
