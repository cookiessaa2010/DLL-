using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
                File.WriteAllText(ConfigPath, DefaultConfig);

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

        internal const string DefaultConfig = @"# KaiCleave v0.2.0-beta
# Bannerlord 1.3.15.110062 / The Old Realms 1.3.15

[General]
Enabled=true
PlayerOnly=true
MaxTargetsPerSwing=12
DebugLogging=true
ShowLoadMessage=true

[Cleave]
# true = every accepted target receives the same pre-armor attack momentum.
FullMomentum=true
# true = force native traversal to continue after a valid enemy hit.
ForceSlicedThrough=true
# false is recommended: horizontal/overhead swings cleave, thrusts do not.
AllowThrusts=false
AllowFriendlyTargets=false

[Weapons]
OneHandedSword=true
TwoHandedSword=true
OneHandedAxe=true
TwoHandedAxe=true
OneHandedPolearm=true
TwoHandedPolearm=true
LowGripPolearm=true
Mace=false
TwoHandedMace=false
Dagger=false
Pick=false
";
    }
}
