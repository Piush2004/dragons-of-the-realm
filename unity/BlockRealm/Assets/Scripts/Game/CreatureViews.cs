using System;
using System.Collections.Generic;
using UnityEngine;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>One drawn entity: a model plus its animation state.</summary>
    public abstract class EntityView
    {
        public GameObject Root;
        public RemoteEntity Entity;
        protected Vector3 LastPos;
        protected float Speed;
        protected float Phase;
        protected float DeathTime;
        public string Label;
        public Vector3 LabelPos;

        public abstract void Update(float dt, SkyController sky, GameClient client);

        public virtual void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }

        protected void FollowTransform(float dt)
        {
            Vector3 p = new Vector3(Entity.RenderPos.X, Entity.RenderPos.Y, Entity.RenderPos.Z);
            Vector3 delta = p - LastPos;
            float inst = new Vector2(delta.x, delta.z).magnitude / Mathf.Max(dt, 1e-4f);
            Speed = Mathf.Lerp(Speed, Mathf.Min(inst, 12f), 1f - Mathf.Exp(-10f * dt));
            LastPos = p;
            Root.transform.position = p;
        }

        protected static float SkyLight(GameClient client, Vector3 pos, out float block)
        {
            block = 0f;
            if (client.World == null) return 1f;
            int x = Mathf.FloorToInt(pos.x), y = Mathf.FloorToInt(pos.y + 0.5f), z = Mathf.FloorToInt(pos.z);
            block = client.World.GetBlockLightAt(x, y, z) / 15f;
            return client.World.GetSkyLight(x, y, z) / 15f;
        }
    }

    /// <summary>Builds creature models from the Blender FBX files and gives them the entity material.</summary>
    public static class ModelFactory
    {
        // If a model ever shows up facing the wrong way after import, flip this one value.
        public const float ModelYawOffset = 0f;

        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Texture2D> Skins = new Dictionary<string, Texture2D>();

        public static GameObject Instantiate(string name, out Material[] materials)
        {
            GameObject prefab;
            if (!Prefabs.TryGetValue(name, out prefab))
            {
                prefab = Resources.Load<GameObject>(GameAssets.Root + "Models/" + name);
                Prefabs[name] = prefab;
            }
            materials = new Material[0];
            GameObject go;
            if (prefab != null) go = UnityEngine.Object.Instantiate(prefab);
            else
            {
                // Missing model: a simple box so the game still plays.
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                go.transform.localScale = new Vector3(0.6f, 1.8f, 0.6f);
            }
            go.name = name;
            Texture2D skin;
            if (!Skins.TryGetValue(name, out skin))
            {
                skin = Resources.Load<Texture2D>(GameAssets.Root + "Models/Textures/" + name);
                if (skin != null) skin.filterMode = FilterMode.Point;
                Skins[name] = skin;
            }
            List<Material> list = new List<Material>();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material m = new Material(GameAssets.EntityMaterialTemplate);
                if (skin != null) m.SetTexture("_MainTex", skin);
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = false;
                list.Add(m);
            }
            materials = list.ToArray();
            return go;
        }

        public static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = Find(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }
    }

    /// <summary>A walking creature or player: limbs swing with speed, the head follows the look direction.</summary>
    public sealed class CreatureView : EntityView
    {
        private readonly Material[] _materials;
        private Transform _head, _armL, _armR, _legL, _legR, _legFL, _legFR, _legBL, _legBR, _wingL, _wingR, _body;
        private readonly bool _humanoid;
        private readonly EntityType _type;
        private Vector3 _legBaseL, _legBaseR;

        public CreatureView(RemoteEntity e)
        {
            Entity = e;
            _type = e.Type;
            string model = ModelFor(e.Type);
            Root = new GameObject("Entity " + e.Id + " " + model);
            Material[] mats;
            GameObject visual = ModelFactory.Instantiate(model, out mats);
            _materials = mats;
            visual.transform.SetParent(Root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(0f, ModelFactory.ModelYawOffset, 0f);
            Transform t = visual.transform;
            _head = ModelFactory.Find(t, "Head");
            _body = ModelFactory.Find(t, "Body");
            _armL = ModelFactory.Find(t, "ArmL");
            _armR = ModelFactory.Find(t, "ArmR");
            _legL = ModelFactory.Find(t, "LegL");
            _legR = ModelFactory.Find(t, "LegR");
            _legFL = ModelFactory.Find(t, "LegFL");
            _legFR = ModelFactory.Find(t, "LegFR");
            _legBL = ModelFactory.Find(t, "LegBL");
            _legBR = ModelFactory.Find(t, "LegBR");
            _wingL = ModelFactory.Find(t, "WingL");
            _wingR = ModelFactory.Find(t, "WingR");
            _humanoid = _armL != null && _armR != null;
            Label = e.Type == EntityType.Player ? e.Name : null;
            LastPos = new Vector3(e.RenderPos.X, e.RenderPos.Y, e.RenderPos.Z);
        }

        public static string ModelFor(EntityType t)
        {
            switch (t)
            {
                case EntityType.Player: return "player";
                case EntityType.Pig: return "pig";
                case EntityType.Cow: return "cow";
                case EntityType.Sheep: return "sheep";
                case EntityType.Chicken: return "chicken";
                case EntityType.Zombie: return "zombie";
                case EntityType.Skeleton: return "skeleton";
                case EntityType.Creeper: return "creeper";
            }
            return "player";
        }

        public override void Update(float dt, SkyController sky, GameClient client)
        {
            FollowTransform(dt);
            float yaw = Entity.RenderYaw * Mathf.Rad2Deg;
            bool dead = (Entity.Flags & EntityFlags.Dead) != 0;
            if (dead) DeathTime += dt; else DeathTime = 0f;
            Phase += dt * (3f + Speed * 1.9f);
            float swing = Mathf.Sin(Phase) * Mathf.Clamp01(Speed / 2.5f) * 55f;

            Quaternion body = Quaternion.Euler(0f, yaw, 0f);
            if (dead)
            {
                // Topple over and sink slightly.
                float fall = Mathf.Clamp01(DeathTime / 0.5f);
                body = body * Quaternion.Euler(0f, 0f, 90f * fall);
            }
            Root.transform.rotation = body;

            bool sneaking = (Entity.Flags & EntityFlags.Sneaking) != 0;
            bool attacking = (Entity.Flags & EntityFlags.Attacking) != 0;

            if (_humanoid)
            {
                SetLimb(_legL, swing, 0f); SetLimb(_legR, -swing, 0f);
                float armSwing = -swing * 0.8f;
                bool undead = _type == EntityType.Zombie;
                float raise = undead ? -85f : 0f;                    // zombies hold their arms out in front
                SetLimb(_armL, raise + armSwing * (undead ? 0.15f : 1f), 0f);
                SetLimb(_armR, raise - armSwing * (undead ? 0.15f : 1f) + (attacking ? -70f : 0f), 0f);
                if (_type == EntityType.Skeleton && attacking) SetLimb(_armR, -90f, 0f);
                if (_head != null)
                {
                    float look = Mathf.Clamp(Entity.Pitch * Mathf.Rad2Deg, -60f, 60f);
                    _head.localRotation = Quaternion.Euler(look, 0f, 0f);
                }
                if (_body != null && sneaking) _body.localRotation = Quaternion.Euler(25f, 0f, 0f);
                else if (_body != null) _body.localRotation = Quaternion.identity;
            }
            else
            {
                SetLimb(_legFL, swing, 0f); SetLimb(_legBR, swing, 0f);
                SetLimb(_legFR, -swing, 0f); SetLimb(_legBL, -swing, 0f);
                if (_head != null)
                {
                    float look = Mathf.Clamp(Entity.Pitch * Mathf.Rad2Deg, -45f, 45f);
                    float bob = _type == EntityType.Chicken ? Mathf.Sin(Phase * 1.6f) * 8f * Mathf.Clamp01(Speed) : 0f;
                    _head.localRotation = Quaternion.Euler(look + bob, 0f, 0f);
                }
                bool airborne = (Entity.Flags & EntityFlags.OnGround) == 0;
                if (_wingL != null && _wingR != null)
                {
                    float flap = airborne ? Mathf.Sin(Time.time * 28f) * 55f : 0f;
                    _wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
                    _wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
                }
            }

            // Creeper swells as its fuse burns; everything flashes red when hurt.
            float swell = 1f;
            if ((Entity.Flags & EntityFlags.Fuse) != 0) swell = 1f + 0.18f * Mathf.Abs(Mathf.Sin(Time.time * 18f));
            Root.transform.localScale = new Vector3(swell, swell, swell);

            float block;
            float sk = SkyLight(client, Root.transform.position + Vector3.up * 0.8f, out block);
            bool hurt = Entity.HurtTime > 0f;
            Color tint = hurt ? new Color(1.6f, 0.45f, 0.45f) : ((Entity.Flags & EntityFlags.Fuse) != 0 && Mathf.Sin(Time.time * 24f) > 0f ? new Color(2f, 2f, 2f) : Color.white);
            for (int i = 0; i < _materials.Length; i++)
            {
                _materials[i].SetColor("_Tint", tint);
                _materials[i].SetVector("_Light", new Vector4(sk, block, 0f, 0f));
            }
            LabelPos = Root.transform.position + Vector3.up * (Entity.Box.Max.Y - Entity.Box.Min.Y + 0.35f);
        }

        private static void SetLimb(Transform t, float xDegrees, float zDegrees)
        {
            if (t != null) t.localRotation = Quaternion.Euler(xDegrees, 0f, zDegrees);
        }
    }

    /// <summary>A dropped item: a small spinning block or flat icon that bobs gently.</summary>
    public sealed class ItemDropView : EntityView
    {
        private readonly Material _material;
        private readonly float _bobOffset;

        public ItemDropView(RemoteEntity e)
        {
            Entity = e;
            Root = new GameObject("Drop " + e.Id);
            _bobOffset = (e.Id * 0.37f) % 6.28f;
            ushort id = (ushort)Mathf.Clamp(e.ItemId, 0, 65535);
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(Root.transform, false);
            MeshFilter mf = visual.AddComponent<MeshFilter>();
            MeshRenderer mr = visual.AddComponent<MeshRenderer>();
            BlockInfo info = id < 256 ? Blocks.All[id] : null;
            if (info != null && (info.Render == RenderKind.Cube || info.Render == RenderKind.Cutout))
            {
                mf.sharedMesh = BlockMeshes.Cube((byte)id, 0.3f);
                _material = new Material(GameAssets.OpaqueMaterial);
                mr.sharedMaterial = _material;
            }
            else
            {
                mf.sharedMesh = BlockMeshes.Quad(0.4f);
                _material = new Material(GameAssets.UnlitAlpha);
                _material.SetTexture("_MainTex", GameAssets.Icon(id));
                _material.SetFloat("_Cull", 0f);
                _material.SetFloat("_Cutoff", 0.4f);
                _material.SetFloat("_SrcBlend", 1f);
                _material.SetFloat("_DstBlend", 0f);
                mr.sharedMaterial = _material;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            LastPos = new Vector3(e.RenderPos.X, e.RenderPos.Y, e.RenderPos.Z);
        }

        public override void Update(float dt, SkyController sky, GameClient client)
        {
            Vector3 p = new Vector3(Entity.RenderPos.X, Entity.RenderPos.Y, Entity.RenderPos.Z);
            float bob = Mathf.Sin(Time.time * 2.2f + _bobOffset) * 0.06f + 0.22f;
            Root.transform.position = p + Vector3.up * bob;
            Root.transform.rotation = Quaternion.Euler(0f, Time.time * 70f + _bobOffset * 40f, 0f);
        }
    }

    /// <summary>Arrows and dragon fireballs.</summary>
    public sealed class ProjectileView : EntityView
    {
        private readonly Light _light;

        public ProjectileView(RemoteEntity e)
        {
            Entity = e;
            Root = new GameObject("Projectile " + e.Id);
            GameObject visual;
            if (e.Type == EntityType.Arrow)
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.transform.localScale = new Vector3(0.04f, 0.04f, 0.6f);
                Renderer r = visual.GetComponent<Renderer>();
                Material m = new Material(GameAssets.UnlitAlpha);
                m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 0f);
                m.color = new Color(0.55f, 0.42f, 0.25f);
                r.sharedMaterial = m;
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.transform.localScale = Vector3.one * 0.9f;
                Renderer r = visual.GetComponent<Renderer>();
                Material m = new Material(GameAssets.UnlitAlpha);
                m.SetTexture("_MainTex", GameAssets.Load("Particles/flame"));
                m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 1f);
                m.color = new Color(1.8f, 1.1f, 0.5f, 1f);
                r.sharedMaterial = m;
                _light = Root.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = new Color(1f, 0.6f, 0.25f);
                _light.range = 14f;
                _light.intensity = 2.2f;
                _light.shadows = LightShadows.None;
            }
            UnityEngine.Object.Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(Root.transform, false);
            visual.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public override void Update(float dt, SkyController sky, GameClient client)
        {
            Vector3 p = new Vector3(Entity.RenderPos.X, Entity.RenderPos.Y, Entity.RenderPos.Z);
            Root.transform.position = p;
            if (Entity.Type == EntityType.Arrow)
                Root.transform.rotation = Quaternion.Euler(-Entity.Pitch * Mathf.Rad2Deg, Entity.RenderYaw * Mathf.Rad2Deg, 0f);
            if (_light != null) _light.intensity = 2f + Mathf.Sin(Time.time * 30f) * 0.5f;
        }
    }

    /// <summary>Creates, updates and removes the views for every entity the client knows about.</summary>
    public sealed class EntityViews : MonoBehaviour
    {
        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();
        private readonly List<int> _scratch = new List<int>();
        private GameClient _client;
        private SkyController _sky;
        public readonly List<EntityView> Labelled = new List<EntityView>();

        public int Count { get { return _views.Count; } }

        public void Bind(GameClient client, SkyController sky)
        {
            _client = client;
            _sky = sky;
        }

        public void Clear()
        {
            foreach (KeyValuePair<int, EntityView> kv in _views) kv.Value.Destroy();
            _views.Clear();
            Labelled.Clear();
        }

        private EntityView Create(RemoteEntity e)
        {
            switch (e.Type)
            {
                case EntityType.Item: return new ItemDropView(e);
                case EntityType.Arrow:
                case EntityType.Fireball: return new ProjectileView(e);
                case EntityType.Dragon: return new DragonView(e);
                default: return new CreatureView(e);
            }
        }

        private void Update()
        {
            if (_client == null || _client.State != ClientState.Joined) return;
            float dt = Time.deltaTime;

            _scratch.Clear();
            foreach (KeyValuePair<int, EntityView> kv in _views)
                if (!_client.Entities.ContainsKey(kv.Key)) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++)
            {
                _views[_scratch[i]].Destroy();
                _views.Remove(_scratch[i]);
            }

            Labelled.Clear();
            foreach (KeyValuePair<int, RemoteEntity> kv in _client.Entities)
            {
                EntityView v;
                if (!_views.TryGetValue(kv.Key, out v))
                {
                    v = Create(kv.Value);
                    _views[kv.Key] = v;
                }
                // The local player's own body is drawn by PlayerView, and a ridden dragon is drawn normally.
                v.Update(dt, _sky, _client);
                if (!string.IsNullOrEmpty(v.Label)) Labelled.Add(v);
            }
        }

        public bool TryGet(int id, out EntityView view) { return _views.TryGetValue(id, out view); }
    }
}
