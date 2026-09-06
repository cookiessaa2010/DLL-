using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    /// <summary>
    /// Keeps the original swing momentum after a successful melee hit made by the player.
    /// Bannerlord/TOR still calculates each target's damage, armor absorption, body part,
    /// perks and resistances normally. Only the post-hit momentum reduction is skipped.
    /// </summary>
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
            // Beta is deliberately single-player and player-only for maximum TOR safety.
            if (GameNetwork.IsSessionActive)
                return true;

            if (attacker == null || !attacker.IsMainAgent || victim == null)
                return true;

            // Do not interfere with Bannerlord/TOR crush-through calculations.
            if (isCrushThrough)
                return true;

            // Only real agent strikes cleave. Walls, blocks, parries and chambers stay vanilla.
            if (collisionData.CollisionResult != CombatCollisionResult.StrikeAgent)
                return true;

            if (collisionData.AttackBlockedWithShield)
                return true;

            // If the active damage model produced no damage, keep vanilla momentum handling.
            // This preserves hard stops from invulnerability/edge cases and is safer for TOR.
            if (b.InflictedDamage <= 0)
                return true;

            if (momentumRemaining <= 0f)
                return true;

            // Returning false skips CalculateRemainingMomentum for this hit.
            // The next native weapon collision therefore starts with the same swing momentum,
            // but its own damage is still evaluated independently by Bannerlord/TOR.
            return false;
        }
    }
}
