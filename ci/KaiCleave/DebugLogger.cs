using System;
using System.Globalization;
using System.IO;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    internal static class DebugLogger
    {
        private static readonly object Sync = new object();

        internal static void StartSession(string version)
        {
            if (!KaiSettings.DebugLogging)
                return;

            Write("============================================================");
            Write("KaiCleave " + version + " session started");
            Write("Config=" + KaiSettings.ConfigPath);
        }

        internal static void Write(string message)
        {
            if (!KaiSettings.DebugLogging || string.IsNullOrEmpty(KaiSettings.LogPath))
                return;

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        KaiSettings.LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                        " | " + message + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never destabilize the game.
            }
        }

        internal static void WriteHit(
            string stage,
            Agent attacker,
            Agent victim,
            in AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon,
            int damage,
            float momentum,
            MeleeCollisionReaction? before,
            MeleeCollisionReaction? after)
        {
            if (!KaiSettings.DebugLogging)
                return;

            string weapon = attackerWeapon.CurrentUsageItem != null
                ? attackerWeapon.CurrentUsageItem.WeaponClass.ToString()
                : "None";

            string reaction = before.HasValue
                ? " reaction=" + before.Value + "->" + (after.HasValue ? after.Value.ToString() : "?")
                : string.Empty;

            Write(stage +
                  " attacker=" + (attacker != null ? attacker.Index.ToString() : "null") +
                  " victim=" + (victim != null ? victim.Index.ToString() : "null") +
                  " weapon=" + weapon +
                  " damage=" + damage +
                  " armorAbsorb=" + collisionData.AbsorbedByArmor +
                  " momentum=" + momentum.ToString("0.000", CultureInfo.InvariantCulture) +
                  " progress=" + collisionData.AttackProgress.ToString("0.000", CultureInfo.InvariantCulture) +
                  " target=" + (attacker != null ? SwingTracker.GetHitCount(attacker, in collisionData).ToString() : "0") +
                  reaction);
        }
    }
}
