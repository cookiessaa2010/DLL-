using System;
using System.Globalization;

namespace KaiTORLoadMonitor
{
    internal sealed class RglSignal
    {
        public string Stage { get; set; }
        public string ShaderKind { get; set; }
        public double? EngineLoadingSeconds { get; set; }
        public bool MenuReadyHint { get; set; }
        public bool Significant { get; set; }
    }

    internal static class RglTelemetry
    {
        internal static RglSignal Analyze(string line)
        {
            var signal = new RglSignal();
            if (string.IsNullOrWhiteSpace(line)) return signal;

            var text = line.Trim();

            if (Contains(text, "Loading Time:"))
            {
                signal.Stage = "Engine: startup complete";
                signal.EngineLoadingSeconds = ParseLoadingSeconds(text);
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "Finished All"))
            {
                signal.Stage = "Engine: startup complete";
                signal.MenuReadyHint = true;
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "TORInitialScreen::HandleActivate") || Contains(text, "TORInitialScreen::HandleResume"))
            {
                signal.Stage = "TOR: главное меню";
                signal.MenuReadyHint = true;
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "gpu_morph_mapping"))
            {
                signal.Stage = "GPU morph / персонажи";
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "Missing shader from sack:") || Contains(text, "compile_shader:"))
            {
                signal.Stage = "Runtime shaders";
                signal.ShaderKind = DetectShaderKind(text);
                if (!string.IsNullOrWhiteSpace(signal.ShaderKind))
                    signal.Stage += ": " + signal.ShaderKind;
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "rglShader_manager::read_compressed_shader_cache_package"))
            {
                signal.Stage = "Shader cache package";
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "rglTerrain_shader_generator") || Contains(text, "Bake Terrain"))
            {
                signal.Stage = "Terrain / scene shaders";
                signal.ShaderKind = "terrain";
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "Opening new mission"))
            {
                signal.Stage = "Scene / mission loading";
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "NAV_MESH:"))
            {
                signal.Stage = "NavMesh";
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "Trying to make partial read on compressed asset data"))
            {
                signal.Stage = "Asset streaming";
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "Loading xml file:") || Contains(text, " opening ") || text.StartsWith("opening ", StringComparison.OrdinalIgnoreCase))
            {
                signal.Stage = "XML / module data";
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "reading physics_material") || Contains(text, "reading sound_files"))
            {
                signal.Stage = "Module resources";
                signal.Significant = true;
                return signal;
            }

            if (Contains(text, "Unable to find particle system") || Contains(text, "Emitter hierarchy does not match"))
            {
                signal.Stage = "TOR effects / particles";
                signal.Significant = true;
                return signal;
            }

            return signal;
        }

        internal static bool SelfTest()
        {
            var terrain = Analyze("[00:00:00.000] Missing shader from sack: pbr_terrain_gbuffer");
            var load = Analyze("Loading Time: 85.547 secs");
            var morph = Analyze("gpu_morph_mapping: 3557866");
            return terrain.Significant && terrain.ShaderKind == "terrain" &&
                   load.EngineLoadingSeconds.HasValue && Math.Abs(load.EngineLoadingSeconds.Value - 85.547) < 0.001 &&
                   morph.Stage == "GPU morph / персонажи";
        }

        private static string DetectShaderKind(string text)
        {
            if (Contains(text, "pbr_terrain")) return "terrain";
            if (Contains(text, "pbr_metallic")) return "material/weapon";
            if (Contains(text, "shadowmap")) return "shadow";
            if (Contains(text, "particle")) return "particle";
            if (Contains(text, "decal")) return "decal";
            return "generic";
        }

        private static double? ParseLoadingSeconds(string text)
        {
            var marker = text.IndexOf("Loading Time:", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return null;
            marker += "Loading Time:".Length;
            while (marker < text.Length && char.IsWhiteSpace(text[marker])) marker++;
            var end = marker;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == ',')) end++;
            if (end <= marker) return null;
            double value;
            var raw = text.Substring(marker, end - marker).Replace(',', '.');
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? (double?)value : null;
        }

        private static bool Contains(string text, string value)
        {
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
