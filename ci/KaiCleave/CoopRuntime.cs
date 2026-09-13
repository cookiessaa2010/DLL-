using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    internal enum CoopRuntimeMode
    {
        Standalone,
        AuthoritativeServer,
        PassiveClient
    }

    /// <summary>
    /// Keeps KaiCleave deterministic when the Coop module is active.
    /// In KaiTOR's campaign-process topology only the /server /coopsave process is allowed
    /// to alter damage/collision state. Coop clients must load the same module/version for
    /// module validation, but they leave the combat Harmony patches inactive.
    /// </summary>
    internal static class CoopRuntime
    {
        private static readonly object ResolveSync = new object();
        private static MethodInfo _tryGetControlledObjectInfo;
        private static bool _playerLookupResolved;
        private static bool _playerLookupFailureLogged;

        internal static readonly bool CoopModuleActive = DetectCoopModuleActive();
        internal static readonly bool ServerCommandLine = HasCommandLineSwitch("/server");
        internal static readonly bool CoopSaveCommandLine = HasCommandLineSwitch("/coopsave");
        internal static readonly CoopRuntimeMode Mode = DetectMode();

        internal static bool CombatPatchesAllowed => Mode != CoopRuntimeMode.PassiveClient;
        internal static bool IsAuthoritativeCoopServer => Mode == CoopRuntimeMode.AuthoritativeServer;

        internal static string Describe()
        {
            return "mode=" + Mode +
                   " coopModule=" + CoopModuleActive +
                   " serverArg=" + ServerCommandLine +
                   " coopSaveArg=" + CoopSaveCommandLine;
        }

        internal static bool IsEligiblePlayerAttacker(Agent attacker)
        {
            if (!KaiSettings.PlayerOnly)
                return true;

            if (attacker == null)
                return false;

            if (!CoopModuleActive)
                return attacker.IsMainAgent;

            if (!IsAuthoritativeCoopServer)
                return false;

            // On the authoritative campaign process there is no meaningful IsMainAgent for every
            // connected player. Resolve the attacker's Hero against Coop's registered-player marker
            // instead. This gives PlayerOnly the same semantic meaning for all four controllers.
            var character = attacker.Character as CharacterObject;
            var hero = character != null ? character.HeroObject : null;
            if (hero == null)
                return false;

            var lookup = ResolvePlayerLookup();
            if (lookup == null)
            {
                if (!_playerLookupFailureLogged)
                {
                    _playerLookupFailureLogged = true;
                    DebugLogger.Write("coop player lookup unavailable; PlayerOnly fails closed on authoritative server");
                }
                return false;
            }

            try
            {
                object[] args = { hero, null };
                return (bool)lookup.Invoke(null, args);
            }
            catch (Exception ex)
            {
                if (!_playerLookupFailureLogged)
                {
                    _playerLookupFailureLogged = true;
                    DebugLogger.Write("coop player lookup failed; PlayerOnly fails closed: " + ex.GetType().Name + " " + ex.Message);
                }
                return false;
            }
        }

        private static CoopRuntimeMode DetectMode()
        {
            if (!CoopModuleActive)
                return CoopRuntimeMode.Standalone;

            // KaiTOR authoritative campaign process is explicitly launched with both markers.
            // Requiring /coopsave as well as /server avoids accidentally enabling cleave in an
            // unrelated Bannerlord server process that happens to have Coop present.
            return ServerCommandLine && CoopSaveCommandLine
                ? CoopRuntimeMode.AuthoritativeServer
                : CoopRuntimeMode.PassiveClient;
        }

        private static bool DetectCoopModuleActive()
        {
            try
            {
                return ModuleHelper.GetActiveModules().Any(module =>
                    string.Equals(module.Id, "Coop", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // The authoritative process always carries /coopsave. This fallback is intentionally
                // one-way: if module discovery fails on a client, do not invent Coop from nothing.
                return HasCommandLineSwitch("/coopsave");
            }
        }

        private static bool HasCommandLineSwitch(string value)
        {
            try
            {
                return Environment.GetCommandLineArgs().Any(arg =>
                    string.Equals(arg, value, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo ResolvePlayerLookup()
        {
            if (_playerLookupResolved)
                return _tryGetControlledObjectInfo;

            lock (ResolveSync)
            {
                if (_playerLookupResolved)
                    return _tryGetControlledObjectInfo;

                try
                {
                    var playerManager = AccessTools.TypeByName("GameInterface.Services.Players.PlayerManager");
                    if (playerManager != null)
                    {
                        _tryGetControlledObjectInfo = playerManager
                            .GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .FirstOrDefault(method =>
                                method.Name == "TryGetControlledObjectInfo" &&
                                method.ReturnType == typeof(bool) &&
                                method.GetParameters().Length == 2);
                    }
                }
                catch
                {
                    _tryGetControlledObjectInfo = null;
                }
                finally
                {
                    _playerLookupResolved = true;
                }

                return _tryGetControlledObjectInfo;
            }
        }
    }
}
