using System.Collections.Generic;
using UnityEngine;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>
    /// A dragon built in Blender (skinned mesh, 43 to 46 bones). Bones are driven by rotations expressed in model space
    /// (X across the wings, Y up, Z along the body toward the head), applied on top of the rest pose, so a wing flap
    /// is "rotate about the body's forward axis" no matter how the bone itself is oriented.
    /// </summary>
    public sealed class DragonView : EntityView
    {
        private struct Bone
        {
            public Transform T;
            public Quaternion RestLocal;
            public Quaternion ParentRestWorld;
        }

        private static readonly float[] ModelScale = { 0.50f, 0.58f, 0.72f };
        private const float GroundOffset = 3.8f;

        private readonly Dictionary<string, Bone> _bones = new Dictionary<string, Bone>();
        private readonly Transform _visual;
        private readonly Material[] _materials;
        private readonly int _kind;
        private float _flapPhase, _idlePhase;
        private float _yawRate, _lastYaw, _bank;
        private float _bite;

        public DragonView(RemoteEntity e)
        {
            Entity = e;
            _kind = Mathf.Clamp(e.Variant, 0, 2);
            string name = DragonStats.Names[_kind].ToLowerInvariant();
            Root = new GameObject("Dragon " + e.Id + " " + name);

            GameObject prefab = Resources.Load<GameObject>(GameAssets.Root + "Models/dragon_" + name);
            GameObject model;
            if (prefab != null) model = Object.Instantiate(prefab);
            else
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.Destroy(model.GetComponent<Collider>());
                model.transform.localScale = new Vector3(2f, 4f, 8f);
            }
            _visual = model.transform;
            _visual.SetParent(Root.transform, false);
            float s = ModelScale[_kind];
            _visual.localScale = Vector3.one * s;
            _visual.localPosition = new Vector3(0f, GroundOffset * s, 0f);

            // Rest pose of every bone, relative to the model root.
            Quaternion inverseRoot = Quaternion.Inverse(_visual.rotation);
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                Bone b = new Bone();
                b.T = t;
                b.RestLocal = t.localRotation;
                b.ParentRestWorld = t.parent != null ? inverseRoot * t.parent.rotation : Quaternion.identity;
                if (!_bones.ContainsKey(t.name)) _bones[t.name] = b;
            }

            // Skins: the FBX material names say which painted texture each part uses.
            List<Material> mats = new List<Material>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] src = r.sharedMaterials;
                Material[] dst = new Material[src.Length];
                for (int k = 0; k < src.Length; k++)
                {
                    string mn = src[k] != null ? src[k].name.ToLowerInvariant() : "skin";
                    string part = "skin";
                    if (mn.Contains("membrane")) part = "membrane";
                    else if (mn.Contains("belly")) part = "belly";
                    else if (mn.Contains("back")) part = "back";
                    else if (mn.Contains("eye")) part = "eye";
                    string tex = part == "eye" ? name + "_" + name + "_eye" : name + "_" + name + "_" + part + "_color";
                    Material m = new Material(GameAssets.EntityMaterialTemplate);
                    Texture2D t = Resources.Load<Texture2D>(GameAssets.Root + "Models/Textures/" + tex);
                    if (t != null) { t.filterMode = FilterMode.Bilinear; m.SetTexture("_MainTex", t); }
                    if (part == "membrane") m.SetFloat("_Cull", 0f);
                    dst[k] = m;
                    mats.Add(m);
                }
                r.sharedMaterials = dst;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer) ((SkinnedMeshRenderer)r).updateWhenOffscreen = true;
            }
            _materials = mats.ToArray();
            Label = DragonStats.Names[_kind];
            LastPos = new Vector3(e.RenderPos.X, e.RenderPos.Y, e.RenderPos.Z);
            _lastYaw = e.RenderYaw;
        }

        private void Rotate(string bone, Quaternion modelSpace)
        {
            Bone b;
            if (!_bones.TryGetValue(bone, out b)) return;
            b.T.localRotation = Quaternion.Inverse(b.ParentRestWorld) * modelSpace * b.ParentRestWorld * b.RestLocal;
        }

        private static Quaternion AboutZ(float radians) { return Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.forward); }
        private static Quaternion AboutX(float radians) { return Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.right); }
        private static Quaternion AboutY(float radians) { return Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.up); }

        public override void Update(float dt, SkyController sky, GameClient client)
        {
            FollowTransform(dt);
            bool flying = (Entity.Flags & EntityFlags.Flying) != 0;
            bool breathing = (Entity.Flags & EntityFlags.BreathingFire) != 0;
            bool biting = (Entity.Flags & EntityFlags.Attacking) != 0;
            bool dead = (Entity.Flags & EntityFlags.Dead) != 0;
            bool ridden = (Entity.Flags & EntityFlags.Ridden) != 0;

            // Banking follows the turn rate.
            float yawNow = Entity.RenderYaw;
            float dyaw = Mathf.DeltaAngle(_lastYaw * Mathf.Rad2Deg, yawNow * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            _lastYaw = yawNow;
            _yawRate = Mathf.Lerp(_yawRate, dyaw / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-6f * dt));
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-_yawRate * 0.45f, -0.7f, 0.7f), 1f - Mathf.Exp(-5f * dt));

            if (dead) DeathTime += dt; else DeathTime = 0f;
            float pitch = Entity.Pitch * Mathf.Rad2Deg;
            Quaternion body = Quaternion.Euler(pitch * 0.8f, yawNow * Mathf.Rad2Deg, _bank * Mathf.Rad2Deg);
            if (dead) body = body * Quaternion.Euler(0f, 0f, 75f * Mathf.Clamp01(DeathTime / 1.2f));
            Root.transform.rotation = body;

            float flapRate = flying ? 1.1f + Mathf.Min(Speed, 25f) * 0.025f : 0.25f;
            _flapPhase += dt * flapRate * Mathf.PI * 2f;
            _idlePhase += dt;
            float amp = flying ? (breathing ? 0.35f : 0.8f) : 0.05f;
            float glide = flying ? 0.12f : -0.0f;
            float a = Mathf.Sin(_flapPhase) * amp + glide;
            float fold = flying ? 0f : 1f;

            // Wings: shoulder lifts and drops, the elbow and fingers follow a little late. Folded on the ground.
            float shoulder = Mathf.Lerp(a, -1.15f, fold);
            Rotate("wing_L0", AboutZ(shoulder));
            Rotate("wing_R0", AboutZ(-shoulder));
            float elbow = Mathf.Lerp(-a * 0.55f + 0.1f, 1.9f, fold);
            Rotate("wing_L1", AboutZ(elbow));
            Rotate("wing_R1", AboutZ(-elbow));
            for (int f = 2; f <= 5; f++)
            {
                float lag = Mathf.Sin(_flapPhase - 0.35f * f) * amp * 0.35f;
                float tuck = Mathf.Lerp(-lag, 1.2f, fold);
                Rotate("wing_L" + f, AboutZ(tuck));
                Rotate("wing_R" + f, AboutZ(-tuck));
            }

            // Neck: a gentle S that tracks look pitch, rears back to breathe fire and strikes forward to bite.
            _bite = Mathf.MoveTowards(_bite, biting ? 1f : 0f, dt * 6f);
            float rear = breathing ? -0.09f : 0f;
            float strike = _bite * 0.16f;
            for (int i = 0; i < 10; i++)
            {
                float sway = Mathf.Sin(_idlePhase * 0.6f + i * 0.35f) * 0.03f;
                float bend = rear + strike + Mathf.Clamp(Entity.Pitch * 0.05f, -0.08f, 0.08f);
                Rotate("neck_" + i, AboutX(bend + Mathf.Sin(_idlePhase * 0.9f + i * 0.4f) * 0.015f) * AboutY(sway));
            }
            Rotate("head", AboutX(strike * 0.6f - (breathing ? 0.15f : 0f)));
            float jaw = breathing ? 0.6f : (biting ? 0.75f : 0.05f + 0.03f * Mathf.Sin(_idlePhase * 1.7f));
            Rotate("jaw", AboutX(jaw));

            // Tail: a travelling wave, leaning into turns.
            for (int i = 0; i < 14; i++)
            {
                float w = Mathf.Sin(_idlePhase * (flying ? 1.8f : 1.0f) - i * 0.5f) * (0.04f + i * 0.004f);
                Rotate("tail_" + i, AboutY(w + _bank * 0.03f));
            }

            // Legs: tucked in flight, planted on the ground.
            float tuckLeg = flying ? 1f : 0f;
            Rotate("leg_L0", AboutX(0.5f * tuckLeg));
            Rotate("leg_R0", AboutX(0.5f * tuckLeg));
            Rotate("leg_L1", AboutX(0.9f * tuckLeg));
            Rotate("leg_R1", AboutX(0.9f * tuckLeg));
            Rotate("leg_L2", AboutX(-0.5f * tuckLeg));
            Rotate("leg_R2", AboutX(-0.5f * tuckLeg));

            // Body bob with each stroke.
            if (_visual != null)
            {
                float s = ModelScale[_kind];
                float bob = flying ? Mathf.Sin(_flapPhase) * 0.22f : 0f;
                _visual.localPosition = new Vector3(0f, GroundOffset * s + (flying ? 0.4f : 0f) + bob * s, 0f);
            }

            float block;
            float sk = SkyLight(client, Root.transform.position + Vector3.up * 2f, out block);
            Color tint = Entity.HurtTime > 0f ? new Color(1.7f, 0.5f, 0.5f) : (ridden ? new Color(1.05f, 1.05f, 1.15f) : Color.white);
            for (int i = 0; i < _materials.Length; i++)
            {
                _materials[i].SetColor("_Tint", tint);
                _materials[i].SetVector("_Light", new Vector4(Mathf.Max(sk, 0.15f), block, 0f, 0f));
            }
            LabelPos = Root.transform.position + Vector3.up * 8f;
        }
    }
}
