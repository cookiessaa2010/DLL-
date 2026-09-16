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
    /// Targeted recovery test for Bannerlord's ActionIndexCache static-initialiser race.
    ///
    /// The engine's UI tableaus consume static readonly ActionIndexCache fields. If the type's
    /// static constructor ran before MBAnimation action types were loaded, those fields can remain
    /// baked at -1 while a later live lookup by name resolves correctly. This test repairs only the
    /// two inventory-idle fields used by CharacterTableau and BasicCharacterTableau.
    ///
    /// Safety:
    /// - MBAnimation gate runs before any ActionIndexCache field read.
    /// - Healthy static fields are never overwritten.
    /// - Only the two field/name pairs whose mapping is exact are touched.
    /// - Reflection writes are re-read and verified.
    /// - At most three repair passes per process.
    /// </summary>
    internal static class StaticActionCacheRecovery
    {
        private static readonly object Gate = new object();
        private static bool _completed;
        private static int _attempts;
        private const int MaxAttempts = 3;
        private const string ProbeAction = "act_inventory_idle_start";

        internal static bool TryEnsureRepaired(string phase)
        {
            try
            {
                lock (Gate)
                {
                    if (_completed)
                        return true;
                    if (_attempts >= MaxAttempts)
                        return false;

                    // IMPORTANT: do not touch ActionIndexCache before this MBAnimation-only gate.
                    int actionCount;
                    int probeIndex;
                    try
                    {
                        actionCount = MBAnimation.GetNumActionCodes();
                        probeIndex = MBAnimation.GetActionCodeWithName(ProbeAction);
                    }
                    catch (Exception ex)
                    {
                        PortraitFixLog.Event(
                            "STATIC_CACHE_GATE",
                            "phase=" + phase + "; ready=false; error=" + ex.GetType().Name);
                        return false;
                    }

                    PortraitFixLog.Event(
                        "STATIC_CACHE_GATE",
                        "phase=" + phase + "; actionCount=" + actionCount + "; probeIndex=" + probeIndex +
                        "; ready=" + (actionCount > 0 && probeIndex >= 0));

                    if (actionCount <= 0 || probeIndex < 0)
                        return false;

                    _attempts++;

                    var idleStart = RepairOne("act_inventory_idle_start", "act_inventory_idle_start");
                    var idle = RepairOne("act_inventory_idle", "act_inventory_idle");

                    var failed = (idleStart.Failed ? 1 : 0) + (idle.Failed ? 1 : 0);
                    var repaired = (idleStart.Repaired ? 1 : 0) + (idle.Repaired ? 1 : 0);
                    var healthy = (idleStart.Healthy ? 1 : 0) + (idle.Healthy ? 1 : 0);

                    _completed = failed == 0 &&
                                 idleStart.AfterIndex >= 0 &&
                                 idle.AfterIndex >= 0;

                    PortraitFixLog.Event(
                        "STATIC_CACHE_RECOVERY",
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
            catch (Exception ex)
            {
                PortraitFixLog.Event(
                    "STATIC_CACHE_RECOVERY",
                    "phase=" + phase + "; completed=false; error=" + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        internal static int ReadStaticIndex(string fieldName)
        {
            try
            {
                var field = typeof(ActionIndexCache).GetField(
                    fieldName,
                    BindingFlags.Public | BindingFlags.Static);
                if (field == null)
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
                var field = typeof(ActionIndexCache).GetField(
                    fieldName,
                    BindingFlags.Public | BindingFlags.Static);
                if (field == null || field.FieldType != typeof(ActionIndexCache))
                {
                    result.Failed = true;
                    result.AfterIndex = int.MinValue;
                    PortraitFixLog.Event(
                        "STATIC_CACHE_FIELD",
                        "field=" + fieldName + "; found=false");
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
                        "STATIC_CACHE_FIELD",
                        "field=" + fieldName +
                        "; action=" + actionName +
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
                        "STATIC_CACHE_FIELD",
                        "field=" + fieldName +
                        "; action=" + actionName +
                        "; before=" + current.Index +
                        "; live=" + liveIndex +
                        "; after=" + current.Index +
                        "; repaired=false; reason=live-unresolved");
                    return result;
                }

                // At this point action types are loaded and the exact field->action mapping is known.
                var candidate = ActionIndexCache.Create(actionName);
                if (candidate.Index != liveIndex || candidate.Index < 0)
                {
                    result.Failed = true;
                    result.AfterIndex = current.Index;
                    PortraitFixLog.Event(
                        "STATIC_CACHE_FIELD",
                        "field=" + fieldName +
                        "; action=" + actionName +
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
                    "STATIC_CACHE_FIELD",
                    "field=" + fieldName +
                    "; action=" + actionName +
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
                    "STATIC_CACHE_FIELD",
                    "field=" + fieldName + "; repaired=false; error=" + ex.GetType().Name + ": " + ex.Message);
                return result;
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
    /// Repair immediately before the normal CharacterTableau refresh consumes the cached idle.
    /// </summary>
    [HarmonyPatch(typeof(CharacterTableau), "RefreshCharacterTableau")]
    internal static class CharacterTableauStaticCacheRecoveryPatch
    {
        [HarmonyPrefix]
        internal static void Prefix()
        {
            StaticActionCacheRecovery.TryEnsureRepaired("CharacterTableau.Refresh");
        }
    }

    /// <summary>
    /// Repair immediately before Save/Load BasicCharacterTableau consumes act_inventory_idle.
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterTableau), "RefreshCharacterTableau")]
    internal static class BasicCharacterTableauStaticCacheRecoveryPatch
    {
        private static int _fallbackSamples;

        [HarmonyPrefix]
        internal static void Prefix()
        {
            StaticActionCacheRecovery.TryEnsureRepaired("BasicCharacterTableau.Refresh");
        }

        [HarmonyPostfix]
        internal static void Postfix(BasicCharacterTableau __instance)
        {
            // Fallback only if initonly reflection write was refused by this runtime. It applies the
            // same live idle to the just-refreshed save-preview entity without changing save data.
            var staticIdle = StaticActionCacheRecovery.ReadStaticIndex("act_inventory_idle");
            if (staticIdle >= 0)
                return;

            var liveIdle = ActionIndexCache.Create("act_inventory_idle");
            if (liveIdle.Index < 0)
                return;

            var sample = Interlocked.Increment(ref _fallbackSamples);
            try
            {
                var t = Traverse.Create(__instance);
                var entities = t.Field("_currentCharacters").GetValue<GameEntity[]>();
                var index = t.Field("_currentEntityToShowIndex").GetValue<int>();
                if (entities == null || index < 0 || index >= entities.Length || entities[index] == null)
                    return;

                var entity = entities[index];
                var skeleton = entity.Skeleton;
                if (skeleton == null)
                    return;

                skeleton.SetAgentActionChannel(0, liveIdle, 0f, -0.2f, true, 0f);
                PortraitFixLog.Event(
                    "STATIC_CACHE_FALLBACK",
                    "target=BasicCharacterTableau; sample=" + sample +
                    "; applied=true; staticIdle=" + staticIdle +
                    "; liveIdle=" + liveIdle.Index);
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event(
                    "STATIC_CACHE_FALLBACK",
                    "target=BasicCharacterTableau; sample=" + sample +
                    "; applied=false; error=" + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    /// <summary>
    /// CharacterTableau normally returns the static act_inventory_idle_start sentinel fallback.
    /// If reflection repair was refused, substitute the same action via a live lookup only when the
    /// original result is invalid.
    /// </summary>
    [HarmonyPatch(typeof(CharacterTableau), "GetIdleAction")]
    internal static class CharacterTableauIdleFallbackPatch
    {
        private static int _samples;

        [HarmonyPostfix]
        internal static void Postfix(ref ActionIndexCache __result)
        {
            if (__result.Index >= 0)
                return;

            var live = ActionIndexCache.Create("act_inventory_idle_start");
            if (live.Index < 0)
                return;

            var sample = Interlocked.Increment(ref _samples);
            var before = __result.Index;
            __result = live;

            PortraitFixLog.Event(
                "STATIC_CACHE_FALLBACK",
                "target=CharacterTableau.GetIdleAction; sample=" + sample +
                "; applied=true; before=" + before +
                "; liveIdleStart=" + live.Index);
        }
    }
}
