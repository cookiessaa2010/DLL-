using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TaleWorlds.Core;

namespace KaiCleave
{
    internal static class KaiSettings
    {
        internal static bool Enabled = true;
        internal static bool PlayerOnly = true;
        internal static bool FullMomentum = true;
        internal static bool ForceSlicedThrough = true;
        internal static bool AllowThrusts = false;
        internal static bool AllowFriendlyTargets = false;
        internal static int MaxTargetsPerSwing = 12;
        internal static bool DebugLogging = true;
        internal static bool ShowLoadMessage = true;

        // Heavy traversal is intentionally player-only through CoopRuntime.
        // Shield continuation defaults ON for v0.3 testing; weapon-block/parry continuation is opt-in.
        internal static bool HeavyShieldContinue = true;
        internal static bool HeavyWeaponBlockContinue = false;

        internal static bool OneHandedSword = true;
        internal static bool TwoHandedSword = true;
        internal static bool OneHandedAxe = true;
        internal static bool TwoHandedAxe = true;
        internal static bool OneHandedPolearm = true;
        internal static bool TwoHandedPolearm = true;
        internal static bool LowGripPolearm = true;
        internal static bool Mace = false;
        internal static bool TwoHandedMace = false;
        internal static bool Dagger = false;
        internal static bool Pick = false;

        internal static string ModuleRoot { get; private set; }
        internal static string ConfigPath { get; private set; }
        internal static string LogPath { get; private set; }

        internal static void Load()
        {
            ResolvePaths();

            if (!File.Exists(ConfigPath))
                File.WriteAllText(ConfigPath, BuildConfig());

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(ConfigPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            Enabled = ReadBool(values, nameof(Enabled), Enabled);
            PlayerOnly = ReadBool(values, nameof(PlayerOnly), PlayerOnly);
            FullMomentum = ReadBool(values, nameof(FullMomentum), FullMomentum);
            ForceSlicedThrough = ReadBool(values, nameof(ForceSlicedThrough), ForceSlicedThrough);
            AllowThrusts = ReadBool(values, nameof(AllowThrusts), AllowThrusts);
            AllowFriendlyTargets = ReadBool(values, nameof(AllowFriendlyTargets), AllowFriendlyTargets);
            MaxTargetsPerSwing = ReadInt(values, nameof(MaxTargetsPerSwing), MaxTargetsPerSwing, 1, 32);
            DebugLogging = ReadBool(values, nameof(DebugLogging), DebugLogging);
            ShowLoadMessage = ReadBool(values, nameof(ShowLoadMessage), ShowLoadMessage);
            HeavyShieldContinue = ReadBool(values, nameof(HeavyShieldContinue), HeavyShieldContinue);
            HeavyWeaponBlockContinue = ReadBool(values, nameof(HeavyWeaponBlockContinue), HeavyWeaponBlockContinue);

            OneHandedSword = ReadBool(values, nameof(OneHandedSword), OneHandedSword);
            TwoHandedSword = ReadBool(values, nameof(TwoHandedSword), TwoHandedSword);
            OneHandedAxe = ReadBool(values, nameof(OneHandedAxe), OneHandedAxe);
            TwoHandedAxe = ReadBool(values, nameof(TwoHandedAxe), TwoHandedAxe);
            OneHandedPolearm = ReadBool(values, nameof(OneHandedPolearm), OneHandedPolearm);
            TwoHandedPolearm = ReadBool(values, nameof(TwoHandedPolearm), TwoHandedPolearm);
            LowGripPolearm = ReadBool(values, nameof(LowGripPolearm), LowGripPolearm);
            Mace = ReadBool(values, nameof(Mace), Mace);
            TwoHandedMace = ReadBool(values, nameof(TwoHandedMace), TwoHandedMace);
            Dagger = ReadBool(values, nameof(Dagger), Dagger);
            Pick = ReadBool(values, nameof(Pick), Pick);
        }

        internal static void Save()
        {
            try
            {
                ResolvePaths();
                File.WriteAllText(ConfigPath, BuildConfig());
                DebugLogger.Write("settings saved | " + DescribeRuntimeSettings());
            }
            catch (Exception ex)
            {
                DebugLogger.Write("settings save failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        internal static bool IsWeaponEnabled(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.OneHandedSword: return OneHandedSword;
                case WeaponClass.TwoHandedSword: return TwoHandedSword;
                case WeaponClass.OneHandedAxe: return OneHandedAxe;
                case WeaponClass.TwoHandedAxe: return TwoHandedAxe;
                case WeaponClass.OneHandedPolearm: return OneHandedPolearm;
                case WeaponClass.TwoHandedPolearm: return TwoHandedPolearm;
                case WeaponClass.LowGripPolearm: return LowGripPolearm;
                case WeaponClass.Mace: return Mace;
                case WeaponClass.TwoHandedMace: return TwoHandedMace;
                case WeaponClass.Dagger: return Dagger;
                case WeaponClass.Pick: return Pick;
                default: return false;
            }
        }

        internal static bool IsHeavyTraversalWeapon(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.TwoHandedSword:
                case WeaponClass.TwoHandedAxe:
                case WeaponClass.TwoHandedMace:
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    return true;
                default:
                    return false;
            }
        }

        internal static string DescribeRuntimeSettings()
        {
            return "enabled=" + Enabled +
                   " maxTargets=" + MaxTargetsPerSwing +
                   " heavyShield=" + HeavyShieldContinue +
                   " heavyWeaponBlock=" + HeavyWeaponBlockContinue +
                   " friendly=" + AllowFriendlyTargets +
                   " thrusts=" + AllowThrusts +
                   " debug=" + DebugLogging;
        }

        private static void ResolvePaths()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            DirectoryInfo win64 = new DirectoryInfo(dllDir ?? AppDomain.CurrentDomain.BaseDirectory);
            ModuleRoot = win64.Parent?.Parent?.FullName ?? win64.FullName;
            ConfigPath = Path.Combine(ModuleRoot, "KaiCleave.ini");
            LogPath = Path.Combine(ModuleRoot, "KaiCleave.log");
        }

