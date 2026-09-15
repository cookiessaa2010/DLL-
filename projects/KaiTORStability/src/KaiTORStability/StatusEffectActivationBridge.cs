using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.MountAndBlade;

namespace KaiTORStability
{
    /// <summary>
    /// Hooks TOR StatusEffectComponent.AddEffect so newly activated components can be
    /// scheduled immediately instead of waiting for the periodic safety rescan.
    /// The hook is optional: if TOR changes the private contract, the optimizer keeps
    /// the older rescan-based path unchanged.
    /// </summary>
    internal static class StatusEffectActivationBridge
    {
        private const string HarmonyId = "kaitor.stability.statuseffect.activation";
        private static readonly object Sync = new object();
        private static readonly Dictionary<object, OptimizedStatusEffectMissionLogic> Owners =
            new Dictionary<object, OptimizedStatusEffectMissionLogic>(ReferenceComparer.Instance);

        private static bool _installAttempted;
        private static bool _installed;
        private static Type _installedComponentType;

        internal static bool EnsureInstalled(Type componentType)
        {
            if (componentType == null) return false;

            lock (Sync)
            {
                if (_installed && _installedComponentType == componentType) return true;
                if (_installAttempted) return false;
                _installAttempted = true;
            }

            try
            {
                var target = componentType
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name == "AddEffect" && x.GetParameters().Length == 1);
                var postfix = AccessTools.Method(typeof(StatusEffectActivationBridge), nameof(AddEffectPostfix));

                if (target == null || postfix == null)
                {
                    StabilityLog.Event(
                        "STATUS_ACTIVATION_HOOK_FALLBACK",
                        "TOR StatusEffectComponent.AddEffect contract was not found; periodic rescans remain the activation source.");
                    return false;
                }

                new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(postfix));

                lock (Sync)
                {
                    _installed = true;
                    _installedComponentType = componentType;
                }

                StabilityLog.Event(
                    "STATUS_ACTIVATION_HOOK_READY",
                    "Patched TOR StatusEffectComponent.AddEffect; inactive components can be activated immediately.");
                return true;
            }
            catch (Exception ex)
            {
                StabilityLog.Event("STATUS_ACTIVATION_HOOK_ERROR", ex.ToString());
                return false;
            }
        }

        internal static void Register(AgentComponent component, OptimizedStatusEffectMissionLogic owner)
        {
            if (component == null || owner == null) return;
            lock (Sync)
            {
                Owners[component] = owner;
            }
        }

        internal static void Unregister(AgentComponent component)
        {
            if (component == null) return;
            lock (Sync)
            {
                Owners.Remove(component);
            }
        }

        internal static void UnregisterOwner(OptimizedStatusEffectMissionLogic owner)
        {
            if (owner == null) return;
            lock (Sync)
            {
                var keys = Owners.Where(x => ReferenceEquals(x.Value, owner)).Select(x => x.Key).ToArray();
                foreach (var key in keys)
                {
                    Owners.Remove(key);
                }
            }
        }

        private static void AddEffectPostfix(object __instance)
        {
            if (__instance == null) return;

            OptimizedStatusEffectMissionLogic owner;
            lock (Sync)
            {
                if (!Owners.TryGetValue(__instance, out owner) || owner == null) return;
            }

            owner.NotifyComponentActivated(__instance as AgentComponent);
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
