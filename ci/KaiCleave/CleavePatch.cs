using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    internal static class CleaveRules
    {
        internal static bool IsEligible(
            Agent attacker,
            Agent victim,
            in AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon,
            int inflictedDamage)
        {
            if (!KaiSettings.Enabled || !CoopRuntime.CombatPatchesAllowed)
                return false;

            if (!CoopRuntime.CoopModuleActive && GameNetwork.IsSessionActive)
                return false;

            if (attacker == null || victim == null || !victim.IsActive())
                return false;

            if (!CoopRuntime.IsEligiblePlayerAttacker(attacker))
                return false;

            if (!KaiSettings.AllowFriendlyTargets && attacker.IsFriendOf(victim))
                return false;

            if (collisionData.IsMissile || collisionData.IsAlternativeAttack)
                return false;

            if (collisionData.CollisionResult != CombatCollisionResult.StrikeAgent)
                return false;

            if (collisionData.AttackBlockedWithShield)
                return false;

            if (inflictedDamage <= 0)
                return false;

            if (!KaiSettings.AllowThrusts && (StrikeType)collisionData.StrikeType != StrikeType.Swing)
                return false;

            if (attackerWeapon.IsEmpty || attackerWeapon.CurrentUsageItem == null)
                return false;

            return KaiSettings.IsWeaponEnabled(attackerWeapon.CurrentUsageItem.WeaponClass);
        }
    }

    [HarmonyPatch(typeof(Mission), "RegisterBlow")]
    internal static class RegisterBlowPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Agent attacker,
            Agent victim,
            Blow b,
            ref AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (!CleaveRules.IsEligible(attacker, victim, in collisionData, in attackerWeapon, b.InflictedDamage))
                    return true;

                return SwingTracker.TryRegisterHit(attacker, victim, in collisionData, in attackerWeapon);
            }
            finally
            {
                DebugLogger.WriteTiming("register-blow", System.Diagnostics.Stopwatch.GetTimestamp() - started, attacker, victim);
            }
        }
    }

    [HarmonyPatch(typeof(MissionCombatMechanicsHelper), nameof(MissionCombatMechanicsHelper.UpdateMomentumRemaining))]
    internal static class CleaveMomentumPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            ref float momentumRemaining,
            in Blow b,
            in AttackCollisionData collisionData,
            Agent attacker,
            Agent victim,
            in MissionWeapon attackerWeapon,
            bool isCrushThrough)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (!KaiSettings.FullMomentum)
                    return true;

                if (isCrushThrough)
                    return true;

                if (!CleaveRules.IsEligible(attacker, victim, in collisionData, in attackerWeapon, b.InflictedDamage))
                    return true;

                if (momentumRemaining <= 0f || !SwingTracker.CanContinueAfterCurrent(attacker, in collisionData, in attackerWeapon))
                    return true;

                DebugLogger.WriteHit("momentum-preserved", attacker, victim, in collisionData, in attackerWeapon,
                    b.InflictedDamage, momentumRemaining, null, null);

                return false;
            }
            finally
            {
                DebugLogger.WriteTiming("momentum", System.Diagnostics.Stopwatch.GetTimestamp() - started, attacker, victim);
            }
        }
    }

    [HarmonyPatch(typeof(MissionCombatMechanicsHelper), nameof(MissionCombatMechanicsHelper.DecideWeaponCollisionReaction))]
    internal static class NativeReactionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
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
                    in attackerWeapon, momentumRemaining, ref colReaction, "native");
            }
            finally
            {
                DebugLogger.WriteTiming("native-reaction", System.Diagnostics.Stopwatch.GetTimestamp() - started, attacker, defender);
            }
        }
    }

    internal static class CleaveReaction
    {
        internal static void Apply(
            in Blow registeredBlow,
            in AttackCollisionData collisionData,
            Agent attacker,
            Agent defender,
            in MissionWeapon attackerWeapon,
            float momentumRemaining,
            ref MeleeCollisionReaction colReaction,
            string source)
        {
            if (!KaiSettings.ForceSlicedThrough)
                return;

            if (!CleaveRules.IsEligible(attacker, defender, in collisionData, in attackerWeapon,
                    registeredBlow.InflictedDamage))
                return;

            if (!SwingTracker.CanContinueAfterCurrent(attacker, in collisionData, in attackerWeapon))
                return;

            MeleeCollisionReaction before = colReaction;
            colReaction = MeleeCollisionReaction.SlicedThrough;

            DebugLogger.WriteHit(source + "-reaction", attacker, defender, in collisionData, in attackerWeapon,
                registeredBlow.InflictedDamage, momentumRemaining, before, colReaction);
        }
    }
}
