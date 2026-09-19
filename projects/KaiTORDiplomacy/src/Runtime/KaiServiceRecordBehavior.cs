using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Read-only career ledger layered on TOR ServeAsAHireling.
/// It never changes TOR service state, menus, battle flow or rewards.
/// </summary>
public sealed class KaiServiceRecordBehavior : CampaignBehaviorBase
{
    private const string LordSaveKey = "kaitor_service_v1_lord";
    private const string FactionSaveKey = "kaitor_service_v1_faction";
    private const string StartSaveKey = "kaitor_service_v1_start";
    private const string EndSaveKey = "kaitor_service_v1_end";
    private const string DurationSaveKey = "kaitor_service_v1_duration";
    private const string BattlesSaveKey = "kaitor_service_v1_battles";
    private const string VictoriesSaveKey = "kaitor_service_v1_victories";
    private const string CareerSaveKey = "kaitor_service_v1_career";
    private const string ResultSaveKey = "kaitor_service_v1_result";
    private const string CurrentSaveKey = "kaitor_service_v1_current";
    private const string SequenceSaveKey = "kaitor_service_v1_sequence";
    private const string PendingDesertionSaveKey = "kaitor_service_v1_pending_desertion";

    private const float TorMinimumHonourableServiceDays = 25f;

    private Dictionary<string, string> _lord = new();
    private Dictionary<string, string> _faction = new();
    private Dictionary<string, double> _start = new();
    private Dictionary<string, double> _end = new();
    private Dictionary<string, float> _duration = new();
    private Dictionary<string, int> _battles = new();
    private Dictionary<string, int> _victories = new();
    private Dictionary<string, string> _career = new();
    private Dictionary<string, string> _result = new();

    private string _currentId;
    private int _sequence;
    private bool _lastEnlisted;
    private bool _pendingDesertion;

