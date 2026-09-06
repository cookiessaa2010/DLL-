using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    internal static class TorCompatibility
    {
        private static bool _patched;

        internal static bool TryPatch(Harmony harmony)
        {
            if (_patched || harmony == null)
                return _patched;

            try
            {
                Type torType = AccessTools.TypeByName("TOR_Core.Models.TORAgentApplyDamageModel");
                if (torType == null)
                    return false;

                MethodInfo target = AccessTools.Method(torType, "DecideWeaponCollisionReaction");
                MethodInfo postfix = AccessTools.Method(typeof(TorCompatibility), nameof(TorReactionPostfix));
                if (target == null || postfix == null)
                {
                    DebugLogger.Write("TOR final-reaction patch target not found");
                    return false;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _patched = true;
                DebugLogger.Write("TOR final-reaction patch active: " + torType.FullName);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Write("TOR patch failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        // This postfix runs after TOR's own override, so TOR cannot replace our final SlicedThrough
        // decision after the native helper has already returned.
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
            CleaveReaction.Apply(in registeredBlow, in collisionData, attacker, defender,
                in attackerWeapon, momentumRemaining, ref colReaction, "TOR-final");
        }
    }
}
