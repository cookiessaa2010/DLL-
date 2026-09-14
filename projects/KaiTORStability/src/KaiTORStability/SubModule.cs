using System;
using System.Linq;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace KaiTORStability
{
    public sealed class SubModule : MBSubModuleBase
    {
        private StabilitySettings _settings;
        private float _shaderTelemetryAccumulator;
        private int _lastShaderCount = -1;
        private DateTime _lastShaderEventUtc = DateTime.MinValue;
        private bool _shaderCompilationActive;
        private bool _shaderTelemetryFaulted;
        private bool _coopActive;
        private bool _coopMissionSkipLogged;
        private bool _initialScreenLogged;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            StabilityLog.StartSession();
            _settings = StabilitySettings.Load();
            _coopActive = IsCoopActive();

            StabilityLog.Event(
                "MODULE_LOAD",
                "KaiTOR Stability 0.4.2 loaded after TOR_Core; optimization=" + _settings.EnableStatusEffectOptimization +
                "; rescanMs=" + _settings.FullRescanIntervalMs +
                "; shaderTelemetry=" + _settings.EnableShaderTelemetry +
                "; shaderSampleMs=" + _settings.ShaderTelemetryIntervalMs +
                "; shaderAcceleration=" + _settings.EnableShaderCacheAcceleration +
                "; variantAwareRoster=true" +
                "; singleLoadoutCopies=" + _settings.ShaderCacheSingleLoadoutCopies +
                "; coopActive=" + _coopActive +
                "; coopGameplayGuard=" + _settings.DisableGameplayPatchesWhenCoopActive);

            if (_coopActive && _settings.DisableGameplayPatchesWhenCoopActive)
            {
                StabilityLog.Event(
                    "COOP_COMPAT_ACTIVE",
                    "Coop module detected. Gameplay-affecting mission replacement is disabled; loading/shader optimizations remain active.");
            }

            ShaderCacheAcceleration.Install(_settings);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_initialScreenLogged) return;
            _initialScreenLogged = true;
            StabilityLog.Event("INITIAL_SCREEN_READY", "Bannerlord initial module screen is ready.");
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            if (_settings == null || !_settings.EnableShaderTelemetry || _shaderTelemetryFaulted) return;

            _shaderTelemetryAccumulator += Math.Max(0f, dt);
            var intervalSeconds = _settings.ShaderTelemetryIntervalMs / 1000f;
            if (_shaderTelemetryAccumulator < intervalSeconds) return;
            _shaderTelemetryAccumulator = 0f;

            try
            {
                var remaining = TaleWorlds.Engine.Utilities.GetNumberOfShaderCompilationsInProgress();
                var now = DateTime.UtcNow;

                if (remaining > 0)
                {
                    if (!_shaderCompilationActive)
                    {
                        _shaderCompilationActive = true;
                        StabilityLog.Event("SHADER_START", "remaining=" + remaining);
                    }

                    if (remaining != _lastShaderCount || (now - _lastShaderEventUtc).TotalSeconds >= 5d)
                    {
                        StabilityLog.Event("SHADER_PROGRESS", "remaining=" + remaining);
                        _lastShaderEventUtc = now;
                    }
                }
                else if (_shaderCompilationActive)
                {
                    _shaderCompilationActive = false;
                    StabilityLog.Event("SHADER_COMPLETE", "remaining=0");
                    _lastShaderEventUtc = now;
                }

                _lastShaderCount = remaining;
            }
            catch (Exception ex)
            {
                _shaderTelemetryFaulted = true;
                StabilityLog.Event("SHADER_TELEMETRY_ERROR", ex.GetType().FullName + ": " + ex.Message);
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);

            if (_settings == null || !_settings.EnableStatusEffectOptimization || mission == null) return;

            if (_settings.DisableGameplayPatchesWhenCoopActive && (_coopActive || IsCoopActive()))
            {
                _coopActive = true;
                if (!_coopMissionSkipLogged)
                {
                    _coopMissionSkipLogged = true;
                    StabilityLog.Event(
                        "OPTIMIZATION_SKIP",
                        "Coop is active; TOR StatusEffectMissionLogic replacement is disabled by CoopCompatibility guard.");
                }
                return;
            }

            try
            {
                var torLogic = mission.MissionBehaviors.FirstOrDefault(
                    x => string.Equals(x.GetType().FullName,
                        "TOR_Core.BattleMechanics.StatusEffect.StatusEffectMissionLogic",
                        StringComparison.Ordinal));

                if (torLogic == null)
                {
                    StabilityLog.Event("OPTIMIZATION_SKIP", "TOR StatusEffectMissionLogic was not present in this mission.");
                    return;
                }

                var replacement = OptimizedStatusEffectMissionLogic.TryCreate(
                    torLogic,
                    _settings.FullRescanIntervalMs / 1000f);

                if (replacement == null)
                {
                    StabilityLog.Event("OPTIMIZATION_FALLBACK", "TOR status-effect API did not match the expected contract; original TOR logic remains active.");
                    return;
                }

                mission.RemoveMissionBehavior(torLogic);
                mission.AddMissionBehavior(replacement);

                StabilityLog.Event(
                    "OPTIMIZATION_ACTIVE",
                    "Replaced TOR StatusEffectMissionLogic; full-rescan interval=" + _settings.FullRescanIntervalMs + "ms.");
            }
            catch (Exception ex)
            {
                StabilityLog.Event("OPTIMIZATION_ERROR", ex.ToString());
            }
        }

        private static bool IsCoopActive()
        {
            try
            {
                return ModuleHelper.GetActiveModules().Any(
                    module => module != null && string.Equals(module.Id, "Coop", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }
}
