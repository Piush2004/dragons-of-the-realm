using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Emberbond
{
    /// <summary>
    /// Chooses the graphics tier at start-up. On a discrete gaming GPU everything stays on (volumetric clouds, volumetric
    /// fog, screen-space reflections, long shadows). On integrated graphics the costliest effects are switched off so the
    /// game stays playable; the player can override with -quality high|low on the command line.
    /// </summary>
    public static class QualityGovernor
    {
        public static bool IsWeakGpu()
        {
            string n = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
            if (n.Contains("rtx") || n.Contains("gtx") || n.Contains("geforce") || n.Contains("quadro")) return false;
            if (n.Contains("radeon rx") || n.Contains("radeon pro") || n.Contains("arc a") || n.Contains("arc b")) return false;
            return true;                                    // integrated, software, or unknown
        }

        public static void Apply()
        {
            bool weak = IsWeakGpu();
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-quality") continue;
                if (args[i + 1] == "high") weak = false;
                if (args[i + 1] == "low") weak = true;
            }
            Debug.Log("[Emberbond] graphics: " + SystemInfo.graphicsDeviceName + " -> " + (weak ? "reduced" : "full") + " quality");
            if (!weak) return;

            Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (Volume v in volumes)
            {
                VolumeProfile p = v.sharedProfile;
                if (p == null) continue;
                VolumetricClouds vc;
                if (p.TryGet(out vc)) vc.enable.value = false;
                Fog fog;
                if (p.TryGet(out fog)) fog.enableVolumetricFog.value = false;
                ScreenSpaceReflection ssr;
                if (p.TryGet(out ssr)) ssr.enabled.value = false;
                ScreenSpaceAmbientOcclusion ao;
                if (p.TryGet(out ao)) ao.intensity.value = 0.6f;
                HDShadowSettings sh;
                if (p.TryGet(out sh)) sh.maxShadowDistance.value = 160f;
            }
            Terrain t = Terrain.activeTerrain;
            if (t != null)
            {
                t.treeDistance = 500f;
                t.treeBillboardDistance = 250f;
                t.detailObjectDistance = 50f;
            }
            QualitySettings.lodBias = 0.7f;
        }
    }
}
