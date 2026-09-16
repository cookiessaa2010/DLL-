using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    internal static class CleaveRules
    {
        internal static bool IsDamageHitEligible(
            Agent attacker,
            Agent victim,
            in AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon,
            int inflictedDamage)
        {
            if (!IsBaseEligible(attacker, victim, in collisionData, in attackerWeapon))
                return false;

            if (collisionData.CollisionResult != CombatCollisionResult.StrikeAgent)
                return false;

            if (collisionData.AttackBlockedWithShield)
                return false;

            return inflictedDamage > 0;
        }

        internal static bool CanTraverseCollision(
            Agent attacker,
            Agent victim,
            in AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon,
            int inflictedDamage)
        {
            if (!IsBaseEligible(attacker, victim, in collisionData, in attackerWeapon))
                return false;

            WeaponClass weaponClass = attackerWeapon.CurrentUsageItem.WeaponClass;
            bool heavy = KaiSettings.IsHeavyTraversalWeapon(weaponClass);

            if (collisionData.AttackBlockedWithShield)
            {
                bool allow = heavy && KaiSettings.HeavyShieldContinue;
                if (allow)
                    DebugLogger.WriteBlock("heavy-shield-continue", attacker, victim, in collisionData, weaponClass);
                return allow;
            }

            bool weaponBlocked = collisionData.CollisionResult == CombatCollisionResult.Blocked ||
                                 collisionData.CollisionResult == CombatCollisionResult.Parried ||
                                 collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked;
            if (weaponBlocked)
            {
                bool allow = heavy && KaiSettings.HeavyWeaponBlockContinue;
                if (allow)
                    DebugLogger.WriteBlock("heavy-weapon-block-continue", attacker, victim, in collisionData, weaponClass);
                return allow;
            }

            return collisionData.CollisionResult == CombatCollisionResult.StrikeAgent && inflictedDamage > 0;
        }

        private static bool IsBaseEligible(
            Agent attacker,
            Agent victim,
            in AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon)
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
                // Only real damage hits enter the duplicate/target-count registry.
                // Shield/weapon blocks may allow traversal, but they are not counted as damaged targets.
                if (!CleaveRules.IsDamageHitEligible(attacker, victim, in collisionData, in attackerWeapon, b.InflictedDamage))
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

                if (!CleaveRules.CanTraverseCollision(attacker, victim, in collisionData, in attackerWeapon, b.InflictedDamage))
                    return true;

                if (momentumRemaining <= 0f || !SwingTracker.CanContinueAfterCurrent(attacker, in collisionData, in attackerWeapon))
                    return true;

                DebugLogger.WriteHit("momentum-preserved", attacker, victim, in collisionData, in attackerWeapon,
                    b.InflictedDamage, momentumRemaining, null, null);

                // Skip native momentum reduction for accepted cleave traversal.
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

            if (!CleaveRules.CanTraverseCollision(attacker, defender, in collisionData, in attackerWeapon,
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
