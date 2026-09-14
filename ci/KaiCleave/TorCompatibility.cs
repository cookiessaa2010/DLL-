using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    internal static class TorCompatibility
    {
        private static bool _patched;
        private static bool _resolved;

        internal static bool TryPatch(Harmony harmony)
        {
            if (_patched) return true;
            if (_resolved) return true;
            if (harmony == null) return false;

            try
            {
                Type torType = AccessTools.TypeByName("TOR_Core.Models.TORAgentApplyDamageModel");
                if (torType == null) return false;

                MethodInfo target = torType.GetMethod(
                    "DecideWeaponCollisionReaction",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo postfix = AccessTools.Method(typeof(TorCompatibility), nameof(TorReactionPostfix));
                if (target == null || postfix == null)
                {
                    DebugLogger.Write("TOR final-reaction patch target not found");
                    return false;
                }

                if (target.DeclaringType != torType)
                {
                    _resolved = true;
                    DebugLogger.Write("TOR final-reaction patch not required | inherited=" +
                                      (target.DeclaringType != null ? target.DeclaringType.FullName : "unknown"));
                    return true;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _patched = true;
                _resolved = true;
                DebugLogger.Write("TOR final-reaction patch active: " + torType.FullName);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Write("TOR patch failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static void TorReactionPostfix(
            in Blow registeredBlow,
            in AttackCollisionData collisionData,
            Agent attacker,
            Agent defender,
            in MissionWeapon attackerWeapon,
            bool isFatalHit,
            bool isShruggedOff,
            float momentumRemaining,
            ref MeleeCollisionReaction colReaction)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                CleaveReaction.Apply(in registeredBlow, in collisionData, attacker, defender,
                    in attackerWeapon, momentumRemaining, ref colReaction, "TOR-final");
            }
            finally
            {
                DebugLogger.WriteTiming("tor-reaction", System.Diagnostics.Stopwatch.GetTimestamp() - started, attacker, defender);
            }
        }
    }
}
