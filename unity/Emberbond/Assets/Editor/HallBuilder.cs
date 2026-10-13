using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Emberbond.EditorTools
{
    /// <summary>Places the throne hall far above the sea, lights it, and gives it its own cinematic grade.</summary>
    public static class HallBuilder
    {
        public static readonly Vector3 HallOrigin = new Vector3(0f, 1500f, 0f);
        private const string Dir = "Assets/Art/Hall";

        private static Material Lit(string name, Color c, float smooth, float metal, Color? emissive = null)
        {
            Material m = new Material(Shader.Find("HDRP/Lit"));
            m.name = "hall_" + name;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            HDMaterial.ValidateMaterial(m);
            Directory.CreateDirectory(Dir + "/Materials");
            AssetDatabase.CreateAsset(m, Dir + "/Materials/hall_" + name + ".mat");
            return m;
        }

        private static Material CityMat(string n)
        {
            return AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/City/Prefabs/" + n + ".mat");
        }

        public static void Place()
        {
            AssetDatabase.Refresh();
            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/throne_hall.fbx");
            if (src == null) { Debug.LogWarning("[Emberbond] throne_hall.fbx missing"); return; }
            Material stone = CityMat("city_stone") ?? Lit("stone", new Color(0.4f, 0.38f, 0.35f), 0.3f, 0f);
            Material timber = CityMat("city_timber") ?? Lit("timber", new Color(0.3f, 0.2f, 0.12f), 0.3f, 0f);
            Dictionary<string, Material> mats = new Dictionary<string, Material>
            {
                { "stone", stone },
                { "timber", timber },
                { "banner", Lit("banner", new Color(0.42f, 0.04f, 0.04f), 0.15f, 0f) },
                { "blade", Lit("blade", new Color(0.32f, 0.33f, 0.36f), 0.82f, 1f) },
                { "gold", Lit("gold", new Color(0.78f, 0.6f, 0.2f), 0.8f, 1f) },
                { "dark", Lit("dark", new Color(0.02f, 0.02f, 0.025f), 0.1f, 0f) },
            };
            GameObject hall = (GameObject)PrefabUtility.InstantiatePrefab(src);
            hall.name = "ThroneHall";
            hall.transform.position = HallOrigin;
            foreach (Renderer r in hall.GetComponentsInChildren<Renderer>())
            {
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] != null ? ms[i].name : "";
                    foreach (KeyValuePair<string, Material> kv in mats) if (n.StartsWith(kv.Key)) { ms[i] = kv.Value; break; }
                }
                r.sharedMaterials = ms;
            }

            // Warm torchlight down the hall (Unity space: the throne is toward -Z, the door toward +Z).
            for (int i = 0; i < 6; i++)
            {
                float z = -22f + i * 9f;
                foreach (float x in new[] { -8f, 8f })
                {
                    GameObject lg = new GameObject("Torch");
                    lg.transform.SetParent(hall.transform, false);
                    lg.transform.localPosition = new Vector3(x * 0.82f, 3.9f, z);
                    Light l = lg.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(1f, 0.62f, 0.3f);
                    l.range = 26f;
                    l.intensity = 90000f;
                    l.shadows = LightShadows.None;
                }
            }
            GameObject key = new GameObject("ThroneLight");
            key.transform.SetParent(hall.transform, false);
            key.transform.localPosition = new Vector3(0f, 9f, -20f);
            Light kl = key.AddComponent<Light>();
            kl.type = LightType.Spot; kl.spotAngle = 70f; kl.range = 60f; kl.intensity = 700000f;
            kl.color = new Color(1f, 0.85f, 0.7f);
            key.transform.rotation = Quaternion.Euler(70f, 0f, 0f);

            // Local volume: brighter interior exposure, shallow depth of field and a warm grade.
            GameObject vg = new GameObject("HallVolume");
            vg.transform.SetParent(hall.transform, false);
            vg.transform.localPosition = new Vector3(0f, 7f, 0f);
            BoxCollider box = vg.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(30f, 20f, 70f);
            Volume vol = vg.AddComponent<Volume>();
            vol.isGlobal = false; vol.priority = 10f; vol.blendDistance = 3f;
            VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
            Exposure ex = p.Add<Exposure>(true);
            ex.mode.Override(ExposureMode.Fixed);
            ex.fixedExposure.Override(10.6f);
            DepthOfField dof = p.Add<DepthOfField>(true);
            dof.focusMode.Override(DepthOfFieldMode.Manual);
            dof.nearFocusStart.Override(0f); dof.nearFocusEnd.Override(1.5f);
            dof.farFocusStart.Override(14f); dof.farFocusEnd.Override(60f);
            dof.nearMaxBlur = 2f; dof.farMaxBlur = 5f;
            ColorAdjustments ca = p.Add<ColorAdjustments>(true);
            ca.colorFilter.Override(new Color(1f, 0.93f, 0.85f));
            ca.saturation.Override(-6f);
            ca.contrast.Override(18f);
            Vignette vi = p.Add<Vignette>(true);
            vi.intensity.Override(0.35f);
            Bloom bl = p.Add<Bloom>(true);
            bl.intensity.Override(0.35f); bl.threshold.Override(0.9f);
            Fog fog = p.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.meanFreePath.Override(90f);
            fog.baseHeight.Override(0f);
            fog.albedo.Override(new Color(0.55f, 0.45f, 0.38f));
            AssetDatabase.CreateAsset(p, "Assets/Scenes/HallVolume.asset");
            foreach (VolumeComponent vc in p.components) { vc.name = vc.GetType().Name; AssetDatabase.AddObjectToAsset(vc, p); }
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            vol.sharedProfile = p;
            Debug.Log("[Emberbond] throne hall placed at " + HallOrigin);
        }
    }
}
