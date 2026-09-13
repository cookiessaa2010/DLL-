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
                var node = doc.Root?.Element("BattleStatusOptimization");
                if (node == null)
                {
                    return settings;
                }

                bool enabled;
                if (bool.TryParse((string)node.Attribute("enabled"), out enabled))
                {
                    settings.EnableStatusEffectOptimization = enabled;
                }

                int interval;
                if (int.TryParse((string)node.Attribute("fullRescanIntervalMs"), out interval))
                {
                    settings.FullRescanIntervalMs = Math.Max(50, Math.Min(1000, interval));
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
