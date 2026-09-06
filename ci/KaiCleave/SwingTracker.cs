using System;
using System.Collections.Generic;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    internal static class SwingTracker
    {
        private sealed class SwingState
        {
            internal int WeaponSlot;
            internal Agent.UsageDirection AttackDirection;
            internal float LastProgress;
            internal long LastTicks;
            internal readonly HashSet<int> Victims = new HashSet<int>();
            internal int HitCount;
        }

        private static readonly Dictionary<int, SwingState> States = new Dictionary<int, SwingState>();
        private static readonly object Sync = new object();
        private const double SwingTimeoutMs = 900.0;

        internal static void Reset()
        {
            lock (Sync)
                States.Clear();
        }

        internal static bool TryRegisterHit(
            Agent attacker,
            Agent victim,
            in AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon)
        {
            if (attacker == null || victim == null)
                return true;

            lock (Sync)
            {
                SwingState state = GetOrResetState(attacker, in collisionData);

                if (state.Victims.Contains(victim.Index))
                {
                    DebugLogger.Write("duplicate-suppressed attacker=" + attacker.Index + " victim=" + victim.Index +
                                      " progress=" + collisionData.AttackProgress.ToString("0.000"));
                    return false;
                }

                if (KaiSettings.MaxTargetsPerSwing > 0 && state.HitCount >= KaiSettings.MaxTargetsPerSwing)
                {
                    DebugLogger.Write("target-cap-suppressed attacker=" + attacker.Index + " victim=" + victim.Index +
                                      " cap=" + KaiSettings.MaxTargetsPerSwing);
                    return false;
                }

                state.Victims.Add(victim.Index);
                state.HitCount++;
                Touch(state, in collisionData);
                return true;
            }
        }

        internal static bool CanContinueAfterCurrent(
            Agent attacker,
            in AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon)
        {
            if (attacker == null)
                return false;

            lock (Sync)
            {
                SwingState state = GetOrResetState(attacker, in collisionData);
                Touch(state, in collisionData);
                return KaiSettings.MaxTargetsPerSwing <= 0 || state.HitCount < KaiSettings.MaxTargetsPerSwing;
            }
        }

        internal static int GetHitCount(Agent attacker, in AttackCollisionData collisionData)
        {
            if (attacker == null)
                return 0;

            lock (Sync)
                return GetOrResetState(attacker, in collisionData).HitCount;
        }

        private static SwingState GetOrResetState(Agent attacker, in AttackCollisionData collisionData)
        {
            long now = DateTime.UtcNow.Ticks;
            int slot = collisionData.AffectorWeaponSlotOrMissileIndex;

            if (!States.TryGetValue(attacker.Index, out SwingState state))
            {
                state = NewState(slot, collisionData.AttackDirection, collisionData.AttackProgress, now);
                States[attacker.Index] = state;
                return state;
            }

            double elapsedMs = TimeSpan.FromTicks(now - state.LastTicks).TotalMilliseconds;
            bool progressRestarted = collisionData.AttackProgress + 0.05f < state.LastProgress;
            bool weaponChanged = slot != state.WeaponSlot;
            bool directionChanged = collisionData.AttackDirection != state.AttackDirection && collisionData.AttackProgress < 0.35f;
            bool timedOut = elapsedMs > SwingTimeoutMs;

            if (progressRestarted || weaponChanged || directionChanged || timedOut)
            {
                state = NewState(slot, collisionData.AttackDirection, collisionData.AttackProgress, now);
                States[attacker.Index] = state;
            }

            return state;
        }

        private static SwingState NewState(int slot, Agent.UsageDirection direction, float progress, long now)
        {
            return new SwingState
            {
                WeaponSlot = slot,
                AttackDirection = direction,
                LastProgress = progress,
                LastTicks = now,
                HitCount = 0
            };
        }

        private static void Touch(SwingState state, in AttackCollisionData collisionData)
        {
            state.LastProgress = Math.Max(state.LastProgress, collisionData.AttackProgress);
            state.LastTicks = DateTime.UtcNow.Ticks;
        }
    }
}
