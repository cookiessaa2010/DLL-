using System;
using System.IO;
using System.Xml.Linq;
using TaleWorlds.ModuleManager;

namespace KaiTORStability
{
    internal sealed class StabilitySettings
    {
        public bool EnableStatusEffectOptimization { get; private set; } = true;
        public int FullRescanIntervalMs { get; private set; } = 100;
        public bool EnableShaderTelemetry { get; private set; } = true;
        public int ShaderTelemetryIntervalMs { get; private set; } = 500;
        public bool EnableShaderCacheAcceleration { get; private set; } = true;
        public int ShaderCacheSingleLoadoutCopies { get; private set; } = 1;
        public bool DisableGameplayPatchesWhenCoopActive { get; private set; } = true;

        public static StabilitySettings Load()
        {
            var settings = new StabilitySettings();
            try
            {
                var root = ModuleHelper.GetModuleFullPath("KaiTOR_Stability");
                var path = Path.Combine(root, "ModuleData", "KaiTORStability.config.xml");
                if (!File.Exists(path))
                {
                    StabilityLog.Event("CONFIG", "Config file missing; using conservative defaults.");
                    return settings;
                }

                var doc = XDocument.Load(path);
                var rootNode = doc.Root;
                if (rootNode == null) return settings;

                var battleNode = rootNode.Element("BattleStatusOptimization");
                if (battleNode != null)
                {
                    bool enabled;
                    if (bool.TryParse((string)battleNode.Attribute("enabled"), out enabled))
                    {
                        settings.EnableStatusEffectOptimization = enabled;
                    }

                    int interval;
                    if (int.TryParse((string)battleNode.Attribute("fullRescanIntervalMs"), out interval))
                    {
                        settings.FullRescanIntervalMs = Math.Max(50, Math.Min(1000, interval));
                    }
                }

                var shaderNode = rootNode.Element("ShaderTelemetry");
                if (shaderNode != null)
                {
                    bool enabled;
                    if (bool.TryParse((string)shaderNode.Attribute("enabled"), out enabled))
                    {
                        settings.EnableShaderTelemetry = enabled;
                    }

                    int interval;
                    if (int.TryParse((string)shaderNode.Attribute("sampleIntervalMs"), out interval))
                    {
                        settings.ShaderTelemetryIntervalMs = Math.Max(250, Math.Min(5000, interval));
                    }
                }

                var acceleratorNode = rootNode.Element("ShaderCacheAcceleration");
                if (acceleratorNode != null)
                {
                    bool enabled;
                    if (bool.TryParse((string)acceleratorNode.Attribute("enabled"), out enabled))
                    {
                        settings.EnableShaderCacheAcceleration = enabled;
                    }

                    int copies;
                    if (int.TryParse((string)acceleratorNode.Attribute("singleLoadoutCopies"), out copies))
                    {
                        settings.ShaderCacheSingleLoadoutCopies = Math.Max(1, Math.Min(4, copies));
                    }
                }

                var coopNode = rootNode.Element("CoopCompatibility");
                if (coopNode != null)
                {
                    bool disableGameplayPatches;
                    if (bool.TryParse((string)coopNode.Attribute("disableGameplayPatches"), out disableGameplayPatches))
                    {
                        settings.DisableGameplayPatchesWhenCoopActive = disableGameplayPatches;
                    }
                }
            }
            catch (Exception ex)
            {
                StabilityLog.Event("CONFIG_ERROR", ex.ToString());
            }

            return settings;
        }
    }
}
