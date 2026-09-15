using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiTORStability
{
    internal static class ShaderCacheAcceleration
    {
        private const string HarmonyId = "kaitor.stability.shadercache";
        private static bool _enabled;
        private static int _singleLoadoutCopies = 1;
        private static bool _installed;
        private static bool _lowMemorySafeMode;
        private static double _physicalRamGb;

        public static void Install(StabilitySettings settings)
        {
            if (_installed || settings == null || !settings.EnableShaderCacheAcceleration) return;

            try
            {
                var memory = SystemMemoryProbe.TryRead();
                _physicalRamGb = memory != null ? memory.TotalPhysicalGb : 0d;
                _lowMemorySafeMode = settings.EnableLowMemoryShaderRoster &&
                                     memory != null &&
                                     memory.TotalPhysicalGb <= settings.LowMemoryPhysicalRamThresholdGb;

                if (memory != null)
                {
                    StabilityLog.Event("MEMORY_PROFILE", memory.Describe());
                    if (_lowMemorySafeMode)
                    {
                        StabilityLog.Event(
                            "LOW_MEMORY_SHADER_MODE",
                            "enabled=true; physicalGB=" + memory.TotalPhysicalGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                            "; thresholdGB=" + settings.LowMemoryPhysicalRamThresholdGb +
                            "; TOR full shader-build soldiers will be collapsed to one roster copy.");
                    }
                }

                var targetType = AccessTools.TypeByName("TOR_Core.GameManagers.TORShaderGameManager");
                if (targetType == null)
                {
                    StabilityLog.Event("SHADER_ACCELERATOR_FALLBACK", "TORShaderGameManager type was not found; TOR shader-cache builder remains unchanged.");
                    return;
                }

                var target = AccessTools.Method(targetType, "GetPlayerParty", new[] { typeof(BasicCharacterObject) });
                var postfix = AccessTools.Method(typeof(ShaderCacheAcceleration), nameof(PostfixGetPlayerParty));
                if (target == null || postfix == null)
                {
                    StabilityLog.Event("SHADER_ACCELERATOR_FALLBACK", "TOR GetPlayerParty contract did not match the expected 1.3.15 API.");
                    return;
                }

                _enabled = true;
                _singleLoadoutCopies = Math.Max(1, Math.Min(4, settings.ShaderCacheSingleLoadoutCopies));
                new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(postfix));
                _installed = true;

                StabilityLog.Event("SHADER_ACCELERATOR_READY",
                    "TOR Build Shader Cache roster patch installed; variantAware=true; singleLoadoutCopies=" + _singleLoadoutCopies +
                    "; lowMemorySafeMode=" + _lowMemorySafeMode +
                    "; physicalGB=" + _physicalRamGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
            catch (Exception ex)
            {
                _enabled = false;
                StabilityLog.Event("SHADER_ACCELERATOR_ERROR", ex.ToString());
            }
        }

        private static void PostfixGetPlayerParty(ref CustomBattleCombatant __result)
        {
            if (!_enabled || __result == null) return;

            try
            {
                var originalCharacters = __result.Characters.ToList();
                if (originalCharacters.Count == 0) return;

                var orderedGroups = new List<CharacterGroup>();
                var byId = new Dictionary<string, CharacterGroup>(StringComparer.Ordinal);

                foreach (var character in originalCharacters)
                {
                    if (character == null) continue;
                    var key = string.IsNullOrEmpty(character.StringId) ? "#" + character.GetHashCode() : character.StringId;
                    CharacterGroup group;
                    if (!byId.TryGetValue(key, out group))
                    {
                        group = new CharacterGroup(character);
                        byId.Add(key, group);
                        orderedGroups.Add(group);
                    }
                    group.OriginalCopies++;
                }

                var optimized = new CustomBattleCombatant(__result.Name, __result.BasicCulture, __result.Banner)
                {
                    Side = __result.Side
                };

                var originalCount = 0;
                var optimizedCount = 0;
                var collapsedSingle = 0;
                var collapsedVariantAware = 0;
                var collapsedLowMemory = 0;
                var preservedFourPlus = 0;

                foreach (var group in orderedGroups)
                {
                    originalCount += group.OriginalCopies;
                    var copies = group.OriginalCopies;

                    if (group.Character.IsSoldier && group.OriginalCopies > 1)
                    {
                        int desired;
                        if (_lowMemorySafeMode)
                        {
                            desired = 1;
                            if (desired < group.OriginalCopies) collapsedLowMemory++;
                        }
                        else
                        {
                            // Count up to 5 only: 4+ variants means TOR's original four copies are preserved.
                            var variantCount = group.Character.BattleEquipments.Take(5).Count();
                            if (variantCount <= 1)
                            {
                                desired = _singleLoadoutCopies;
                                if (desired < group.OriginalCopies) collapsedSingle++;
                            }
                            else if (variantCount < 4)
                            {
                                desired = variantCount;
                                if (desired < group.OriginalCopies) collapsedVariantAware++;
                            }
                            else
                            {
                                desired = group.OriginalCopies;
                                preservedFourPlus++;
                            }
                        }

                        copies = Math.Min(group.OriginalCopies, Math.Max(1, desired));
                    }

                    optimized.AddCharacter(group.Character, Math.Max(1, copies));
                    optimizedCount += Math.Max(1, copies);
                }

                if (__result.General != null) optimized.SetGeneral(__result.General);
                __result = optimized;

                StabilityLog.Event("SHADER_CACHE_ROSTER",
                    "original=" + originalCount +
                    "; optimized=" + optimizedCount +
                    "; saved=" + Math.Max(0, originalCount - optimizedCount) +
                    "; uniqueCharacters=" + orderedGroups.Count +
                    "; lowMemorySafeMode=" + _lowMemorySafeMode +
                    "; collapsedLowMemoryTroops=" + collapsedLowMemory +
                    "; collapsedSingleLoadoutTroops=" + collapsedSingle +
                    "; collapsedVariantAwareTroops=" + collapsedVariantAware +
                    "; preservedFourPlusVariantTroops=" + preservedFourPlus + ".");
            }
            catch (Exception ex)
            {
                StabilityLog.Event("SHADER_ACCELERATOR_ERROR", ex.ToString());
            }
        }

        private sealed class CharacterGroup
        {
            public CharacterGroup(BasicCharacterObject character) { Character = character; }
            public BasicCharacterObject Character { get; private set; }
            public int OriginalCopies { get; set; }
        }
    }
}
