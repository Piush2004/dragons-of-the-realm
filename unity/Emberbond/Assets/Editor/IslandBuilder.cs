using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Emberbond.EditorTools
{
    /// <summary>
    /// Builds the Cinder Isles prototype island: procedural terrain with PBR layers, physically based sky,
    /// volumetric clouds and fog, ocean, sun, camera and a dragon. Run from the command line:
    ///   Unity.exe -batchmode -projectPath Emberbond -executeMethod Emberbond.EditorTools.IslandBuilder.Build -quit
    /// </summary>
    public static class IslandBuilder
    {
        public const string ScenePath = "Assets/Scenes/Island.unity";
        public const float WorldSize = 3000f;      // metres per side
        public const float WorldHeight = 700f;
        public const float SeaLevel = 80f;
        public const int HeightRes = 1025;
        public const int AlphaRes = 1024;

        // The volcano sits off-centre in normalised terrain coordinates.
        private static readonly Vector2 Volcano = new Vector2(0.62f, 0.40f);

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static float Fbm(float x, float y, int octaves, float lac, float gain, float seed)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Mathf.PerlinNoise(seed + x * freq, seed * 0.7f + y * freq);
                norm += amp; amp *= gain; freq *= lac;
            }
            return sum / norm;
        }

        private static float Ridged(float x, float y, int octaves, float seed)
        {
            float sum = 0f, amp = 0.55f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(seed + x * freq, seed * 1.3f + y * freq) - 1f);
                sum += amp * n * n; norm += amp; amp *= 0.5f; freq *= 2.1f;
            }
            return sum / norm;
        }

        /// <summary>Elevation in metres above sea level for normalised coordinates u,v in 0..1.</summary>
        public static float Elevation(float u, float v)
        {
            float dx = u - 0.5f, dz = v - 0.5f;
            float warp = Fbm(u * 3.1f, v * 3.1f, 4, 2f, 0.5f, 3.3f) - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dz * dz) * 2f + warp * 0.55f;
            float island = 1f - Smooth(0.38f, 0.92f, r);

            float lowland = Fbm(u * 9f, v * 9f, 5, 2f, 0.5f, 7.1f);
            float hills = Fbm(u * 5f, v * 5f, 5, 2f, 0.5f, 1.9f);
            float range = Ridged(u * 4f, v * 4f, 6, 5.5f);

            float e = island * (6f + 40f * lowland + 90f * hills * hills);
            e += Mathf.Pow(island, 1.6f) * range * 230f * Smooth(0.35f, 0.75f, hills);

            // The volcano: a broad cone with a crater and a lava-scarred rim.
            float vx = u - Volcano.x, vz = v - Volcano.y;
            float d = Mathf.Sqrt(vx * vx + vz * vz);
            float cone = Mathf.Exp(-Mathf.Pow(d / 0.17f, 1.7f));
            float crater = Smooth(0.045f, 0.0f, d);
            float rim = 1f - Mathf.Abs(Smooth(0.0f, 0.075f, d) - 0.5f) * 2f;
            e += cone * 430f * (0.85f + 0.3f * Fbm(u * 24f, v * 24f, 4, 2f, 0.55f, 9.4f));
            e -= crater * 150f;
            e += Mathf.Max(0f, rim) * 14f * Smooth(0.0f, 0.2f, cone);

            // Beyond the coast the sea floor falls away.
            float shelf = (1f - island);
            e -= shelf * (30f + 40f * Smooth(0.7f, 1.0f, r));
            return e;
        }

        public static TerrainData BuildTerrainData(out float[,] heights)
        {
            heights = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                {
                    float u = x / (float)(HeightRes - 1), v = z / (float)(HeightRes - 1);
                    float y = SeaLevel + Elevation(u, v);
                    heights[z, x] = Mathf.Clamp01(y / WorldHeight);
                }
            CityBuilder.Flatten(heights);
            TerrainData td = new TerrainData();
            td.heightmapResolution = HeightRes;
            td.size = new Vector3(WorldSize, WorldHeight, WorldSize);
            td.SetHeights(0, 0, heights);
            td.alphamapResolution = AlphaRes;
            td.baseMapResolution = 1024;
            return td;
        }

        private static TerrainLayer MakeLayer(string name, float tileMeters, float normalScale, float smoothness)
        {
            string dir = "Assets/Art/Terrain";
            TerrainLayer layer = new TerrainLayer();
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + name + "_albedo.png");
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + name + "_normal.png");
            layer.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + name + "_mask.png");
            layer.tileSize = new Vector2(tileMeters, tileMeters);
            layer.normalScale = normalScale;
            layer.smoothness = smoothness;
            layer.metallic = 0f;
            Directory.CreateDirectory("Assets/Art/Terrain/Layers");
            AssetDatabase.CreateAsset(layer, "Assets/Art/Terrain/Layers/" + name + ".terrainlayer");
            return layer;
        }

        private static void ConfigureTextureImport()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Terrain" });
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool normal = path.EndsWith("_normal.png");
                bool albedo = path.EndsWith("_albedo.png");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = albedo;
                ti.maxTextureSize = 2048;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.filterMode = FilterMode.Trilinear;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
        }

        private static float[,,] BuildSplat(float[,] heights, int layerCount)
        {
            float[,,] a = new float[AlphaRes, AlphaRes, layerCount];
            float cell = WorldSize / (HeightRes - 1);
            for (int z = 0; z < AlphaRes; z++)
            {
                for (int x = 0; x < AlphaRes; x++)
                {
                    float u = x / (float)(AlphaRes - 1), v = z / (float)(AlphaRes - 1);
                    int hx = Mathf.Clamp(Mathf.RoundToInt(u * (HeightRes - 1)), 1, HeightRes - 2);
                    int hz = Mathf.Clamp(Mathf.RoundToInt(v * (HeightRes - 1)), 1, HeightRes - 2);
                    float y = heights[hz, hx] * WorldHeight;
                    float dxh = (heights[hz, hx + 1] - heights[hz, hx - 1]) * WorldHeight / (2f * cell);
                    float dzh = (heights[hz + 1, hx] - heights[hz - 1, hx]) * WorldHeight / (2f * cell);
                    float slope = Mathf.Atan(Mathf.Sqrt(dxh * dxh + dzh * dzh)) * Mathf.Rad2Deg;
                    float elev = y - SeaLevel;
                    float n1 = Fbm(u * 38f, v * 38f, 4, 2f, 0.5f, 12.7f);
                    float n2 = Fbm(u * 11f, v * 11f, 3, 2f, 0.5f, 4.4f);
                    float vd = Mathf.Sqrt((u - Volcano.x) * (u - Volcano.x) + (v - Volcano.y) * (v - Volcano.y));

                    float sand = Smooth(9f + n1 * 6f, 0f, elev) ;
                    float rock = Smooth(26f + n1 * 8f, 40f + n1 * 6f, slope);
                    float ash = Smooth(0.2f + n1 * 0.03f, 0.1f, vd);
                    float snow = Smooth(300f + n2 * 80f, 360f + n2 * 80f, elev) * (1f - rock * 0.7f);
                    float dirt = Smooth(0.55f, 0.75f, n2) * 0.55f;
                    float grass = 1f;

                    float gShare = 1f - Mathf.Clamp01(rock + sand + ash + snow);
                    float w0 = grass * gShare * (1f - dirt);          // grass
                    float w1 = rock;                                  // rock
                    float w2 = sand * (1f - rock);                    // sand
                    float w3 = ash * (1f - snow);                     // ash
                    float w4 = snow;                                  // snow
                    float w5 = grass * gShare * dirt;                 // dirt
                    float s = w0 + w1 + w2 + w3 + w4 + w5;
                    if (s < 1e-4f) { w0 = 1f; s = 1f; }
                    a[z, x, 0] = w0 / s; a[z, x, 1] = w1 / s; a[z, x, 2] = w2 / s;
                    a[z, x, 3] = w3 / s; a[z, x, 4] = w4 / s; a[z, x, 5] = w5 / s;
                }
            }
            return a;
        }

        private static void EnableHdrpFeatures()
        {
            string[] guids = AssetDatabase.FindAssets("t:HDRenderPipelineAsset");
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                HDRenderPipelineAsset asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
                if (asset == null) continue;
                RenderPipelineSettings s = asset.currentPlatformRenderPipelineSettings;
                s.supportWater = true;
                s.supportVolumetricClouds = true;
                s.supportSSR = true;
                s.supportSSAO = true;
                s.supportSSGI = true;
                s.supportVolumetrics = true;
                s.supportDistortion = true;
                s.supportTransparentDepthPrepass = true;
                s.supportSubsurfaceScattering = true;
                GlobalDynamicResolutionSettings dr = s.dynamicResolutionSettings;
                dr.enabled = true;
                dr.dynResType = DynamicResolutionType.Software;
                dr.upsampleFilter = DynamicResUpscaleFilter.CatmullRom;
                dr.minPercentage = 40f;
                dr.maxPercentage = 100f;
                s.dynamicResolutionSettings = dr;
                asset.currentPlatformRenderPipelineSettings = s;
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
        }

        private static VolumeProfile BuildVolumeProfile()
        {
            VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();

            VisualEnvironment env = p.Add<VisualEnvironment>(true);
            env.skyType.Override((int)SkyType.PhysicallyBased);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            PhysicallyBasedSky sky = p.Add<PhysicallyBasedSky>(true);
            sky.atmosphericScattering.Override(true);
            sky.groundTint.Override(new Color(0.34f, 0.36f, 0.4f));
            sky.aerosolDensity.Override(0.004f);
            sky.aerosolTint.Override(new Color(0.85f, 0.88f, 0.92f));
            sky.airTint.Override(new Color(0.78f, 0.88f, 1f));
            sky.multiplier.Override(1.0f);

            env.cloudType.Override((int)CloudType.CloudLayer);
            CloudLayer layer = p.Add<CloudLayer>(true);
            layer.opacity.Override(0.9f);
            layer.layers.Override(CloudMapMode.Single);
            Texture2D cloudMap = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/Texture/DefaultCloudMap.png");
            if (cloudMap != null) layer.layerA.cloudMap.Override(cloudMap);
            else Debug.LogWarning("[Emberbond] default cloud map not found");
            layer.layerA.opacityR.Override(1f);
            layer.layerA.tint.Override(new Color(1f, 0.97f, 0.92f));
            layer.layerA.altitude.Override(7000f);
            layer.layerA.exposure.Override(0.3f);

            VolumetricClouds clouds = p.Add<VolumetricClouds>(true);
            clouds.enable.Override(true);
            clouds.cloudPreset = VolumetricClouds.CloudPresets.Sparse;
            clouds.bottomAltitude.Override(1100f);
            clouds.altitudeRange.Override(1800f);
            clouds.densityMultiplier.Override(0.5f);
            clouds.shapeFactor.Override(0.75f);
            clouds.shadows.Override(true);

            Fog fog = p.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.enableVolumetricFog.Override(true);
            fog.meanFreePath.Override(5200f);
            fog.baseHeight.Override(SeaLevel + 20f);
            fog.maximumHeight.Override(SeaLevel + 260f);
            fog.albedo.Override(new Color(0.8f, 0.86f, 0.96f));

            Exposure exp = p.Add<Exposure>(true);
            exp.mode.Override(ExposureMode.Fixed);          // automatic exposure is fooled by the bright sky
            exp.fixedExposure.Override(11.2f);

            Tonemapping tm = p.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);

            Bloom bloom = p.Add<Bloom>(true);
            bloom.intensity.Override(0.18f);
            bloom.threshold.Override(1.0f);

            ScreenSpaceAmbientOcclusion ao = p.Add<ScreenSpaceAmbientOcclusion>(true);
            ao.intensity.Override(1.0f);

            ScreenSpaceReflection ssr = p.Add<ScreenSpaceReflection>(true);
            ssr.enabled.Override(true);

            ColorAdjustments ca = p.Add<ColorAdjustments>(true);
            ca.saturation.Override(8f);
            ca.contrast.Override(8f);

            Vignette vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.22f);

            return p;
        }

        /// <summary>Soft round glow texture and an additive HDRP unlit material for fire, embers and sparks.</summary>
        private static void CreateFxAssets()
        {
            Directory.CreateDirectory("Assets/Resources");
            const int s = 128;
            Texture2D t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            File.WriteAllBytes("Assets/Resources/FireGlow.png", t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset("Assets/Resources/FireGlow.png");
            TextureImporter ti = AssetImporter.GetAtPath("Assets/Resources/FireGlow.png") as TextureImporter;
            if (ti != null) { ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
            Texture2D glow = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/FireGlow.png");

            Material m = new Material(Shader.Find("HDRP/Unlit"));
            m.name = "FireAdditive";
            m.SetTexture("_UnlitColorMap", glow);
            m.SetColor("_UnlitColor", new Color(1f, 0.42f, 0.09f) * 0.55f);
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 1f);               // additive
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_ZWrite", 0f);
            HDMaterial.ValidateMaterial(m);
            AssetDatabase.CreateAsset(m, "Assets/Resources/FireAdditive.mat");
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Emberbond/Build island scene")]
        public static void Build()
        {
            ConfigureTextureImport();
            AssetDatabase.Refresh();
            EnableHdrpFeatures();
            CreateFxAssets();
            CharacterBuilder.Build();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Terrain
            float[,] heights;
            TerrainData td = BuildTerrainData(out heights);
            TerrainLayer[] layers =
            {
                MakeLayer("grass", 7f, 1.0f, 0.05f),
                MakeLayer("rock", 11f, 1.2f, 0.1f),
                MakeLayer("sand", 6f, 0.8f, 0.05f),
                MakeLayer("ash", 9f, 1.0f, 0.06f),
                MakeLayer("snow", 8f, 0.6f, 0.3f),
                MakeLayer("dirt", 5f, 1.0f, 0.05f),
            };
            td.terrainLayers = layers;
            float[,,] splat = BuildSplat(heights, layers.Length);
            td.SetAlphamaps(0, 0, splat);
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.CreateAsset(td, "Assets/Scenes/IslandTerrain.asset");

            GameObject terrainGo = Terrain.CreateTerrainGameObject(td);
            terrainGo.name = "Terrain";
            terrainGo.transform.position = new Vector3(-WorldSize * 0.5f, 0f, -WorldSize * 0.5f);
            Terrain terrain = terrainGo.GetComponent<Terrain>();
            terrain.heightmapPixelError = 2f;
            terrain.drawInstanced = false;
            terrain.basemapDistance = 1500f;
            VegetationBuilder.Scatter(terrain, td, heights, splat).transform.position = Vector3.zero;
            CityBuilder.Place(terrain);
            HallBuilder.Place();

            // Sun
            GameObject sunGo = new GameObject("Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 110000f;                         // lux, HDRP physical units
            sun.color = new Color(1f, 0.9f, 0.78f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(40f, -38f, 0f);
            HDAdditionalLightData sunData = sunGo.AddComponent<HDAdditionalLightData>();
            sunData.EnableShadows(true);
            sunData.SetShadowResolution(4096);

            // Global volume
            GameObject volGo = new GameObject("Global Volume");
            Volume volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            VolumeProfile profile = BuildVolumeProfile();
            AssetDatabase.CreateAsset(profile, "Assets/Scenes/IslandVolume.asset");
            // The overrides must be saved inside the profile asset or they are lost when the scene is serialised.
            foreach (VolumeComponent vc in profile.components)
            {
                vc.name = vc.GetType().Name;
                AssetDatabase.AddObjectToAsset(vc, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            volume.sharedProfile = profile;

            // Ocean
            GameObject waterGo = new GameObject("Ocean");
            waterGo.transform.position = new Vector3(0f, SeaLevel, 0f);
            WaterSurface water = waterGo.AddComponent<WaterSurface>();
            water.surfaceType = WaterSurfaceType.OceanSeaLake;
            water.geometryType = WaterGeometryType.Infinite;

            // Camera
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 12000f;
            cam.fieldOfView = 55f;
            camGo.AddComponent<HDAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(-420f, SeaLevel + 140f, -980f);
            camGo.transform.rotation = Quaternion.Euler(8f, 28f, 0f);

            // Dragon
            GameObject dragonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Dragons/dragon_cinderwing.fbx");
            if (dragonPrefab != null)
            {
                GameObject dragon = (GameObject)PrefabUtility.InstantiatePrefab(dragonPrefab);
                dragon.name = "Dragon";
                dragon.transform.position = new Vector3(-380f, SeaLevel + 70f, -930f);
                dragon.transform.localScale = Vector3.one * 6f;
                DragonMaterials.Apply(dragon, "cinderwing");
            }

            GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Dragons/dragon_brazenhide.fbx");
            if (enemyPrefab != null)
            {
                GameObject enemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
                enemy.name = "EnemyDragon";
                enemy.transform.position = new Vector3(300f, SeaLevel + 260f, -200f);
                enemy.transform.localScale = Vector3.one * 8f;
                DragonMaterials.Apply(enemy, "brazenhide");
            }

            // Runtime helpers
            GameObject runner = new GameObject("ShotRunner");
            runner.AddComponent<ShotRunner>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[Emberbond] island scene built");
        }

        /// <summary>Builds a Windows player (D3D12) containing the island scene.</summary>
        public static void BuildPlayer()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.productName = "Emberbond";
            Directory.CreateDirectory("Builds/Windows");
            BuildPlayerOptions o = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/Emberbond.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(o);
            Debug.Log("[Emberbond] build " + report.summary.result + " errors=" + report.summary.totalErrors + " size=" +
                      (report.summary.totalSize / (1024 * 1024)) + " MB time=" + report.summary.totalTime);
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Enters play mode in the island scene so ShotRunner can take its screenshots.</summary>
        public static void Shoot()
        {
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }
    }

    [InitializeOnLoad]
    public static class ShotWatcher
    {
        static ShotWatcher()
        {
            EditorApplication.update += () =>
            {
                if (!Application.isBatchMode) return;
                string marker = Path.Combine(Directory.GetCurrentDirectory(), "shots", "done.txt");
                if (File.Exists(marker))
                {
                    File.Delete(marker);
                    EditorApplication.Exit(0);
                }
            };
        }
    }

    /// <summary>Gives the imported dragon FBX HDRP/Lit materials built from its generated textures.</summary>
    public static class DragonMaterials
    {
        public static void Apply(GameObject root, string lineage)
        {
            Shader lit = Shader.Find("HDRP/Lit");
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                    string key = n.Contains("eye") ? "eye" : n.Contains("membrane") || n.Contains("wing") ? "membrane"
                        : n.Contains("belly") ? "belly" : n.Contains("back") ? "back" : "skin";
                    string basePath = "Assets/Art/Dragons/Textures/" + lineage + "_" + lineage + "_" + key;
                    Material m = new Material(lit);
                    m.name = lineage + "_" + key;
                    Texture2D color = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath + "_color.png");
                    Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath + "_normal.png");
                    if (color != null) m.SetTexture("_BaseColorMap", color);
                    if (normal != null) { m.SetTexture("_NormalMap", normal); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_NormalScale", 1.2f); }
                    m.SetFloat("_Smoothness", key == "eye" ? 0.9f : (key == "membrane" ? 0.35f : 0.45f));
                    m.SetFloat("_Metallic", 0f);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }
    }
}
