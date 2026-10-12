using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Emberbond.EditorTools
{
    /// <summary>Turns the Blender vegetation FBX files into HDRP prefabs and scatters them over the terrain.</summary>
    public static class VegetationBuilder
    {
        private const string Dir = "Assets/Art/Vegetation";
        private const string PrefabDir = "Assets/Art/Vegetation/Prefabs";

        private static Material _bark, _leaf, _needle, _grass, _rock;

        private static Texture2D Tex(string name) { return AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + name); }

        private static void ConfigureImports()
        {
            foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool normal = path.EndsWith("_normal.png");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal;
                ti.alphaIsTransparency = path.Contains("leaf") || path.Contains("needle") || path.Contains("grass");
                ti.mipmapEnabled = true;
                ti.mipMapsPreserveCoverage = ti.alphaIsTransparency;
                ti.alphaTestReferenceValue = 0.45f;
                ti.anisoLevel = 8;
                ti.SaveAndReimport();
            }
        }

        private static Material Lit(string name, Texture2D color, Texture2D normal, bool cutout, float smooth, Vector2 tiling)
        {
            Material m = new Material(Shader.Find("HDRP/Lit"));
            m.name = name;
            if (color != null) m.SetTexture("_BaseColorMap", color);
            if (normal != null) { m.SetTexture("_NormalMap", normal); m.SetFloat("_NormalScale", 1f); }
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.SetTextureScale("_BaseColorMap", tiling);
            m.SetTextureScale("_NormalMap", tiling);
            if (cutout)
            {
                m.SetFloat("_AlphaCutoffEnable", 1f);
                m.SetFloat("_AlphaCutoff", 0.45f);
                m.SetFloat("_DoubleSidedEnable", 1f);
                m.SetFloat("_DoubleSidedNormalMode", 0f);
                m.SetFloat("_MaterialID", 1f);        // standard (0 would be subsurface scattering without a profile)
            }
            HDMaterial.ValidateMaterial(m);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.CreateAsset(m, PrefabDir + "/" + name + ".mat");
            return m;
        }

        private static GameObject MakePrefab(string fbxName, Material first, Material second)
        {
            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/" + fbxName + ".fbx");
            if (src == null) return null;
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                if (mats.Length > 0) mats[0] = first;
                if (mats.Length > 1) mats[1] = second ?? first;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            GameObject unpacked = inst;
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            string path = PrefabDir + "/" + fbxName + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(unpacked, path);
            Object.DestroyImmediate(unpacked);
            return prefab;
        }

        public static void Prepare(out GameObject[] pines, out GameObject[] oaks, out GameObject[] bushes, out GameObject[] rocks, out Texture2D grassTex)
        {
            ConfigureImports();
            AssetDatabase.Refresh();
            Directory.CreateDirectory(PrefabDir);
            _bark = Lit("bark", Tex("bark_albedo.png"), Tex("bark_normal.png"), false, 0.15f, Vector2.one);
            _leaf = Lit("leaf", Tex("leaf_albedo.png"), null, true, 0.3f, Vector2.one);
            _needle = Lit("needle", Tex("needle_albedo.png"), null, true, 0.25f, Vector2.one);
            _grass = Lit("grass_blade", Tex("grass_albedo.png"), null, true, 0.2f, Vector2.one);
            Texture2D rockA = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Terrain/rock_albedo.png");
            Texture2D rockN = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Terrain/rock_normal.png");
            _rock = Lit("rock_object", rockA, rockN, false, 0.25f, new Vector2(0.35f, 0.35f));

            pines = new GameObject[3]; oaks = new GameObject[3]; bushes = new GameObject[2]; rocks = new GameObject[4];
            for (int i = 0; i < 3; i++) pines[i] = MakePrefab("pine_" + (i + 1), _bark, _needle);
            for (int i = 0; i < 3; i++) oaks[i] = MakePrefab("oak_" + (i + 1), _bark, _leaf);
            for (int i = 0; i < 2; i++) bushes[i] = MakePrefab("bush_" + (i + 1), _bark, _leaf);
            for (int i = 0; i < 4; i++) rocks[i] = MakePrefab("rock_" + (i + 1), _rock, null);
            grassTex = Tex("grass_albedo.png");
            AssetDatabase.SaveAssets();
        }

        private static float Hash(int x, int z, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(s * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>Adds tree instances and grass details to the terrain and returns a root with rock objects.</summary>
        public static GameObject Scatter(Terrain terrain, TerrainData td, float[,] heights, float[,,] splat)
        {
            GameObject[] pines, oaks, bushes, rocks; Texture2D grassTex;
            Prepare(out pines, out oaks, out bushes, out rocks, out grassTex);

            // Tree prototypes: 0-2 pines, 3-5 oaks, 6-7 bushes.
            List<TreePrototype> protos = new List<TreePrototype>();
            foreach (GameObject g in pines) protos.Add(new TreePrototype { prefab = g });
            foreach (GameObject g in oaks) protos.Add(new TreePrototype { prefab = g });
            foreach (GameObject g in bushes) protos.Add(new TreePrototype { prefab = g });
            td.treePrototypes = protos.ToArray();

            int hr = IslandBuilder.HeightRes, ar = IslandBuilder.AlphaRes;
            float size = IslandBuilder.WorldSize;
            List<TreeInstance> trees = new List<TreeInstance>();
            const float step = 7f;
            int n = Mathf.CeilToInt(size / step);
            for (int gz = 0; gz < n; gz++)
            {
                for (int gx = 0; gx < n; gx++)
                {
                    float jx = Hash(gx, gz, 1), jz = Hash(gx, gz, 2);
                    float wx = (gx + jx) * step, wz = (gz + jz) * step;
                    float u = wx / size, v = wz / size;
                    if (u < 0.01f || u > 0.99f || v < 0.01f || v > 0.99f) continue;
                    int hx = Mathf.Clamp(Mathf.RoundToInt(u * (hr - 1)), 1, hr - 2);
                    int hz = Mathf.Clamp(Mathf.RoundToInt(v * (hr - 1)), 1, hr - 2);
                    float y = heights[hz, hx] * IslandBuilder.WorldHeight;
                    float elev = y - IslandBuilder.SeaLevel;
                    if (elev < 5f || elev > 270f) continue;
                    int ax = Mathf.Clamp(Mathf.RoundToInt(u * (ar - 1)), 0, ar - 1);
                    int az = Mathf.Clamp(Mathf.RoundToInt(v * (ar - 1)), 0, ar - 1);
                    float grassW = splat[az, ax, 0] + splat[az, ax, 5] * 0.6f;
                    if (grassW < 0.55f) continue;
                    // Forest density from low-frequency noise.
                    float dens = Mathf.PerlinNoise(u * 7f + 3.1f, v * 7f + 8.7f);
                    dens = Mathf.Clamp01((dens - 0.38f) * 2.2f);
                    float roll = Hash(gx, gz, 3);
                    if (roll > dens * 0.9f) continue;
                    // Cooler, higher ground favours pine.
                    float pineBias = Mathf.Clamp01((elev - 60f) / 160f) + (Mathf.PerlinNoise(u * 4f, v * 4f + 5f) - 0.5f) * 0.8f;
                    bool pine = Hash(gx, gz, 4) < Mathf.Clamp01(pineBias + 0.15f);
                    int proto = pine ? Mathf.FloorToInt(Hash(gx, gz, 5) * 3f) : 3 + Mathf.FloorToInt(Hash(gx, gz, 5) * 3f);
                    if (!pine && Hash(gx, gz, 6) < 0.22f) proto = 6 + Mathf.FloorToInt(Hash(gx, gz, 7) * 2f);   // undergrowth
                    float scale = 0.8f + Hash(gx, gz, 8) * 0.7f;
                    trees.Add(new TreeInstance
                    {
                        position = new Vector3(u, y / IslandBuilder.WorldHeight, v),
                        prototypeIndex = proto,
                        widthScale = scale,
                        heightScale = scale * (0.9f + Hash(gx, gz, 9) * 0.25f),
                        rotation = Hash(gx, gz, 10) * Mathf.PI * 2f,
                        color = Color.white,
                        lightmapColor = Color.white,
                    });
                }
            }
            td.treeInstances = trees.ToArray();
            terrain.treeDistance = 1500f;
            terrain.treeBillboardDistance = 900f;
            terrain.treeCrossFadeLength = 60f;
            terrain.treeMaximumFullLODCount = 400;

            // Grass: a detail layer of crossed-quad tufts where the ground is grassy.
            td.SetDetailResolution(1024, 16);
            DetailPrototype dp = new DetailPrototype
            {
                prototypeTexture = grassTex,
                renderMode = DetailRenderMode.GrassBillboard,
                minWidth = 0.8f, maxWidth = 1.5f, minHeight = 0.6f, maxHeight = 1.2f,
                healthyColor = new Color(0.75f, 0.85f, 0.55f),
                dryColor = new Color(0.78f, 0.7f, 0.42f),
                noiseSpread = 14f,
                useInstancing = false,
            };
            td.detailPrototypes = new[] { dp };
            int[,] layer = new int[1024, 1024];
            for (int z = 0; z < 1024; z++)
                for (int x = 0; x < 1024; x++)
                {
                    float u = x / 1023f, v = z / 1023f;
                    int ax = Mathf.Clamp(Mathf.RoundToInt(u * (ar - 1)), 0, ar - 1);
                    int az = Mathf.Clamp(Mathf.RoundToInt(v * (ar - 1)), 0, ar - 1);
                    float gw = splat[az, ax, 0];
                    int hx = Mathf.Clamp(Mathf.RoundToInt(u * (hr - 1)), 0, hr - 1);
                    int hz = Mathf.Clamp(Mathf.RoundToInt(v * (hr - 1)), 0, hr - 1);
                    float elev = heights[hz, hx] * IslandBuilder.WorldHeight - IslandBuilder.SeaLevel;
                    if (gw > 0.5f && elev > 3f && elev < 260f && Hash(x, z, 11) < 0.55f * gw) layer[z, x] = 1 + (int)(Hash(x, z, 12) * 2f);
                }
            td.SetDetailLayer(0, 0, 0, layer);
            terrain.detailObjectDistance = 120f;
            terrain.detailObjectDensity = 1f;

            // Rocks: scene objects on steep ground and scattered on the beaches.
            GameObject root = new GameObject("Rocks");
            System.Random rnd = new System.Random(77);
            float cell = size / (hr - 1);
            int placed = 0;
            for (int i = 0; i < 6000 && placed < 700; i++)
            {
                float u = (float)rnd.NextDouble(), v = (float)rnd.NextDouble();
                int hx = Mathf.Clamp(Mathf.RoundToInt(u * (hr - 1)), 1, hr - 2);
                int hz = Mathf.Clamp(Mathf.RoundToInt(v * (hr - 1)), 1, hr - 2);
                float y = heights[hz, hx] * IslandBuilder.WorldHeight;
                float elev = y - IslandBuilder.SeaLevel;
                if (elev < 0.5f) continue;
                float dx = (heights[hz, hx + 1] - heights[hz, hx - 1]) * IslandBuilder.WorldHeight / (2f * cell);
                float dz = (heights[hz + 1, hx] - heights[hz - 1, hx]) * IslandBuilder.WorldHeight / (2f * cell);
                float slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                if (slope < 22f && rnd.NextDouble() > 0.05) continue;
                GameObject prefab = rocks[rnd.Next(rocks.Length)];
                if (prefab == null) continue;
                GameObject r = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                r.transform.position = new Vector3(u * size - size * 0.5f, y - 0.15f, v * size - size * 0.5f);
                r.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 12f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 12f);
                float s = 0.6f + (float)rnd.NextDouble() * (slope > 30f ? 5.5f : 2.2f);
                r.transform.localScale = new Vector3(s * (0.8f + (float)rnd.NextDouble() * 0.5f), s, s * (0.8f + (float)rnd.NextDouble() * 0.5f));
                GameObjectUtility.SetStaticEditorFlags(r, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                placed++;
            }
            return root;
        }
    }
}
