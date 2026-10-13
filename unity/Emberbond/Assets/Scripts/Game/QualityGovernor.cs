using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Emberbond
{
    /// <summary>
    /// Holds the game at 60 frames per second. It does two things:
    ///  1. Picks a graphics tier at start-up. A discrete gaming GPU keeps everything on; integrated graphics switch off the
    ///     costliest effects (volumetric clouds and fog, SSR, long shadows) and shorten tree and detail distances.
    ///  2. Runs a dynamic-resolution controller every frame: when the frame takes longer than 16.7 ms it lowers the render
    ///     scale (the picture is upscaled with a sharpening filter), and raises it again when there is headroom.
    /// Command line: -quality high|low, -renderscale 0.5..1 to pin the scale, -fps N to change the target.
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

        private static string Arg(string name)
        {
            string[] a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static void Apply()
        {
            bool weak = IsWeakGpu();
            if (GameSettings.Quality == 1) weak = false; else if (GameSettings.Quality == 2) weak = true;
            string q = Arg("-quality");
            if (q == "high") weak = false;
            if (q == "low") weak = true;
            int target = 60;
            int parsed;
            if (int.TryParse(Arg("-fps") ?? "", out parsed) && parsed >= 20) target = parsed;

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = target;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Time.fixedDeltaTime = 1f / 60f;

            Debug.Log("[Emberbond] graphics: " + SystemInfo.graphicsDeviceName + " -> " + (weak ? "reduced" : "full") + " quality, target " + target + " fps");

            Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (Volume v in volumes)
            {
                VolumeProfile p = v.sharedProfile;
                if (p == null) continue;
                HDShadowSettings sh;
                if (p.TryGet(out sh))
                {
                    sh.maxShadowDistance.value = weak ? 140f : 300f;
                    sh.cascadeShadowSplitCount.value = weak ? 2 : 3;
                }
                if (!weak) continue;
                VolumetricClouds vc;
                if (p.TryGet(out vc)) vc.enable.value = false;
                Fog fog;
                if (p.TryGet(out fog)) fog.enableVolumetricFog.value = false;
                ScreenSpaceReflection ssr;
                if (p.TryGet(out ssr)) ssr.enabled.value = false;
                ScreenSpaceAmbientOcclusion ao;
                if (p.TryGet(out ao)) ao.active = false;
                Bloom bl;
                if (p.TryGet(out bl)) bl.quality.value = 0;
            }

            Light sun = null;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                HDAdditionalLightData d = sun.GetComponent<HDAdditionalLightData>();
                if (d != null) d.SetShadowResolution(weak ? 1024 : 2048);
            }

            Terrain t = Terrain.activeTerrain;
            if (t != null)
            {
                t.treeDistance = weak ? 450f : 900f;
                t.treeBillboardDistance = weak ? 200f : 450f;
                t.treeMaximumFullLODCount = weak ? 120 : 300;
                t.detailObjectDistance = weak ? 45f : 90f;
                t.heightmapPixelError = weak ? 6f : 3f;
            }
            QualitySettings.lodBias = weak ? 0.6f : 1f;

            // Dynamic resolution: the controller below drives the scale.
            Camera cam = Camera.main;
            if (cam != null)
            {
                HDAdditionalCameraData hd = cam.GetComponent<HDAdditionalCameraData>();
                if (hd != null) hd.allowDynamicResolution = true;
            }
            PerformanceScaler ps = Object.FindFirstObjectByType<PerformanceScaler>();
            if (ps == null)
            {
                GameObject go = new GameObject("PerformanceScaler");
                Object.DontDestroyOnLoad(go);
                ps = go.AddComponent<PerformanceScaler>();
            }
            ps.TargetMs = 1000f / target;
            ps.MinScale = weak ? 0.45f : 0.6f;
            float fixedScale;
            if (float.TryParse(Arg("-renderscale") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fixedScale))
                ps.Pinned = Mathf.Clamp(fixedScale, 0.3f, 1f);
            else if (GameSettings.RenderScale > 0)
                ps.Pinned = GameSettings.RenderScale / 100f;
        }
    }

    public sealed class PerformanceScaler : MonoBehaviour
    {
        public float TargetMs = 16.6f;
        public float MinScale = 0.5f;
        public float Pinned = -1f;                 // >0 holds the scale fixed (for measurement)
        public float Scale = 1f;
        private float _smooth = 16f;
        private float _cooldown;

        private void Awake()
        {
            DynamicResolutionHandler.SetDynamicResScaler(() => Scale * 100f, DynamicResScalePolicyType.ReturnsPercentage);
        }

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > 250f) return;                 // ignore loading hitches
            _smooth = Mathf.Lerp(_smooth, ms, 0.12f);
            if (Pinned > 0f) { Scale = Pinned; return; }
            _cooldown -= Time.unscaledDeltaTime;
            if (_cooldown > 0f) return;
            if (_smooth > TargetMs * 1.06f)
            {
                Scale = Mathf.Max(MinScale, Scale - Mathf.Clamp((_smooth - TargetMs) / TargetMs * 0.35f, 0.02f, 0.12f));
                _cooldown = 0.15f;
            }
            else if (_smooth < TargetMs * 0.82f && Scale < 1f)
            {
                Scale = Mathf.Min(1f, Scale + 0.02f);
                _cooldown = 0.4f;
            }
        }
    }
}
