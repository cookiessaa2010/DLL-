using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Stable, narrowly-scoped repair for Bannerlord's ActionIndexCache static-initializer race.
    ///
    /// Confirmed failure mode on Bannerlord 1.3.15.110062:
    /// ActionIndexCache.act_inventory_idle_start and act_inventory_idle can be baked as -1 when the
    /// type is initialized before MBAnimation action types are ready. Later live lookups already
    /// resolve correctly, but UI tableaus keep consuming the poisoned static values and characters
    /// remain in bind pose.
    ///
    /// This repair intentionally touches only the two inventory-idle fields required by the affected
    /// CharacterTableau and BasicCharacterTableau paths. It is a no-op when those fields are healthy.
    /// </summary>
    internal static class ActionIndexCacheRepair
    {
        private static readonly object Gate = new object();
        private static bool _completed;
        private static int _attempts;
        private static int _deferredLogs;

        private const int MaxAttempts = 3;
        private const int MaxDeferredLogs = 2;
        private const string ProbeAction = "act_inventory_idle_start";

        internal static bool TryEnsureRepaired(string phase)
        {
            lock (Gate)
            {
                if (_completed)
                    return true;
                if (_attempts >= MaxAttempts)
                    return false;

                // Do not touch ActionIndexCache before this MBAnimation-only gate. The gate prevents
                // this mod from being the code that initializes ActionIndexCache too early.
                int actionCount;
                int probeIndex;
                try
                {
                    actionCount = MBAnimation.GetNumActionCodes();
                    probeIndex = MBAnimation.GetActionCodeWithName(ProbeAction);
                }
                catch (Exception ex)
                {
                    LogDeferred(phase, "gate-exception=" + ex.GetType().Name);
                    return false;
                }

                if (actionCount <= 0 || probeIndex < 0)
                {
                    LogDeferred(
                        phase,
                        "actionCount=" + actionCount + "; probeIndex=" + probeIndex + "; ready=false");
                    return false;
                }

                _attempts++;

                var idleStart = RepairOne("act_inventory_idle_start", "act_inventory_idle_start");
                var idle = RepairOne("act_inventory_idle", "act_inventory_idle");

                var failed = (idleStart.Failed ? 1 : 0) + (idle.Failed ? 1 : 0);
                var repaired = (idleStart.Repaired ? 1 : 0) + (idle.Repaired ? 1 : 0);
                var healthy = (idleStart.Healthy ? 1 : 0) + (idle.Healthy ? 1 : 0);

                _completed = failed == 0 && idleStart.AfterIndex >= 0 && idle.AfterIndex >= 0;

                PortraitFixLog.Event(
                    "ACTION_CACHE_REPAIR",
                    "phase=" + phase +
                    "; attempt=" + _attempts +
                    "; completed=" + _completed +
                    "; repaired=" + repaired +
                    "; healthy=" + healthy +
                    "; failed=" + failed +
                    "; idleStart=" + idleStart.AfterIndex +
                    "; idle=" + idle.AfterIndex);

                return _completed;
            }
        }

        internal static int ReadStaticIndex(string fieldName)
        {
            try
            {
                var field = typeof(ActionIndexCache).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (field == null || field.FieldType != typeof(ActionIndexCache))
                    return int.MinValue;
                return ((ActionIndexCache)field.GetValue(null)).Index;
            }
            catch
            {
                return int.MinValue;
            }
        }

        private static RepairResult RepairOne(string fieldName, string actionName)
        {
            var result = new RepairResult();

            try
            {
                var field = typeof(ActionIndexCache).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (field == null || field.FieldType != typeof(ActionIndexCache))
                {
                    result.Failed = true;
                    result.AfterIndex = int.MinValue;
                    PortraitFixLog.Event("ACTION_CACHE_FIELD", "field=" + fieldName + "; found=false");
                    return result;
                }

                var current = (ActionIndexCache)field.GetValue(null);
                result.BeforeIndex = current.Index;

                var liveIndex = MBAnimation.GetActionCodeWithName(actionName);
                result.LiveIndex = liveIndex;

                if (current.Index >= 0)
                {
                    result.Healthy = true;
                    result.AfterIndex = current.Index;
                    PortraitFixLog.Event(
                        "ACTION_CACHE_FIELD",
                        "field=" + fieldName +
                        "; before=" + current.Index +
                        "; live=" + liveIndex +
                        "; after=" + current.Index +
                        "; repaired=false; reason=already-healthy");
                    return result;
                }

                if (liveIndex < 0)
                {
                    result.Failed = true;
                    result.AfterIndex = current.Index;
                    PortraitFixLog.Event(
                        "ACTION_CACHE_FIELD",
                        "field=" + fieldName +
                        "; before=" + current.Index +
                        "; live=" + liveIndex +
                        "; after=" + current.Index +
                        "; repaired=false; reason=live-unresolved");
                    return result;
                }

                // Action types are loaded and these two field->action mappings are exact.
                var candidate = ActionIndexCache.Create(actionName);
                if (candidate.Index < 0 || candidate.Index != liveIndex)
                {
                    result.Failed = true;
                    result.AfterIndex = current.Index;
                    PortraitFixLog.Event(
                        "ACTION_CACHE_FIELD",
                        "field=" + fieldName +
                        "; before=" + current.Index +
                        "; live=" + liveIndex +
                        "; candidate=" + candidate.Index +
                        "; after=" + current.Index +
                        "; repaired=false; reason=candidate-mismatch");
                    return result;
                }

                field.SetValue(null, candidate);
                var after = ((ActionIndexCache)field.GetValue(null)).Index;

                result.AfterIndex = after;
                result.Repaired = after == candidate.Index && after >= 0;
                result.Failed = !result.Repaired;

                PortraitFixLog.Event(
                    "ACTION_CACHE_FIELD",
                    "field=" + fieldName +
                    "; before=" + current.Index +
                    "; live=" + liveIndex +
                    "; candidate=" + candidate.Index +
                    "; after=" + after +
                    "; repaired=" + result.Repaired +
                    "; reason=" + (result.Repaired ? "write-verified" : "write-refused"));

                return result;
            }
            catch (Exception ex)
            {
                result.Failed = true;
                result.AfterIndex = int.MinValue;
                PortraitFixLog.Event(
                    "ACTION_CACHE_FIELD",
                    "field=" + fieldName + "; repaired=false; error=" + ex.GetType().Name + ": " + ex.Message);
                return result;
            }
        }

        private static void LogDeferred(string phase, string detail)
        {
            var n = Interlocked.Increment(ref _deferredLogs);
            if (n <= MaxDeferredLogs)
            {
                PortraitFixLog.Event(
                    "ACTION_CACHE_DEFERRED",
                    "phase=" + phase + "; " + detail);
            }
            else if (n == MaxDeferredLogs + 1)
            {
                PortraitFixLog.Event(
                    "ACTION_CACHE_DEFERRED",
                    "further-deferred-events-suppressed=true");
            }
        }

        private sealed class RepairResult
        {
            internal int BeforeIndex = int.MinValue;
            internal int LiveIndex = int.MinValue;
            internal int AfterIndex = int.MinValue;
            internal bool Healthy;
            internal bool Repaired;
            internal bool Failed;
        }
    }

    /// <summary>
    /// Repair immediately before the normal CharacterTableau refresh consumes the cached idle action.
    /// </summary>
    [HarmonyPatch(typeof(CharacterTableau), "RefreshCharacterTableau")]
    internal static class CharacterTableauActionCacheRepairPatch
    {
        [HarmonyPrefix]
        internal static void Prefix()
        {
            ActionIndexCacheRepair.TryEnsureRepaired("CharacterTableau.Refresh");
        }
    }

    /// <summary>
    /// Repair immediately before Save/Load BasicCharacterTableau consumes act_inventory_idle.
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterTableau), "RefreshCharacterTableau")]
    internal static class BasicCharacterTableauActionCacheRepairPatch
    {
        private static int _fallbackLogs;

        [HarmonyPrefix]
        internal static void Prefix()
        {
            ActionIndexCacheRepair.TryEnsureRepaired("BasicCharacterTableau.Refresh");
        }

        [HarmonyPostfix]
        internal static void Postfix(BasicCharacterTableau __instance)
        {
            // Runtime fallback only when reflection could not restore the poisoned readonly static.
            // It changes only the already-created preview skeleton and never touches save data.
            var staticIdle = ActionIndexCacheRepair.ReadStaticIndex("act_inventory_idle");
            if (staticIdle >= 0)
                return;

            var liveCode = MBAnimation.GetActionCodeWithName("act_inventory_idle");
            if (liveCode < 0)
                return;

            var liveIdle = ActionIndexCache.Create("act_inventory_idle");
            if (liveIdle.Index != liveCode || liveIdle.Index < 0)
                return;

            try
            {
                var t = Traverse.Create(__instance);
                var entities = t.Field("_currentCharacters").GetValue<GameEntity[]>();
                var index = t.Field("_currentEntityToShowIndex").GetValue<int>();
                if (entities == null || index < 0 || index >= entities.Length || entities[index] == null)
                    return;

                var skeleton = entities[index].Skeleton;
                if (skeleton == null)
                    return;

                skeleton.SetAgentActionChannel(0, liveIdle, 0f, -0.2f, true, 0f);
                LogFallback(
                    "target=BasicCharacterTableau; applied=true; staticIdle=" + staticIdle +
                    "; liveIdle=" + liveIdle.Index);
            }
            catch (Exception ex)
            {
                LogFallback(
                    "target=BasicCharacterTableau; applied=false; error=" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void LogFallback(string message)
        {
            var n = Interlocked.Increment(ref _fallbackLogs);
            if (n <= 4)
                PortraitFixLog.Event("ACTION_CACHE_FALLBACK", message);
            else if (n == 5)
                PortraitFixLog.Event("ACTION_CACHE_FALLBACK", "further-events-suppressed=true");
        }
    }

    /// <summary>
    /// CharacterTableau normally falls back to the static act_inventory_idle_start value. If a
    /// runtime refuses the reflection write, substitute the same action via a live lookup.
    /// </summary>
    [HarmonyPatch(typeof(CharacterTableau), "GetIdleAction")]
    internal static class CharacterTableauIdleFallbackPatch
    {
        private static int _fallbackLogs;

        [HarmonyPostfix]
        internal static void Postfix(ref ActionIndexCache __result)
        {
            if (__result.Index >= 0)
                return;

            var liveCode = MBAnimation.GetActionCodeWithName("act_inventory_idle_start");
            if (liveCode < 0)
                return;

            var live = ActionIndexCache.Create("act_inventory_idle_start");
            if (live.Index != liveCode || live.Index < 0)
                return;

            var before = __result.Index;
            __result = live;

            var n = Interlocked.Increment(ref _fallbackLogs);
            if (n <= 4)
            {
                PortraitFixLog.Event(
                    "ACTION_CACHE_FALLBACK",
                    "target=CharacterTableau.GetIdleAction; applied=true; before=" + before +
                    "; liveIdleStart=" + live.Index);
            }
            else if (n == 5)
            {
                PortraitFixLog.Event("ACTION_CACHE_FALLBACK", "further-events-suppressed=true");
            }
        }
    }
}
