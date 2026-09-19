using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Persistent political grievances. This phase records memory only; it does not create
/// rebel factions, secession, ultimatums or civil wars.
/// </summary>
public sealed class KaiGrievanceBehavior : CampaignBehaviorBase
{
    private const string TypeSaveKey = "kaitor_grievance_v1_type";
    private const string SourceSaveKey = "kaitor_grievance_v1_source";
    private const string TargetSaveKey = "kaitor_grievance_v1_target";
    private const string SettlementSaveKey = "kaitor_grievance_v1_settlement";
    private const string SeveritySaveKey = "kaitor_grievance_v1_severity";
    private const string CreatedSaveKey = "kaitor_grievance_v1_created";
    private const string SequenceSaveKey = "kaitor_grievance_v1_sequence";

    private const float DailyDecay = 0.05f;
    private const int LandlessTierThreshold = 4;

    private Dictionary<string, string> _type = new();
    private Dictionary<string, string> _source = new();
    private Dictionary<string, string> _target = new();
    private Dictionary<string, string> _settlement = new();
    private Dictionary<string, float> _severity = new();
    private Dictionary<string, double> _created = new();
    private int _sequence;

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(TypeSaveKey, ref _type);
        dataStore.SyncData(SourceSaveKey, ref _source);
        dataStore.SyncData(TargetSaveKey, ref _target);
        dataStore.SyncData(SettlementSaveKey, ref _settlement);
        dataStore.SyncData(SeveritySaveKey, ref _severity);
        dataStore.SyncData(CreatedSaveKey, ref _created);
        dataStore.SyncData(SequenceSaveKey, ref _sequence);

