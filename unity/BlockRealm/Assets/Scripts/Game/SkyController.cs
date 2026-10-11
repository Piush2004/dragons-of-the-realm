using UnityEngine;
using UnityEngine.Rendering;

namespace BlockRealm.Game
{
    /// <summary>
    /// Day and night: sun and moon, sky colours, fog, the shared light values the block and entity shaders read,
    /// and a drifting cloud layer. Time comes from the server (20 ticks per second, 24000 per day).
    /// </summary>
    public sealed class SkyController : MonoBehaviour
    {
        public Light Sun;
        public Camera Cam;

        private Material _skyMaterial;
        private Material _cloudMaterial;
        private Transform _clouds;
        private float _tick = 1000f;
        private float _serverTick = 1000f;
        private Vector2 _wind;
        private float _viewBlocks = 160f;

        public float Daylight { get; private set; }          // 0 night .. 1 day
        public float SunHeight { get; private set; }
        public float TickOfDay { get { return _tick; } }

        public static readonly int IdDaylight = Shader.PropertyToID("_BR_Daylight");
        public static readonly int IdSkyAmbient = Shader.PropertyToID("_BR_SkyAmbient");
        public static readonly int IdTorch = Shader.PropertyToID("_BR_TorchColor");
        public static readonly int IdNight = Shader.PropertyToID("_BR_NightTint");

        public void Init(Camera cam)
        {
            Cam = cam;
            GameObject lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(transform, false);
            Sun = lightGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.shadows = GameSettings.Shadows ? LightShadows.Soft : LightShadows.None;
            Sun.shadowStrength = 0.85f;
            Sun.shadowBias = 0.05f;
            Sun.shadowNormalBias = 0.6f;
            Sun.color = Color.white;
            Sun.intensity = 1.1f;
            RenderSettings.sun = Sun;

            QualitySettings.shadowDistance = 110f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowCascade4Split = new Vector3(0.05f, 0.18f, 0.45f);
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadows = GameSettings.Shadows ? ShadowQuality.All : ShadowQuality.Disable;

            Shader sky = GameAssets.LoadShader("Sky");
            _skyMaterial = new Material(sky) { name = "Sky" };
            _skyMaterial.SetTexture("_SunTex", GameAssets.Load("Sky/sun"));
            _skyMaterial.SetTexture("_MoonTex", GameAssets.Load("Sky/moon"));
            _skyMaterial.SetFloat("_SunSize", 0.11f);
            _skyMaterial.SetFloat("_MoonSize", 0.085f);
            RenderSettings.skybox = _skyMaterial;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = AmbientMode.Flat;

            Shader cloud = GameAssets.LoadShader("Clouds");
            _cloudMaterial = new Material(cloud) { name = "Clouds" };
            _cloudMaterial.SetTexture("_MainTex", GameAssets.Load("Sky/clouds"));
            _cloudMaterial.mainTexture.wrapMode = TextureWrapMode.Repeat;
            _cloudMaterial.mainTexture.filterMode = FilterMode.Point;
            GameObject cg = new GameObject("Clouds");
            cg.transform.SetParent(transform, false);
            MeshFilter mf = cg.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildCloudPlane(3000f);
            MeshRenderer mr = cg.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _cloudMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _clouds = cg.transform;

            Shader.SetGlobalVector(IdTorch, new Vector4(1.0f, 0.74f, 0.42f, 1f));
            ApplyView(GameSettings.ViewDistance);
            Evaluate();
        }

        public void ApplyView(int chunks)
        {
            _viewBlocks = chunks * 16f;
            if (Cam != null) Cam.farClipPlane = Mathf.Max(200f, (chunks + 2) * 16f * 1.5f + 64f);
            RenderSettings.fogStartDistance = _viewBlocks * 0.55f;
            RenderSettings.fogEndDistance = _viewBlocks * 0.98f;
        }

        private static Mesh BuildCloudPlane(float half)
        {
            Mesh m = new Mesh { name = "Cloud plane" };
            m.vertices = new[]
            {
                new Vector3(-half, 0, -half), new Vector3(-half, 0, half), new Vector3(half, 0, half), new Vector3(half, 0, -half),
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            m.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 4f, half * 2f));
            return m;
        }

        public void SetServerTick(int tick)
        {
            _serverTick = tick;
            if (Mathf.Abs(_serverTick - _tick) > 60f && Mathf.Abs(_serverTick - _tick) < WorldConstDay - 60f) _tick = _serverTick;
        }

        private const float WorldConstDay = 24000f;