        private static bool ReadBool(Dictionary<string, string> values, string key, bool fallback)
        {
            return values.TryGetValue(key, out string s) && bool.TryParse(s, out bool v) ? v : fallback;
        }

        private static int ReadInt(Dictionary<string, string> values, string key, int fallback, int min, int max)
        {
            if (!values.TryGetValue(key, out string s) || !int.TryParse(s, out int v))
                return fallback;
            return Math.Max(min, Math.Min(max, v));
        }

        private static string BuildConfig()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# KaiCleave v0.3.0-heavy-block-ui");
            sb.AppendLine("# Bannerlord 1.3.15.110062 / The Old Realms 1.3.15");
            sb.AppendLine("# Press F10 in game to change these settings without editing this file.");
            sb.AppendLine("# NPC cleave remains disabled by default. KaiTOR Coop always fails closed to player-owned attackers.");
            sb.AppendLine();
            sb.AppendLine("[General]");
            sb.AppendLine("Enabled=" + Enabled);
            sb.AppendLine("PlayerOnly=" + PlayerOnly);
            sb.AppendLine("MaxTargetsPerSwing=" + MaxTargetsPerSwing);
            sb.AppendLine("DebugLogging=" + DebugLogging);
            sb.AppendLine("ShowLoadMessage=" + ShowLoadMessage);
            sb.AppendLine();
            sb.AppendLine("[Cleave]");
            sb.AppendLine("# Full damage per accepted target; no progressive damage falloff.");
            sb.AppendLine("FullMomentum=" + FullMomentum);
            sb.AppendLine("ForceSlicedThrough=" + ForceSlicedThrough);
            sb.AppendLine("AllowThrusts=" + AllowThrusts);
            sb.AppendLine("AllowFriendlyTargets=" + AllowFriendlyTargets);
            sb.AppendLine();
            sb.AppendLine("[HeavyBlock]");
            sb.AppendLine("# Applies only to two-handed weapons and polearms. One-handed weapons still stop on blocks.");
            sb.AppendLine("HeavyShieldContinue=" + HeavyShieldContinue);
            sb.AppendLine("# Weapon block/parry continuation is separate because it changes duel balance more strongly.");
            sb.AppendLine("HeavyWeaponBlockContinue=" + HeavyWeaponBlockContinue);
            sb.AppendLine();
            sb.AppendLine("[Weapons]");
            sb.AppendLine("OneHandedSword=" + OneHandedSword);
            sb.AppendLine("TwoHandedSword=" + TwoHandedSword);
            sb.AppendLine("OneHandedAxe=" + OneHandedAxe);
            sb.AppendLine("TwoHandedAxe=" + TwoHandedAxe);
            sb.AppendLine("OneHandedPolearm=" + OneHandedPolearm);
            sb.AppendLine("TwoHandedPolearm=" + TwoHandedPolearm);
            sb.AppendLine("LowGripPolearm=" + LowGripPolearm);
            sb.AppendLine("Mace=" + Mace);
            sb.AppendLine("TwoHandedMace=" + TwoHandedMace);
            sb.AppendLine("Dagger=" + Dagger);
            sb.AppendLine("Pick=" + Pick);
            return sb.ToString();
        }
    }
}
