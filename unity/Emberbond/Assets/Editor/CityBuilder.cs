using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Emberbond.EditorTools
{
    /// <summary>Finds a coastal hill, levels it and lays out Hearthmoor: castle keep, walled streets, harbour and ruins.</summary>
    public static class CityBuilder
    {
        private const string Dir = "Assets/Art/City";
        private const string PrefabDir = "Assets/Art/City/Prefabs";
        public static Vector2 SiteUV = new Vector2(-1f, -1f);     // normalised terrain coordinates
        public static float PlateauHeight = 106f;                  // metres above the bottom of the world (sea is 80)
        public const float PlateauRadius = 200f;
        public const float FalloffRadius = 340f;

        private static bool _found;

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Picks the flattest coastal land with a gentle rise, away from the volcano.</summary>
        public static void FindSite()
        {
            if (_found) return;
            float best = float.MinValue;
            float size = IslandBuilder.WorldSize;
            for (float v = 0.12f; v <= 0.88f; v += 0.01f)
                for (float u = 0.12f; u <= 0.88f; u += 0.01f)
                {
                    float e = IslandBuilder.Elevation(u, v);
                    if (e < 14f || e > 50f) continue;
                    // reject sites near the volcano
                    if (Vector2.Distance(new Vector2(u, v), new Vector2(0.62f, 0.40f)) < 0.25f) continue;
                    // flatness over a ring, and the sea within a few hundred metres
                    float minE = e, maxE = e; int sea = 0;
                    for (int k = 0; k < 12; k++)
                    {
                        float a = k * Mathf.PI / 6f;
                        float r = 190f / size;
                        float e2 = IslandBuilder.Elevation(u + Mathf.Cos(a) * r, v + Mathf.Sin(a) * r);
                        minE = Mathf.Min(minE, e2); maxE = Mathf.Max(maxE, e2);
                        float e3 = IslandBuilder.Elevation(u + Mathf.Cos(a) * r * 2.4f, v + Mathf.Sin(a) * r * 2.4f);
                        if (e3 < 1f) sea++;
                    }
                    if (minE < 2f) continue;                      // plateau must be on land
                    float score = -(maxE - minE) * 1.0f + Mathf.Min(sea, 5) * 6f - Mathf.Abs(e - 30f) * 0.5f;
                    if (score > best) { best = score; SiteUV = new Vector2(u, v); PlateauHeight = IslandBuilder.SeaLevel + Mathf.Clamp(e, 18f, 40f); }
                }
            _found = true;
            Debug.Log("[Emberbond] city site uv=" + SiteUV + " height=" + PlateauHeight);
        }

        public static Vector3 SiteWorld()
        {
            float size = IslandBuilder.WorldSize;
            return new Vector3(SiteUV.x * size - size * 0.5f, PlateauHeight, SiteUV.y * size - size * 0.5f);
        }

        /// <summary>Blends the heightmap toward a flat plateau under the city.</summary>
        public static void Flatten(float[,] heights)
        {
            FindSite();
            int n = IslandBuilder.HeightRes;
            float size = IslandBuilder.WorldSize;
            float cell = size / (n - 1);
            Vector2 c = new Vector2(SiteUV.x * size, SiteUV.y * size);
            float target = PlateauHeight / IslandBuilder.WorldHeight;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x * cell, z * cell), c);
                    if (d > FalloffRadius) continue;
                    float w = 1f - Smooth((d - PlateauRadius) / (FalloffRadius - PlateauRadius));
                    if (d <= PlateauRadius) w = 1f;
                    heights[z, x] = Mathf.Lerp(heights[z, x], target, w);
                }
        }

        // ------------------------------------------------------------------ assets

        private static Material Lit(string name, string albedo, string normal, float tileMetres, float smooth, Color? tint = null)
        {
            Material m = new Material(Shader.Find("HDRP/Lit"));
            m.name = name;
            if (albedo != null) m.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + albedo));
            if (normal != null) { m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + normal)); m.SetFloat("_NormalScale", 1f); }
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            if (tint.HasValue) m.SetColor("_BaseColor", tint.Value);
            Vector2 tiling = Vector2.one / tileMetres;
            m.SetTextureScale("_BaseColorMap", tiling);
            m.SetTextureScale("_NormalMap", tiling);
            HDMaterial.ValidateMaterial(m);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.CreateAsset(m, PrefabDir + "/" + name + ".mat");
            return m;
        }

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
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.SaveAndReimport();
            }
        }

        private static GameObject MakePrefab(string fbx, Dictionary<string, Material> mats)
        {
            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/" + fbx + ".fbx");
            if (src == null) return null;
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>())
            {
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    Material found;
                    string n = ms[i] != null ? ms[i].name : "";
                    if (mats.TryGetValue(n, out found)) ms[i] = found;
                    else foreach (KeyValuePair<string, Material> kv in mats) if (n.Contains(kv.Key)) { ms[i] = kv.Value; break; }
                }
                r.sharedMaterials = ms;
                if (r.GetComponent<MeshCollider>() == null && r.GetComponent<MeshFilter>() != null) r.gameObject.AddComponent<MeshCollider>();
            }
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + "/" + fbx + ".prefab");
            Object.DestroyImmediate(inst);
            return prefab;
        }

        // ------------------------------------------------------------------ placement

        private static float Ground(Terrain t, Vector3 p) { return t.SampleHeight(p) + t.transform.position.y; }

        private static GameObject Put(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale = 1f)
        {
            if (prefab == null) return null;
            GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            g.transform.position = pos;
            g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            g.transform.localScale = Vector3.one * scale;
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic);
            return g;
        }

        public static GameObject Place(Terrain terrain)
        {
            ConfigureImports();
            AssetDatabase.Refresh();
            Dictionary<string, Material> mats = new Dictionary<string, Material>
            {
                { "plaster", Lit("city_plaster", "plaster_albedo.png", "plaster_normal.png", 3f, 0.2f) },
                { "roof_tile", Lit("city_roof", "roof_tile_albedo.png", "roof_tile_normal.png", 4f, 0.3f) },
                { "stone", Lit("city_stone", "stone_brick_albedo.png", "stone_brick_normal.png", 4f, 0.2f) },
                { "timber", Lit("city_timber", "timber_albedo.png", "timber_normal.png", 2f, 0.25f) },
                { "dark", Lit("city_dark", null, null, 1f, 0.1f, new Color(0.03f, 0.025f, 0.02f)) },
            };
            GameObject[] houses = new GameObject[4];
            for (int i = 0; i < 4; i++) houses[i] = MakePrefab("house_" + "abcd"[i], mats);
            GameObject tower = MakePrefab("tower_round", mats);
            GameObject wall = MakePrefab("wall_segment", mats);
            GameObject gate = MakePrefab("gate", mats);
            GameObject keep = MakePrefab("keep", mats);
            GameObject pier = MakePrefab("pier", mats);
            GameObject ship = MakePrefab("ship", mats);
            GameObject stall = MakePrefab("market_stall", mats);
            GameObject ruin = MakePrefab("ruin_wall", mats);

            GameObject root = new GameObject("Hearthmoor");
            Vector3 c = SiteWorld();
            float baseY = Ground(terrain, c);
            System.Random rnd = new System.Random(2026);

            // The keep sits at the middle, a little above the street level.
            Put(keep, root.transform, new Vector3(c.x, baseY, c.z), 0f);

            // Which way is the sea? Look for the direction with the lowest ground a few hundred metres out.
            float seaYaw = 0f, lowest = float.MaxValue;
            for (int k = 0; k < 24; k++)
            {
                float a = k * 15f * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 420f;
                float h = Ground(terrain, p);
                if (h < lowest) { lowest = h; seaYaw = k * 15f; }
            }

            // Curtain wall: a ring of segments with a tower every few and a gate toward the sea.
            float wallR = 165f;
            int segs = Mathf.RoundToInt(2f * Mathf.PI * wallR / 10f);
            for (int i = 0; i < segs; i++)
            {
                float a = i * 360f / segs;
                Vector3 p = c + new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad)) * wallR;
                p.y = Ground(terrain, p);
                bool isGate = Mathf.Abs(Mathf.DeltaAngle(a, seaYaw)) < 360f / segs * 0.6f;
                if (isGate) { Put(gate, root.transform, p, a); continue; }
                if (i % 6 == 0) Put(tower, root.transform, p, a);
                else Put(wall, root.transform, p, a);
            }

            // Houses: rings of buildings facing the keep, skipping the main street to the gate.
            List<Vector3> placed = new List<Vector3> { c };
            int count = 0;
            for (int attempt = 0; attempt < 4000 && count < 170; attempt++)
            {
                float a = (float)rnd.NextDouble() * 360f;
                float r = 38f + (float)rnd.NextDouble() * (wallR - 52f);
                if (Mathf.Abs(Mathf.DeltaAngle(a, seaYaw)) < 7f) continue;      // main street
                Vector3 p = c + new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad)) * r;
                bool ok = true;
                for (int j = 0; j < placed.Count && ok; j++) if ((placed[j] - p).sqrMagnitude < 11.5f * 11.5f) ok = false;
                if (!ok) continue;
                p.y = Ground(terrain, p);
                // Face the street, with a little variation; houses on the outer ring look outward less.
                float yaw = a + 180f + (float)(rnd.NextDouble() - 0.5) * 30f;
                yaw = Mathf.Round(yaw / 90f) * 90f + (float)(rnd.NextDouble() - 0.5) * 8f;
                Put(houses[rnd.Next(houses.Length)], root.transform, p, yaw, 0.9f + (float)rnd.NextDouble() * 0.25f);
                placed.Add(p);
                count++;
            }

            // Market stalls near the gate road.
            for (int i = 0; i < 8; i++)
            {
                float a = seaYaw + (i % 2 == 0 ? 11f : -11f);
                float r = 60f + i * 12f;
                Vector3 p = c + new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad)) * r;
                p.y = Ground(terrain, p);
                Put(stall, root.transform, p, a + 90f);
            }

            // Harbour: the pier runs out to sea and ships lie at anchor.
            Vector3 dir = new Vector3(Mathf.Sin(seaYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(seaYaw * Mathf.Deg2Rad));
            Vector3 shore = c + dir * (wallR + 20f);
            for (int step = 0; step < 60; step++)
            {
                Vector3 q = c + dir * (wallR + 20f + step * 8f);
                if (Ground(terrain, q) < IslandBuilder.SeaLevel - 1f) { shore = q; break; }
            }
            Put(pier, root.transform, new Vector3(shore.x, IslandBuilder.SeaLevel + 0.9f, shore.z), seaYaw);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = shore + dir * (40f + i * 14f) + side * ((i % 2 == 0 ? 1f : -1f) * (24f + i * 5f));
                Put(ship, root.transform, new Vector3(p.x, IslandBuilder.SeaLevel + 0.4f, p.z), seaYaw + (float)rnd.NextDouble() * 40f - 20f, 1.1f);
            }

            // Ruins on the hills around the city.
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rnd.NextDouble() * 360f;
                float r = wallR + 60f + (float)rnd.NextDouble() * 220f;
                Vector3 p = c + new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad)) * r;
                float h = Ground(terrain, p);
                if (h < IslandBuilder.SeaLevel + 3f) continue;
                p.y = h;
                Put(ruin, root.transform, p, (float)rnd.NextDouble() * 360f);
            }

            Debug.Log("[Emberbond] Hearthmoor placed: " + count + " houses, " + segs + " wall segments, site " + c + ", sea yaw " + seaYaw);
            return root;
        }
    }
}