        _type ??= new Dictionary<string, string>();
        _source ??= new Dictionary<string, string>();
        _target ??= new Dictionary<string, string>();
        _settlement ??= new Dictionary<string, string>();
        _severity ??= new Dictionary<string, float>();
        _created ??= new Dictionary<string, double>();
    }

    public void AddConquestClaimDenied(Clan source, Clan target, Settlement settlement)
        => AddOrStrengthen(
            "conquest_claim_denied",
            source,
            target,
            settlement,
            30f);

    public void AddNapBreach(Kingdom breaker, Kingdom victim)
    {
        if (breaker?.RulingClan == null || victim?.RulingClan == null)
            return;

        AddOrStrengthen(
            "nap_breach",
            victim.RulingClan,
            breaker.RulingClan,
            null,
            45f);
    }

    public void AddTruceBreach(Kingdom breaker, Kingdom victim)
    {
        if (breaker?.RulingClan == null || victim?.RulingClan == null)
            return;

        AddOrStrengthen(
            "truce_breach",
            victim.RulingClan,
            breaker.RulingClan,
            null,
            30f);
    }

    public void AddDynasticBreach(Kingdom breaker, Kingdom victim)
    {
        if (breaker?.RulingClan == null || victim?.RulingClan == null)
            return;

        AddOrStrengthen(
            "dynastic_promise_breach",
            victim.RulingClan,
            breaker.RulingClan,
            null,
            35f);
    }

    public int ResolveBetween(Clan first, Clan second, string reason)
    {
        if (first == null || second == null || first == second)
            return 0;

        var ids = _type.Keys
            .Where(id =>
                (string.Equals(Get(_source, id), first.StringId, StringComparison.Ordinal) &&
                 string.Equals(Get(_target, id), second.StringId, StringComparison.Ordinal)) ||
                (string.Equals(Get(_source, id), second.StringId, StringComparison.Ordinal) &&
                 string.Equals(Get(_target, id), first.StringId, StringComparison.Ordinal)))
            .ToArray();

        foreach (var id in ids)
            Resolve(id, string.IsNullOrWhiteSpace(reason) ? "mutual_settlement" : reason);

        return ids.Length;
    }

    public void ResolveLandless(Clan clan)
    {
        if (clan == null)
            return;

        foreach (var id in _type.Keys
                     .Where(id =>
                         string.Equals(_type[id], "landless_strong_house", StringComparison.Ordinal) &&
                         string.Equals(Get(_source, id), clan.StringId, StringComparison.Ordinal))
                     .ToArray())
            Resolve(id, "received_fief");
    }

    public IEnumerable<string> DescribeStatus()
    {
        if (_type.Count == 0)
        {
            yield return "Grievances: none.";
            yield break;
        }

        foreach (var id in _type.Keys
                     .OrderByDescending(id => Get(_severity, id))
                     .ThenBy(id => id, StringComparer.Ordinal)
                     .Take(30))
        {
            yield return
                $"{id}: type={Get(_type,id)}; source={Get(_source,id)}; target={Get(_target,id)}; " +
                $"settlement={Get(_settlement,id)}; severity={Get(_severity,id):0.0}; " +
                $"age={Math.Max(0d, CampaignTime.Now.ToDays - Get(_created,id)):0.0}d";
        }
    }

    private void OnDailyTick()
    {
        foreach (var id in _severity.Keys.ToArray())
        {
            var after = Math.Max(0f, Get(_severity, id) - DailyDecay);
            if (after <= 0.01f)
                Resolve(id, "decayed");
            else
                _severity[id] = after;
        }

        foreach (var kingdom in Kingdom.All.Where(k => k != null && !k.IsEliminated && k.RulingClan?.Leader != null))
        {
            var rulerClan = kingdom.RulingClan;
            var ruler = rulerClan.Leader;

            foreach (var clan in kingdom.Clans.Where(c =>
                         c != null &&
                         !c.IsEliminated &&
                         c != rulerClan &&
                         !c.IsUnderMercenaryService &&
                         c.IsNoble &&
                         c.Leader != null &&
                         c.Leader.IsAlive))
            {
                if (clan.Tier >= LandlessTierThreshold && !clan.Fiefs.Any())
                    AddOrStrengthen("landless_strong_house", clan, rulerClan, null, 18f, allowIncrease:false);

                var relation = clan.Leader.GetRelation(ruler);
                if (relation <= -30)
                {
                    var severity = Math.Min(35f, 15f + Math.Abs(relation) * 0.20f);
                    AddOrStrengthen("bad_relation_with_ruler", clan, rulerClan, null, severity, allowIncrease:false);
                }
            }
        }
    }

    private void OnHeroKilled(
        Hero victim,
        Hero killer,
        KillCharacterAction.KillCharacterActionDetail detail,
        bool showNotification)
    {
        if (victim?.Clan == null || killer?.Clan == null || victim.Clan == killer.Clan)
            return;

        if (detail != KillCharacterAction.KillCharacterActionDetail.Executed &&
            detail != KillCharacterAction.KillCharacterActionDetail.ExecutionAfterMapEvent)
            return;

        AddOrStrengthen(
            "relative_executed",
            victim.Clan,
            killer.Clan,
            null,
            60f);
    }

    private void OnSettlementOwnerChanged(
        Settlement settlement,
        bool openToClaim,
        Hero newOwner,
        Hero oldOwner,
        Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        var oldClan = oldOwner?.Clan;
        var newClan = newOwner?.Clan;
        if (settlement == null ||
            oldClan == null ||
            newClan == null ||
            oldClan == newClan ||
            oldClan.Kingdom == null ||
            oldClan.Kingdom != newClan.Kingdom)
            return;

        if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision &&
            detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByGift)
            return;

        var rulerClan = newClan.Kingdom?.RulingClan;
        if (rulerClan == null || oldClan == rulerClan)
            return;

        AddOrStrengthen(
            "fief_removed",
            oldClan,
            rulerClan,
            settlement,
            settlement.IsTown ? 35f : 25f);
    }

    private void AddOrStrengthen(
        string type,
        Clan source,
        Clan target,
        Settlement settlement,
        float severity,
        bool allowIncrease = true)
    {
        if (source == null || target == null || source == target || severity <= 0f)
            return;

        var existing = _type.Keys.FirstOrDefault(id =>
            string.Equals(Get(_type,id), type, StringComparison.Ordinal) &&
            string.Equals(Get(_source,id), source.StringId, StringComparison.Ordinal) &&
            string.Equals(Get(_target,id), target.StringId, StringComparison.Ordinal) &&
            string.Equals(Get(_settlement,id), settlement?.StringId ?? "none", StringComparison.Ordinal));

        if (existing != null)
        {
            if (allowIncrease)
                _severity[existing] = Math.Min(100f, Math.Max(Get(_severity,existing), severity) + Math.Min(10f, severity * 0.20f));
            else
                _severity[existing] = Math.Max(Get(_severity,existing), severity);
            return;
        }

        var id = "grievance_" + (++_sequence);
        _type[id] = type;
        _source[id] = source.StringId;
        _target[id] = target.StringId;
        _settlement[id] = settlement?.StringId ?? "none";
        _severity[id] = Math.Max(0f, Math.Min(100f, severity));
        _created[id] = CampaignTime.Now.ToDays;

        KaiRuntimeLog.Write(
            "GRIEVANCE_CREATED",
            $"id={id}; type={type}; source={source.StringId}; target={target.StringId}; " +
            $"settlement={settlement?.StringId ?? "none"}; severity={_severity[id]:0.0}");
    }

    private void Resolve(string id, string reason)
    {
        if (string.IsNullOrWhiteSpace(id) || !_type.ContainsKey(id))
            return;

        KaiRuntimeLog.Write(
            "GRIEVANCE_RESOLVED",
            $"id={id}; type={Get(_type,id)}; source={Get(_source,id)}; target={Get(_target,id)}; reason={reason}");

        _type.Remove(id);
        _source.Remove(id);
        _target.Remove(id);
        _settlement.Remove(id);
        _severity.Remove(id);
        _created.Remove(id);
    }

    private static string Get(Dictionary<string,string> map, string key)
        => map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : "none";

    private static float Get(Dictionary<string,float> map, string key)
        => map.TryGetValue(key, out var value) ? Math.Max(0f, value) : 0f;

    private static double Get(Dictionary<string,double> map, string key)
        => map.TryGetValue(key, out var value) ? value : CampaignTime.Now.ToDays;
}
