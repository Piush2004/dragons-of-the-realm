using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Emberbond
{
    /// <summary>Runtime-built geometry and small helpers the later chapters are made from.</summary>
    public static class Kit
    {
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        public static Material Lit(Color c, float smooth = 0.15f, float metal = 0f)
        {
            string key = c.ToString() + smooth + metal;
            Material m;
            if (Mats.TryGetValue(key, out m) && m != null) return m;
            m = new Material(Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            Mats[key] = m;
            return m;
        }

        public static Material Glow(Color c)
        {
            string key = "glow" + c;
            Material m;
            if (Mats.TryGetValue(key, out m) && m != null) return m;
            Shader s = Shader.Find("HDRP/Unlit");
            m = new Material(s);
            m.SetColor("_UnlitColor", c);
            Mats[key] = m;
            return m;
        }

        public static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Quaternion rot, Material m, string name, bool collider = true)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.rotation = rot;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.Destroy(g.GetComponent<Collider>());
            return g;
        }

        public static GameObject Ball(Transform parent, Vector3 pos, float radius, Material m, string name)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = Vector3.one * radius * 2f;
            g.GetComponent<Renderer>().sharedMaterial = m;
            Object.Destroy(g.GetComponent<Collider>());
            return g;
        }

        public static Light PointLight(Transform parent, Vector3 pos, Color c, float range, float intensity, string name)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            Light l = g.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            return l;
        }

        public static float GroundY(Vector3 p)
        {
            Terrain t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(p) + t.transform.position.y : p.y;
        }

        /// <summary>A volume that applies only while the camera is inside the box (used for the pit's darkness).</summary>
        public static Volume LocalGrade(Transform parent, Vector3 centre, Vector3 size, float exposure, Color filter, float fogDistance)
        {
            GameObject g = new GameObject("LocalGrade");
            g.transform.SetParent(parent, false);
            g.transform.position = centre;
            BoxCollider box = g.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = size;
            Volume v = g.AddComponent<Volume>();
            v.isGlobal = false; v.priority = 20f; v.blendDistance = 2f;
            VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
            Exposure ex = p.Add<Exposure>(true);
            ex.mode.Override(ExposureMode.Fixed);
            ex.fixedExposure.Override(exposure);
            ColorAdjustments ca = p.Add<ColorAdjustments>(true);
            ca.colorFilter.Override(filter);
            ca.contrast.Override(14f);
            Vignette vi = p.Add<Vignette>(true);
            vi.intensity.Override(0.38f);
            if (fogDistance > 0f)
            {
                Fog f = p.Add<Fog>(true);
                f.enabled.Override(true);
                f.meanFreePath.Override(fogDistance);
                f.baseHeight.Override(0f);
                f.albedo.Override(new Color(0.22f, 0.16f, 0.12f));
            }
            v.sharedProfile = p;
            return v;
        }

        /// <summary>A global grade layered over the day look: the cold of the Pale Tide, the ember glow of an ending.</summary>
        public static Volume GlobalGrade(Transform parent, Color filter, float saturation, float fogDistance, Color fogColor)
        {
            GameObject g = new GameObject("GlobalGrade");
            g.transform.SetParent(parent, false);
            Volume v = g.AddComponent<Volume>();
            v.isGlobal = true; v.priority = 15f; v.weight = 0f;
            VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
            ColorAdjustments ca = p.Add<ColorAdjustments>(true);
            ca.colorFilter.Override(filter);
            ca.saturation.Override(saturation);
            if (fogDistance > 0f)
            {
                Fog f = p.Add<Fog>(true);
                f.enabled.Override(true);
                f.meanFreePath.Override(fogDistance);
                f.baseHeight.Override(400f);
                f.albedo.Override(fogColor);
            }
            v.sharedProfile = p;
            return v;
        }
    }

    /// <summary>Lets the story spawn extra dragons from the same Blender model the rival wyrm uses.</summary>
    public static class DragonModels
    {
        private static GameObject _template;

        public static void Capture(GameObject source)
        {
            if (source == null || _template != null) return;
            _template = Object.Instantiate(source);
            _template.name = "DragonTemplate";
            _template.SetActive(false);
            Object.DontDestroyOnLoad(_template);
        }

        public static bool Available { get { return _template != null; } }

        public static GameObject Spawn(Vector3 pos, Quaternion rot, float scale, Color tint, string name)
        {
            if (_template == null) return null;
            GameObject g = Object.Instantiate(_template, pos, rot);
            g.name = name;
            g.transform.localScale = _template.transform.localScale * scale;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>())
            {
                Material[] ms = r.materials;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].HasProperty("_BaseColor"))
                    {
                        Color c = ms[i].GetColor("_BaseColor");
                        ms[i].SetColor("_BaseColor", new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a));
                    }
                }
                r.materials = ms;
            }
            g.SetActive(true);
            return g;
        }
    }

    /// <summary>A dragon that only perches or circles: chained in the pit, or flying over the city in an ending.</summary>
    public sealed class AmbientDragon : MonoBehaviour
    {
        public Vector3 Centre;
        public float Radius, Height, Speed = 0.12f;
        public bool Orbiting;
        public bool Bombard;                         // an ally that hurls fire at hostile soldiers
        public Transform Protect;                    // do not bomb within 30 m of this transform
        private float _angle, _fire = 3f;
        private DragonAnimator _anim;

        private void Start()
        {
            _anim = GetComponent<DragonAnimator>();
            if (_anim == null) _anim = gameObject.AddComponent<DragonAnimator>();
            _angle = Random.value * 6.28f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_anim != null)
            {
                _anim.FlapRate = Orbiting ? 0.9f : 0.28f;
                _anim.FlapAmplitude = Orbiting ? 0.55f : 0.1f;
                _anim.JawOpen = (!Orbiting && Mathf.Sin(Time.time * 0.7f) > 0.92f) ? 0.6f : 0f;
            }
            if (!Orbiting) return;
            _angle += Speed * dt;
            Vector3 p = Centre + new Vector3(Mathf.Sin(_angle) * Radius, Height + Mathf.Sin(_angle * 2.3f) * 12f, Mathf.Cos(_angle) * Radius);
            Vector3 v = p - transform.position;
            if (v.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(v.normalized, Vector3.up), 1f - Mathf.Exp(-2f * dt));
            transform.position = p;
            if (!Bombard) return;
            _fire -= dt;
            if (_fire > 0f) return;
            Director d = Director.Instance;
            if (d == null) return;
            Soldier best = null; float bestD = 1e9f;
            for (int i = 0; i < d.Soldiers.Count; i++)
            {
                Soldier s = d.Soldiers[i];
                if (s == null || s.Health == null || s.Health.Dead) continue;
                if (Protect != null && (s.transform.position - Protect.position).sqrMagnitude < 30f * 30f) continue;
                float dd = (s.transform.position - transform.position).sqrMagnitude;
                if (dd < bestD && dd < 450f * 450f) { bestD = dd; best = s; }
            }
            if (best == null) { _fire = 1.5f; return; }
            _fire = Random.Range(2.5f, 4f);
            Vector3 mouth = transform.position + transform.forward * 5f;
            Vector3 aim = (best.transform.position + Vector3.up - mouth).normalized;
            Fireball.Spawn(mouth, aim * 100f, gameObject, 35f);
        }
    }

    /// <summary>A wooden siege tower that burns and collapses into rigid bodies.</summary>
    public sealed class SiegeTower : MonoBehaviour
    {
        public Damageable Health;
        public bool Fallen;

        public static SiegeTower Build(Vector3 pos, float yaw)
        {
            GameObject root = new GameObject("SiegeTower");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Material wood = Kit.Lit(new Color(0.34f, 0.22f, 0.12f), 0.12f);
            Material dark = Kit.Lit(new Color(0.16f, 0.1f, 0.06f), 0.1f);
            Material hide = Kit.Lit(new Color(0.55f, 0.15f, 0.1f), 0.1f);
            Quaternion r = root.transform.rotation;
            float[] hs = { 5f, 5f, 4f, 3f };
            float[] ws = { 7.5f, 6.4f, 6.4f, 7.5f };
            float y = 0f;
            for (int i = 0; i < hs.Length; i++)
            {
                GameObject piece = Kit.Box(root.transform, pos + r * new Vector3(0f, y + hs[i] * 0.5f, 0f), new Vector3(ws[i], hs[i], ws[i]), r, i % 2 == 0 ? wood : dark, "Piece" + i);
                // Cross bracing so it reads as a timber frame.
                Kit.Box(root.transform, pos + r * new Vector3(0f, y + hs[i] * 0.5f, ws[i] * 0.505f), new Vector3(ws[i] * 0.9f, 0.4f, 0.25f), r, hide, "Brace", false);
                y += hs[i];
            }
            Kit.Box(root.transform, pos + r * new Vector3(0f, y + 1.2f, 0f), new Vector3(0.25f, 2.4f, 0.25f), r, dark, "Pole", false);
            Kit.Box(root.transform, pos + r * new Vector3(0f, y + 2f, 1.1f), new Vector3(0.1f, 1.5f, 2.2f), r, hide, "Banner", false);
            // Wheels.
            for (int k = 0; k < 4; k++)
            {
                float sx = (k % 2 == 0 ? -1f : 1f) * 3.9f, sz = (k < 2 ? -1f : 1f) * 2.6f;
                GameObject w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                w.transform.SetParent(root.transform, false);
                w.transform.position = pos + r * new Vector3(sx, 0.9f, sz);
                w.transform.rotation = r * Quaternion.Euler(0f, 0f, 90f);
                w.transform.localScale = new Vector3(1.8f, 0.3f, 1.8f);
                w.GetComponent<Renderer>().sharedMaterial = dark;
                w.name = "Wheel";
            }
            SiegeTower t = root.AddComponent<SiegeTower>();
            t.Health = root.AddComponent<Damageable>();
            t.Health.MaxHealth = t.Health.Health = 110f;
            t.Health.DisplayName = "Siege tower";
            t.Health.OnDied += d => t.Collapse();
            return t;
        }

        private void Collapse()
        {
            if (Fallen) return;
            Fallen = true;
            Vector3 centre = transform.position + Vector3.up * 8f;
            List<Transform> kids = new List<Transform>();
            foreach (Transform c in transform) kids.Add(c);
            foreach (Transform c in kids)
            {
                if (c.GetComponent<Collider>() == null) continue;
                Rigidbody rb = c.gameObject.AddComponent<Rigidbody>();
                rb.mass = 300f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.AddExplosionForce(9000f, centre + Random.insideUnitSphere * 2f, 18f, 1.5f, ForceMode.Impulse);
            }
            Effects.Burst(centre, 9f);
            Effects.Burst(transform.position + Vector3.up * 2f, 6f);
            AudioDirector.Play(Sfx.Crash, transform.position, 1f);
            Destroy(gameObject, 14f);
        }
    }
}
