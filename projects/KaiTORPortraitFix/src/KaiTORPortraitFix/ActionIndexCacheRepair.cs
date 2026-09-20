using System;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;
using TaleWorlds.ScreenSystem;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Safe runtime fallback for poisoned Bannerlord inventory-idle action indices.
    ///
    /// 0.6.0 repaired ActionIndexCache static readonly fields globally. Live testing on
    /// Bannerlord 1.3.15.110062 + TOR 1.3.15 showed that this can leak into TOR character
    /// creation and corrupt the preview pose, and a repeat character-creation attempt ended
    /// in a native access violation. 0.6.1 therefore never writes ActionIndexCache statics.
    ///
    /// The fix is now local to the affected UI call:
    /// - CharacterTableau.GetIdleAction receives a live fallback only when the returned index is invalid.
    /// - BasicCharacterTableau receives a live inventory-idle action only on its current preview skeleton.
    /// - CharacterCreationScreen is explicitly excluded from all pose fallback work.
    /// </summary>
    internal static class ActionIndexCacheRepair
    {
        private const string IdleStartAction = "act_inventory_idle_start";
        private const string IdleAction = "act_inventory_idle";

        internal static bool IsCharacterCreationScreenActive()
        {
            try
            {
                var top = ScreenManager.TopScreen;
                var fullName = top?.GetType()?.FullName ?? string.Empty;
                return fullName.IndexOf(
                    "CharacterCreation.CharacterCreationScreen",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                // Fail safe: if screen detection itself is unavailable, do not mutate any global
                // state anyway. The local fallback remains bounded to the caller.
                return false;
            }
        }

        internal static bool TryCreateLiveAction(string actionName, out ActionIndexCache action)
        {
            action = default(ActionIndexCache);

            try
            {
                if (string.IsNullOrWhiteSpace(actionName))
                    return false;

                var actionCount = MBAnimation.GetNumActionCodes();
                if (actionCount <= 0)
                    return false;

                var liveIndex = MBAnimation.GetActionCodeWithName(actionName);
                if (liveIndex < 0)
                    return false;

                var candidate = ActionIndexCache.Create(actionName);
                if (candidate.Index < 0 || candidate.Index != liveIndex)
                    return false;

                action = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryCreateIdleStart(out ActionIndexCache action)
            => TryCreateLiveAction(IdleStartAction, out action);

        internal static bool TryCreateIdle(out ActionIndexCache action)
            => TryCreateLiveAction(IdleAction, out action);
    }

    /// <summary>
    /// CharacterTableau normally returns ActionIndexCache.act_inventory_idle_start.
    /// If that cached value is poisoned, substitute a valid live lookup for this call only.
    /// TOR's CharacterCreationScreen is never touched.
    /// </summary>
    [HarmonyPatch(typeof(CharacterTableau), "GetIdleAction")]
    internal static class CharacterTableauIdleFallbackPatch
    {
        private static int _fallbackLogs;
        private static int _creationSkipLogs;

        [HarmonyPostfix]
        internal static void Postfix(ref ActionIndexCache __result)
        {
            if (__result.Index >= 0)
                return;

            if (ActionIndexCacheRepair.IsCharacterCreationScreenActive())
            {
                LogCreationSkip("target=CharacterTableau.GetIdleAction; reason=character-creation");
                return;
            }

            if (!ActionIndexCacheRepair.TryCreateIdleStart(out var live))
                return;

            var before = __result.Index;
            __result = live;

            var n = Interlocked.Increment(ref _fallbackLogs);
            if (n <= 6)
            {
                PortraitFixLog.Event(
                    "LOCAL_POSE_FALLBACK",
                    "target=CharacterTableau.GetIdleAction; applied=true; before=" + before +
                    "; liveIdleStart=" + live.Index);
            }
            else if (n == 7)
            {
                PortraitFixLog.Event("LOCAL_POSE_FALLBACK", "further-events-suppressed=true");
            }
        }

        private static void LogCreationSkip(string message)
        {
            var n = Interlocked.Increment(ref _creationSkipLogs);
            if (n <= 3)
                PortraitFixLog.Event("CHARACTER_CREATION_BYPASS", message);
            else if (n == 4)
                PortraitFixLog.Event("CHARACTER_CREATION_BYPASS", "further-events-suppressed=true");
        }
    }

    /// <summary>
    /// Save/Load uses BasicCharacterTableau and can consume a poisoned static inventory-idle
    /// value directly. Apply the valid action only to the already-created preview skeleton.
    /// No static field is read through reflection or written.
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterTableau), "RefreshCharacterTableau")]
    internal static class BasicCharacterTableauIdleFallbackPatch
    {
        private static int _fallbackLogs;
        private static int _creationSkipLogs;

        [HarmonyPostfix]
        internal static void Postfix(BasicCharacterTableau __instance)
        {
            if (__instance == null)
                return;

            if (ActionIndexCacheRepair.IsCharacterCreationScreenActive())
            {
                LogCreationSkip("target=BasicCharacterTableau.Refresh; reason=character-creation");
                return;
            }

            if (!ActionIndexCacheRepair.TryCreateIdle(out var liveIdle))
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

                var n = Interlocked.Increment(ref _fallbackLogs);
                if (n <= 6)
                {
                    PortraitFixLog.Event(
                        "LOCAL_POSE_FALLBACK",
                        "target=BasicCharacterTableau.Refresh; applied=true; liveIdle=" + liveIdle.Index);
                }
                else if (n == 7)
                {
                    PortraitFixLog.Event("LOCAL_POSE_FALLBACK", "further-events-suppressed=true");
                }
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event(
                    "LOCAL_POSE_FALLBACK",
                    "target=BasicCharacterTableau.Refresh; applied=false; error=" +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void LogCreationSkip(string message)
        {
            var n = Interlocked.Increment(ref _creationSkipLogs);
            if (n <= 3)
                PortraitFixLog.Event("CHARACTER_CREATION_BYPASS", message);
            else if (n == 4)
                PortraitFixLog.Event("CHARACTER_CREATION_BYPASS", "further-events-suppressed=true");
        }
    }
}
