using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiTORStability
{
    /// <summary>
    /// Conservative phase-1 accelerator for TOR's "Build Shader Cache" custom battle.
    /// TOR currently adds four copies of every soldier. We only collapse those four
    /// copies when the character exposes zero/one battle equipment variant. Troops
    /// with multiple battle equipment variants keep TOR's original multiplicity.
    /// This removes obviously redundant agents without intentionally reducing the
    /// sampling coverage of multi-loadout troops.
    /// </summary>
    internal static class ShaderCacheAcceleration
    {
        private const string HarmonyId = "kaitor.stability.shadercache";
        private static bool _enabled;
        private static int _singleLoadoutCopies = 1;
        private static bool _installed;

        public static void Install(StabilitySettings settings)
        {
            if (_installed || settings == null || !settings.EnableShaderCacheAcceleration)
            {
                return;
            }

            try
            {
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

                var harmony = new Harmony(HarmonyId);
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _installed = true;

                StabilityLog.Event(
                    "SHADER_ACCELERATOR_READY",
                    "TOR Build Shader Cache roster patch installed; singleLoadoutCopies=" + _singleLoadoutCopies + ".");
            }
            catch (Exception ex)
            {
                _enabled = false;
                StabilityLog.Event("SHADER_ACCELERATOR_ERROR", ex.ToString());
            }
        }

        private static void PostfixGetPlayerParty(ref CustomBattleCombatant __result)
        {
            if (!_enabled || __result == null)
            {
                return;
            }

            try
            {
                var originalCharacters = __result.Characters.ToList();
                if (originalCharacters.Count == 0)
                {
                    return;
                }

                var orderedGroups = new List<CharacterGroup>();
                var byId = new Dictionary<string, CharacterGroup>(StringComparer.Ordinal);

                foreach (var character in originalCharacters)
                {
                    if (character == null) continue;
                    var key = string.IsNullOrEmpty(character.StringId)
                        ? "#" + character.GetHashCode().ToString()
                        : character.StringId;

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
                var collapsedTroops = 0;
                var multiLoadoutTroops = 0;

                foreach (var group in orderedGroups)
                {
                    originalCount += group.OriginalCopies;
                    var copies = group.OriginalCopies;

                    if (group.Character.IsSoldier && group.OriginalCopies > _singleLoadoutCopies)
                    {
                        // Take(2) is intentional: we only need to distinguish 0/1 from 2+.
                        // Multi-loadout troops retain TOR's original four-copy sampling.
                        var battleVariantCount = group.Character.BattleEquipments.Take(2).Count();
                        if (battleVariantCount <= 1)
                        {
                            copies = Math.Min(group.OriginalCopies, _singleLoadoutCopies);
                            if (copies < group.OriginalCopies) collapsedTroops++;
                        }
                        else
                        {
                            multiLoadoutTroops++;
                        }
                    }

                    optimized.AddCharacter(group.Character, Math.Max(1, copies));
                    optimizedCount += Math.Max(1, copies);
                }

                if (__result.General != null)
                {
                    optimized.SetGeneral(__result.General);
                }

                __result = optimized;

                StabilityLog.Event(
                    "SHADER_CACHE_ROSTER",
                    "original=" + originalCount +
                    "; optimized=" + optimizedCount +
                    "; saved=" + Math.Max(0, originalCount - optimizedCount) +
                    "; uniqueCharacters=" + orderedGroups.Count +
                    "; collapsedSingleLoadoutTroops=" + collapsedTroops +
                    "; preservedMultiLoadoutTroops=" + multiLoadoutTroops + ".");
            }
            catch (Exception ex)
            {
                // Fail open at the postfix boundary. If anything unexpected happens,
                // the original TOR result remains usable unless replacement completed.
                StabilityLog.Event("SHADER_ACCELERATOR_ERROR", ex.ToString());
            }
        }

        private sealed class CharacterGroup
        {
            public CharacterGroup(BasicCharacterObject character)
            {
                Character = character;
            }

            public BasicCharacterObject Character { get; private set; }
            public int OriginalCopies { get; set; }
        }
    }
}