        private void Update()
        {
            _tick += Time.deltaTime * 20f;
            _serverTick += Time.deltaTime * 20f;
            if (_tick >= WorldConstDay) _tick -= WorldConstDay;
            // Drift toward the server's idea of the time so nobody's day runs ahead.
            float diff = Mathf.DeltaAngle(_tick / WorldConstDay * 360f, _serverTick % WorldConstDay / WorldConstDay * 360f) / 360f * WorldConstDay;
            _tick += diff * Mathf.Clamp01(Time.deltaTime * 0.5f);
            Evaluate();

            if (Cam != null && _clouds != null)
            {
                Vector3 p = Cam.transform.position;
                _clouds.position = new Vector3(p.x, 196f, p.z);
                _wind += new Vector2(1.4f, 0.5f) * Time.deltaTime;
                _cloudMaterial.SetVector("_Scroll", new Vector4(_wind.x, _wind.y, 0, 0));
            }
        }

        private void Evaluate()
        {
            float a = _tick / WorldConstDay * Mathf.PI * 2f;          // 0 = sunrise in the east
            Vector3 toSun = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0.25f).normalized;
            SunHeight = toSun.y;
            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.28f, SunHeight));
            float twilight = Mathf.Clamp01(1f - Mathf.Abs(SunHeight) / 0.32f);          // strongest near the horizon

            Color zenithDay = new Color(0.20f, 0.42f, 0.86f), zenithNight = new Color(0.012f, 0.018f, 0.06f);
            Color horizonDay = new Color(0.66f, 0.80f, 0.96f), horizonNight = new Color(0.04f, 0.06f, 0.12f);
            Color dusk = new Color(1.0f, 0.52f, 0.28f);
            Color zenith = Color.Lerp(zenithNight, zenithDay, Daylight);
            Color horizon = Color.Lerp(horizonNight, horizonDay, Daylight);
            horizon = Color.Lerp(horizon, dusk, twilight * 0.75f);
            zenith = Color.Lerp(zenith, new Color(0.30f, 0.26f, 0.5f), twilight * 0.35f * Daylight);

            // The sun's sprite plane axes.
            Vector3 right = Vector3.Cross(Vector3.forward, toSun).normalized;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            Vector3 up = Vector3.Cross(toSun, right).normalized;
            _skyMaterial.SetColor("_ZenithColor", zenith);
            _skyMaterial.SetColor("_HorizonColor", horizon);
            _skyMaterial.SetColor("_GroundColor", Color.Lerp(horizon, Color.black, 0.5f));
            _skyMaterial.SetVector("_SunDir", toSun);
            _skyMaterial.SetVector("_SunRight", right);
            _skyMaterial.SetVector("_SunUp", up);
            _skyMaterial.SetFloat("_NightAmount", 1f - Daylight);

            RenderSettings.fogColor = horizon;
            RenderSettings.ambientLight = Color.Lerp(new Color(0.05f, 0.06f, 0.12f), new Color(0.55f, 0.6f, 0.7f), Daylight);

            if (Sun != null)
            {
                // The light shines from the sun by day and from the moon at night, so shadows always have a source.
                Vector3 dirToLight = SunHeight > -0.08f ? toSun : -toSun;
                Sun.transform.rotation = Quaternion.LookRotation(-dirToLight);
                Color warm = Color.Lerp(new Color(1.0f, 0.97f, 0.9f), new Color(1.0f, 0.62f, 0.34f), twilight);
                Sun.color = SunHeight > -0.08f ? warm : new Color(0.45f, 0.55f, 0.95f);
                Sun.intensity = SunHeight > -0.08f ? Mathf.Lerp(0.15f, 1.15f, Daylight) : 0.18f;
            }

            Shader.SetGlobalFloat(IdDaylight, Mathf.Lerp(0.0f, 1.0f, Daylight));
            Shader.SetGlobalVector(IdSkyAmbient, new Vector4(0.50f, 0.56f, 0.70f, 1f) * Mathf.Lerp(0.6f, 1f, Daylight));
            Shader.SetGlobalVector(IdNight, new Vector4(0.10f, 0.13f, 0.26f, 1f));

            float cloudLight = Mathf.Lerp(0.12f, 1.0f, Daylight);
            Color cloud = Color.Lerp(new Color(0.55f, 0.58f, 0.7f), Color.white, Daylight) * cloudLight;
            cloud = Color.Lerp(cloud, new Color(1f, 0.7f, 0.5f) * cloudLight, twilight * 0.6f);
            cloud.a = 0.88f;
            _cloudMaterial.SetColor("_Color", cloud);
        }

        /// <summary>Mean colour of the sky, used for the underwater and damage tints.</summary>
        public Color Horizon { get { return RenderSettings.fogColor; } }
    }
}
