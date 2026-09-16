using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    internal static class DebugLogger
    {
        private static readonly ConcurrentQueue<string> Queue = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent Signal = new AutoResetEvent(false);
        private static readonly object LifecycleSync = new object();
        private static Thread _worker;
        private static volatile bool _stopping;
        private static volatile bool _started;

        internal static void StartSession(string version)
        {
            if (!KaiSettings.DebugLogging) return;
            EnsureStarted();
            Write("============================================================");
            Write("KaiCleave " + version + " session started");
            Write("Config=" + KaiSettings.ConfigPath);
            Write("AsyncLogging=True | flushIntervalMs=250 | timingThresholdMs=2");
        }

        internal static void Write(string message)
        {
            if (!KaiSettings.DebugLogging || string.IsNullOrEmpty(KaiSettings.LogPath)) return;
            EnsureStarted();
            Queue.Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " | " + message);
            Signal.Set();
        }

        internal static void FlushAndStop()
        {
            lock (LifecycleSync)
            {
                if (!_started) return;
                _stopping = true;
                Signal.Set();
            }
            try
            {
                if (_worker != null && _worker.IsAlive) _worker.Join(1500);
            }
            catch { }
            FlushBatch();
            lock (LifecycleSync)
            {
                _worker = null;
                _started = false;
                _stopping = false;
            }
        }

        private static void EnsureStarted()
        {
            if (_started) return;
            lock (LifecycleSync)
            {
                if (_started) return;
                _stopping = false;
                _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "KaiCleave.AsyncLogger" };
                _started = true;
                _worker.Start();
            }
        }

        private static void WorkerLoop()
        {
            while (!_stopping)
            {
                Signal.WaitOne(250);
                FlushBatch();
            }
            FlushBatch();
        }

        private static void FlushBatch()
        {
            if (string.IsNullOrEmpty(KaiSettings.LogPath)) return;
            var lines = new List<string>(256);
            string line;
            while (lines.Count < 1024 && Queue.TryDequeue(out line)) lines.Add(line);
            if (lines.Count == 0) return;
            try
            {
                var directory = Path.GetDirectoryName(KaiSettings.LogPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(KaiSettings.LogPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
            catch { }
        }

        internal static void WriteTiming(string stage, long elapsedTicks, Agent attacker, Agent victim)
        {
            if (!KaiSettings.DebugLogging) return;
            var ms = elapsedTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
            if (ms < 2d) return;
            Write("CLEAVE_TIMING|stage=" + stage +
                  "; ms=" + ms.ToString("0.###", CultureInfo.InvariantCulture) +
                  "; attacker=" + (attacker != null ? attacker.Index.ToString() : "null") +
                  "; victim=" + (victim != null ? victim.Index.ToString() : "null"));
        }

        internal static void WriteBlock(string stage, Agent attacker, Agent victim,
            in AttackCollisionData collisionData, WeaponClass weaponClass)
        {
            if (!KaiSettings.DebugLogging) return;
            Write(stage +
                  " attacker=" + (attacker != null ? attacker.Index.ToString() : "null") +
                  " victim=" + (victim != null ? victim.Index.ToString() : "null") +
                  " weapon=" + weaponClass +
                  " shield=" + collisionData.AttackBlockedWithShield +
                  " correctShield=" + collisionData.CorrectSideShieldBlock +
                  " collision=" + collisionData.CollisionResult +
                  " progress=" + collisionData.AttackProgress.ToString("0.000", CultureInfo.InvariantCulture));
        }

        internal static void WriteHit(string stage, Agent attacker, Agent victim,
            in AttackCollisionData collisionData, in MissionWeapon attackerWeapon,
            int damage, float momentum, MeleeCollisionReaction? before, MeleeCollisionReaction? after)
        {
            if (!KaiSettings.DebugLogging) return;
            string weapon = attackerWeapon.CurrentUsageItem != null ? attackerWeapon.CurrentUsageItem.WeaponClass.ToString() : "None";
            string reaction = before.HasValue ? " reaction=" + before.Value + "->" + (after.HasValue ? after.Value.ToString() : "?") : string.Empty;
            Write(stage +
                  " attacker=" + (attacker != null ? attacker.Index.ToString() : "null") +
                  " victim=" + (victim != null ? victim.Index.ToString() : "null") +
                  " weapon=" + weapon +
                  " damage=" + damage +
                  " armorAbsorb=" + collisionData.AbsorbedByArmor +
                  " momentum=" + momentum.ToString("0.000", CultureInfo.InvariantCulture) +
                  " progress=" + collisionData.AttackProgress.ToString("0.000", CultureInfo.InvariantCulture) +
                  " target=" + (attacker != null ? SwingTracker.GetHitCount(attacker, in collisionData).ToString() : "0") + reaction);
        }
    }
}