    public override void RegisterEvents()
    {
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, _ => ReconcileState());
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => ReconcileState());
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(LordSaveKey, ref _lord);
        dataStore.SyncData(FactionSaveKey, ref _faction);
        dataStore.SyncData(StartSaveKey, ref _start);
        dataStore.SyncData(EndSaveKey, ref _end);
        dataStore.SyncData(DurationSaveKey, ref _duration);
        dataStore.SyncData(BattlesSaveKey, ref _battles);
        dataStore.SyncData(VictoriesSaveKey, ref _victories);
        dataStore.SyncData(CareerSaveKey, ref _career);
        dataStore.SyncData(ResultSaveKey, ref _result);
        dataStore.SyncData(CurrentSaveKey, ref _currentId);
        dataStore.SyncData(SequenceSaveKey, ref _sequence);
        dataStore.SyncData(PendingDesertionSaveKey, ref _pendingDesertion);

        _lord ??= new Dictionary<string, string>();
        _faction ??= new Dictionary<string, string>();
        _start ??= new Dictionary<string, double>();
        _end ??= new Dictionary<string, double>();
        _duration ??= new Dictionary<string, float>();
        _battles ??= new Dictionary<string, int>();
        _victories ??= new Dictionary<string, int>();
        _career ??= new Dictionary<string, string>();
        _result ??= new Dictionary<string, string>();
    }

    public IEnumerable<string> DescribeStatus()
    {
        if (string.IsNullOrWhiteSpace(_currentId) && _lord.Count == 0)
        {
            yield return "Service record: no recorded TOR hireling service.";
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(_currentId) && _lord.ContainsKey(_currentId))
            yield return Format(_currentId, true);

        foreach (var id in _lord.Keys
                     .Where(id => !string.Equals(id, _currentId, StringComparison.Ordinal))
                     .OrderByDescending(id => _end.TryGetValue(id, out var value) ? value : 0d)
                     .Take(10))
            yield return Format(id, false);
    }

    public void MarkLeavingService(bool desertion)
    {
        if (string.IsNullOrWhiteSpace(_currentId))
            return;

        RefreshCurrent();
        _pendingDesertion = desertion;

        KaiRuntimeLog.Write(
            "SERVICE_RECORD_LEAVE_MARK",
            $"id={_currentId}; desertion={desertion}; duration={Get(_duration, _currentId):0.0}");
    }

    public float GetTotalHonourableServiceDays()
        => _duration
            .Where(x => string.Equals(Get(_result, x.Key), "honourable", StringComparison.Ordinal))
            .Sum(x => Math.Max(0f, x.Value));

    private void OnHourlyTick()
    {
        var enlisted = TorHirelingBridge.IsEnlisted();

        if (enlisted && string.IsNullOrWhiteSpace(_currentId))
            StartRecord(recovered: _lastEnlisted == false && TorHirelingBridge.GetDurationDays() > 0.5f);

        if (enlisted && !string.IsNullOrWhiteSpace(_currentId))
            RefreshCurrent();

        if (!enlisted && !string.IsNullOrWhiteSpace(_currentId))
            FinishCurrent();

        _lastEnlisted = enlisted;
    }

    private void OnMapEventEnded(MapEvent mapEvent)
    {
        if (mapEvent == null ||
            !mapEvent.IsPlayerMapEvent ||
            string.IsNullOrWhiteSpace(_currentId) ||
            !TorHirelingBridge.IsEnlisted())
            return;

        _battles[_currentId] = Get(_battles, _currentId) + 1;

        if (mapEvent.HasWinner && mapEvent.PlayerSide == mapEvent.WinningSide)
            _victories[_currentId] = Get(_victories, _currentId) + 1;
    }

    private void ReconcileState()
    {
        _lastEnlisted = TorHirelingBridge.IsEnlisted();

        if (_lastEnlisted && string.IsNullOrWhiteSpace(_currentId))
            StartRecord(recovered: true);
        else if (!_lastEnlisted && !string.IsNullOrWhiteSpace(_currentId))
            FinishCurrent();
    }

    private void StartRecord(bool recovered)
    {
        var lord = TorHirelingBridge.GetEnlistingLord();
        if (lord == null)
            return;

        var duration = TorHirelingBridge.GetDurationDays();
        var id = "service_" + (++_sequence);
        _currentId = id;
        _lord[id] = lord.StringId;
        _faction[id] = lord.MapFaction?.StringId ?? "none";
        _duration[id] = Math.Max(0f, duration);
        _start[id] = CampaignTime.Now.ToDays - Math.Max(0f, duration);
        _end.Remove(id);
        _battles[id] = 0;
        _victories[id] = Math.Max(0, TorHirelingBridge.GetTorCountedVictories());
        _career[id] = TorHirelingBridge.GetCareerId();
        _result[id] = "active";
        _pendingDesertion = false;

        KaiRuntimeLog.Write(
            "SERVICE_RECORD_START",
            $"id={id}; lord={_lord[id]}; faction={_faction[id]}; career={_career[id]}; recovered={recovered}; priorDuration={duration:0.0}");
    }

    private void RefreshCurrent()
    {
        if (string.IsNullOrWhiteSpace(_currentId))
            return;

        _duration[_currentId] = Math.Max(
            Get(_duration, _currentId),
            TorHirelingBridge.GetDurationDays());

        _victories[_currentId] = Math.Max(
            Get(_victories, _currentId),
            TorHirelingBridge.GetTorCountedVictories());

        var lord = TorHirelingBridge.GetEnlistingLord();
        if (lord != null)
        {
            _lord[_currentId] = lord.StringId;
            _faction[_currentId] = lord.MapFaction?.StringId ?? Get(_faction, _currentId);
        }
    }

    private void FinishCurrent()
    {
        if (string.IsNullOrWhiteSpace(_currentId))
            return;

        var id = _currentId;
        var duration = Math.Max(0f, Get(_duration, id));
        var result = _pendingDesertion
            ? "desertion"
            : (duration >= TorMinimumHonourableServiceDays ? "honourable" : "forced_or_early_end");

        _end[id] = CampaignTime.Now.ToDays;
        _result[id] = result;
        _currentId = null;
        _pendingDesertion = false;

        KaiRuntimeLog.Write(
            "SERVICE_RECORD_END",
            $"id={id}; lord={Get(_lord, id)}; faction={Get(_faction, id)}; duration={duration:0.0}; battles={Get(_battles, id)}; victories={Get(_victories, id)}; career={Get(_career, id)}; result={result}");
    }

    private string Format(string id, bool active)
        => $"{id}: lord={Get(_lord, id)}; faction={Get(_faction, id)}; " +
           $"duration={Get(_duration, id):0.0}d; battles={Get(_battles, id)}; " +
           $"victories={Get(_victories, id)}; career={Get(_career, id)}; " +
           $"result={(active ? "active" : Get(_result, id))}";

    private static string Get(Dictionary<string, string> map, string key)
        => map.TryGetValue(key, out var value) ? value ?? "none" : "none";

    private static int Get(Dictionary<string, int> map, string key)
        => map.TryGetValue(key, out var value) ? Math.Max(0, value) : 0;

    private static float Get(Dictionary<string, float> map, string key)
        => map.TryGetValue(key, out var value) ? Math.Max(0f, value) : 0f;
}
