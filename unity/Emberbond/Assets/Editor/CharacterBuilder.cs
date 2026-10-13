using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Emberbond.EditorTools
{
    /// <summary>Builds the rider prefab (HDRP materials on the Blender rig) into Resources so the game can spawn it.</summary>
    public static class CharacterBuilder
    {
        private static Material Lit(string name, Color c, float smooth, float metal)
        {
            Material m = new Material(Shader.Find("HDRP/Lit"));
            m.name = "rider_" + name;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (name == "cloak" || name == "cloth")
            {
                m.SetFloat("_DoubleSidedEnable", 1f);
                m.SetFloat("_DoubleSidedNormalMode", 0f);
            }
            HDMaterial.ValidateMaterial(m);
            string dir = "Assets/Resources/Characters";
            Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(m, dir + "/rider_" + name + ".mat");
            return m;
        }

        public static void Build()
        {
            AssetDatabase.Refresh();
            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/rider.fbx");
            if (src == null) { Debug.LogWarning("[Emberbond] rider.fbx missing"); return; }
            Dictionary<string, Material> mats = new Dictionary<string, Material>
            {
                { "skin", Lit("skin", new Color(0.72f, 0.56f, 0.46f), 0.35f, 0f) },
                { "cloth", Lit("cloth", new Color(0.13f, 0.11f, 0.11f), 0.2f, 0f) },
                { "leather", Lit("leather", new Color(0.27f, 0.16f, 0.09f), 0.3f, 0f) },
                { "steel", Lit("steel", new Color(0.62f, 0.64f, 0.67f), 0.75f, 1f) },
                { "hair", Lit("hair", new Color(0.88f, 0.87f, 0.82f), 0.4f, 0f) },
                { "cloak", Lit("cloak", new Color(0.07f, 0.055f, 0.05f), 0.15f, 0f) },
                { "gold", Lit("gold", new Color(0.78f, 0.6f, 0.22f), 0.8f, 1f) },
            };
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            foreach (SkinnedMeshRenderer r in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] != null ? ms[i].name : "";
                    foreach (KeyValuePair<string, Material> kv in mats)
                        if (n == kv.Key || n.StartsWith(kv.Key)) { ms[i] = kv.Value; break; }
                }
                r.sharedMaterials = ms;
                r.updateWhenOffscreen = true;
            }
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "Rider";
            PrefabUtility.SaveAsPrefabAsset(inst, "Assets/Resources/Rider.prefab");
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            Debug.Log("[Emberbond] rider prefab built");
        }
    }
}
