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
        CoopPeer,
        CoopCampaignServer
    }

    /// <summary>
    /// Keeps KaiCleave deterministic when BannerlordCoop/KaiTOR is active.
    /// Coop battles are simulated peer-to-peer: every client owns its own real agents while
    /// remote agents are inert puppets. Cleave therefore runs on the peer that owns the
    /// attacking player Hero, not on the campaign server and not on puppet copies.
    /// </summary>
    internal static class CoopRuntime
    {
        private static readonly object ResolveSync = new object();
        private static MethodInfo _tryGetControlledObjectInfo;
        private static bool _playerLookupResolved;
        private static bool _playerLookupFailureLogged;
        private static bool _coopPlayerOnlyWarningLogged;

        internal static readonly bool CoopModuleActive = DetectCoopModuleActive();
        internal static readonly bool ServerCommandLine = HasCommandLineSwitch("/server");
        internal static readonly bool CoopSaveCommandLine = HasCommandLineSwitch("/coopsave");
        internal static readonly CoopRuntimeMode Mode = DetectMode();

        // KaiTOR's campaign process owns campaign state, not local mission collision simulation.
        // Actual Coop battle peers must keep KaiCleave patched so the owner's real swing can
        // generate the native/routed blows that the existing Coop damage router distributes.
        internal static bool CombatPatchesAllowed => Mode != CoopRuntimeMode.CoopCampaignServer;

        internal static string Describe()
        {
            return "mode=" + Mode +
                   " coopModule=" + CoopModuleActive +
                   " serverArg=" + ServerCommandLine +
                   " coopSaveArg=" + CoopSaveCommandLine;
        }

        internal static bool IsEligiblePlayerAttacker(Agent attacker)
        {
            if (attacker == null)
                return false;

            if (!CoopModuleActive)
                return !KaiSettings.PlayerOnly || attacker.IsMainAgent;

            if (Mode != CoopRuntimeMode.CoopPeer)
                return false;

            // v0.2.1 intentionally keeps Coop cleave player-only even if the INI asks for
            // PlayerOnly=false. NPC ownership can migrate between peers, so enabling NPC cleave
            // safely requires a separate authority-aware registry integration. Failing closed here
            // avoids duplicate NPC damage while keeping the default player-only behavior correct.
            if (!KaiSettings.PlayerOnly && !_coopPlayerOnlyWarningLogged)
            {
                _coopPlayerOnlyWarningLogged = true;
                DebugLogger.Write("Coop safety: PlayerOnly=false is not enabled in v0.2.1; using local registered player Hero only");
            }

            var character = attacker.Character as CharacterObject;
            var hero = character != null ? character.HeroObject : null;
            if (hero == null)
                return false;

            var lookup = ResolvePlayerLookup();
            if (lookup == null)
            {
                LogPlayerLookupFailure("coop player lookup unavailable; local-owner check fails closed");
                return false;
            }

            try
            {
                object[] args = { hero, null };
                if (!(bool)lookup.Invoke(null, args))
                    return false;

                return IsLocallyControlled(args[1]);
            }
            catch (Exception ex)
            {
                LogPlayerLookupFailure("coop player lookup failed; local-owner check fails closed: " +
                                       ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static bool IsLocallyControlled(object controlledObjectInfo)
        {
            if (controlledObjectInfo == null)
                return false;

            try
            {
                var type = controlledObjectInfo.GetType();
                var property = type.GetProperty("IsControlled", BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.PropertyType == typeof(bool))
                    return (bool)property.GetValue(controlledObjectInfo, null);

                // Compatibility fallback for the pinned Coop contract: compare the registered
                // object's controller id with the local IControllerIdProvider.ControllerId.
                var ownerField = type.GetField("ObjectControllerId", BindingFlags.Public | BindingFlags.Instance);
                var providerField = type.GetField("ControllerIdProvider", BindingFlags.Public | BindingFlags.Instance);
                var owner = ownerField != null ? ownerField.GetValue(controlledObjectInfo) as string : null;
                var provider = providerField != null ? providerField.GetValue(controlledObjectInfo) : null;
                var controllerProperty = provider != null
                    ? provider.GetType().GetProperty("ControllerId", BindingFlags.Public | BindingFlags.Instance)
                    : null;
                var local = controllerProperty != null ? controllerProperty.GetValue(provider, null) as string : null;
                return !string.IsNullOrEmpty(owner) && string.Equals(owner, local, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                LogPlayerLookupFailure("coop ControlledObjectInfo read failed; local-owner check fails closed: " +
                                       ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static CoopRuntimeMode DetectMode()
        {
            if (!CoopModuleActive)
                return CoopRuntimeMode.Standalone;

            // KaiTOR launches its campaign authority with both switches. Battle missions are
            // client/peer-owned, so that process should not alter mission collision state.
            return ServerCommandLine && CoopSaveCommandLine
                ? CoopRuntimeMode.CoopCampaignServer
                : CoopRuntimeMode.CoopPeer;
        }

        private static bool DetectCoopModuleActive()
        {
            try
            {
                if (ModuleHelper.GetActiveModules().Any(module =>
                    string.Equals(module.Id, "Coop", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch
            {
                // Fall through to type/command-line probes.
            }

            try
            {
                if (AccessTools.TypeByName("Coop.CoopMod") != null ||
                    AccessTools.TypeByName("Coop.Core.Client.ClientLogic") != null)
                    return true;
            }
            catch
            {
                // Ignore and use the campaign-server marker as the final fallback.
            }

            return HasCommandLineSwitch("/coopsave");
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

        private static void LogPlayerLookupFailure(string message)
        {
            if (_playerLookupFailureLogged)
                return;

            _playerLookupFailureLogged = true;
            DebugLogger.Write(message);
        }
    }
}
