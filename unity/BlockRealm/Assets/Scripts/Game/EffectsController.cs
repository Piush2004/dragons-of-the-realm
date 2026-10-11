using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>Particles, the block outline, crack overlays and screen shake.</summary>
    public sealed class EffectsController : MonoBehaviour
    {
        private ParticleSystem _debris, _smoke, _flame;
        private GameClient _client;
        private AudioBank _audio;
        private Transform _selection;
        private MeshRenderer _selectionRenderer;
        private Transform _crack;
        private MeshRenderer _crackRenderer;
        private Material _crackMaterial;
        private Texture2D[] _crackTextures;
        private readonly Dictionary<int, Transform> _otherCracks = new Dictionary<int, Transform>();
        private float _fireTimer;

        public float Shake;                              // current camera shake in blocks, decays

        public void Init(AudioBank audio)
        {
            _audio = audio;
            _debris = MakeSystem("Debris", GameAssets.White, false, 0.55f, 0.1f, 1.0f);
            _smoke = MakeSystem("Smoke", GameAssets.Load("Particles/smoke"), false, 2.0f, 1.2f, -0.05f);
            _flame = MakeSystem("Flame", GameAssets.Load("Particles/flame"), true, 0.9f, 0.9f, -0.2f);

            _crackTextures = new Texture2D[10];
            for (int i = 0; i < 10; i++) _crackTextures[i] = GameAssets.Load("Cracks/crack_" + i);
            _crackMaterial = new Material(GameAssets.UnlitAlpha);
            _crackMaterial.SetTexture("_MainTex", _crackTextures[0]);
            _crackMaterial.SetFloat("_DepthBias", -2f);
            _crackMaterial.SetFloat("_Cull", 2f);

            GameObject cg = new GameObject("Crack");
            cg.transform.SetParent(transform, false);
            cg.AddComponent<MeshFilter>().sharedMesh = BlockMeshes.CrackCube(0.003f);
            _crackRenderer = cg.AddComponent<MeshRenderer>();
            _crackRenderer.sharedMaterial = _crackMaterial;
            _crackRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _crack = cg.transform;
            _crackRenderer.enabled = false;

            GameObject sg = new GameObject("Selection");
            sg.transform.SetParent(transform, false);
            sg.AddComponent<MeshFilter>().sharedMesh = BlockMeshes.Wireframe(0.004f);
            _selectionRenderer = sg.AddComponent<MeshRenderer>();
            Material lines = new Material(GameAssets.UnlitAlpha);
            lines.color = new Color(0f, 0f, 0f, 0.65f);
            _selectionRenderer.sharedMaterial = lines;
            _selectionRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _selection = sg.transform;
            _selectionRenderer.enabled = false;
        }

        private ParticleSystem MakeSystem(string name, Texture2D tex, bool additive, float life, float size, float gravity)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = life;
            main.startSize = size;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2000;
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, additive ? 0.2f : 1.6f));
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            Material m = new Material(additive ? GameAssets.UnlitAdditive : GameAssets.UnlitAlpha);
            m.SetTexture("_MainTex", tex);
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        public void Bind(GameClient client)
        {
            if (_client != null) { _client.EffectReceived = null; _client.BreakStage = null; }
            _client = client;
            client.EffectReceived = OnEffect;
            client.BreakStage = OnRemoteBreak;
        }

        private void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, Color color, float size, float life)
        {
            ParticleSystem.EmitParams p = new ParticleSystem.EmitParams();
            p.position = pos;
            p.velocity = vel;
            p.startColor = color;
            p.startSize = size;
            p.startLifetime = life;
            ps.Emit(p, 1);
        }

        private void OnEffect(EffectKind kind, Vec3 position, int data)
        {
            Vector3 pos = new Vector3(position.X, position.Y, position.Z);
            switch (kind)
            {
                case EffectKind.BlockBreak:
                {
                    BlockInfo info = Blocks.Get((byte)Mathf.Clamp(data, 0, 255));
                    Color c = info.Render == RenderKind.None ? Color.gray : GameAssets.AverageColor(info.LayerSide);
                    for (int i = 0; i < 16; i++)
                    {
                        Vector3 v = new Vector3(UnityEngine.Random.Range(-2.2f, 2.2f), UnityEngine.Random.Range(0.5f, 3.5f), UnityEngine.Random.Range(-2.2f, 2.2f));
                        Vector3 p = pos + new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), UnityEngine.Random.Range(-0.4f, 0.4f), UnityEngine.Random.Range(-0.4f, 0.4f));
                        float shade = UnityEngine.Random.Range(0.75f, 1.1f);
                        Emit(_debris, p, v, new Color(c.r * shade, c.g * shade, c.b * shade, 1f), 0.11f, UnityEngine.Random.Range(0.4f, 0.8f));
                    }
                    if (_audio != null) _audio.Play(_audio.BreakSoundFor((byte)Mathf.Clamp(data, 0, 255)), pos, 0.9f);
                    break;
                }
                case EffectKind.Explosion:
                {
                    float radius = data / 10f;
                    int count = Mathf.Clamp((int)(radius * 14f), 12, 90);
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 dir = UnityEngine.Random.onUnitSphere;
                        Emit(_smoke, pos + dir * UnityEngine.Random.Range(0f, radius * 0.6f), dir * UnityEngine.Random.Range(1f, 4.5f) + Vector3.up * 1.2f,
                             new Color(0.35f, 0.35f, 0.38f, 0.9f), UnityEngine.Random.Range(1.2f, 2.8f), UnityEngine.Random.Range(1.2f, 2.4f));
                    }
                    for (int i = 0; i < count / 2; i++)
                    {
                        Vector3 dir = UnityEngine.Random.onUnitSphere;
                        Emit(_flame, pos + dir * UnityEngine.Random.Range(0f, radius * 0.5f), dir * UnityEngine.Random.Range(2f, 7f),
                             new Color(1f, 0.7f, 0.3f, 1f), UnityEngine.Random.Range(0.9f, 1.8f), UnityEngine.Random.Range(0.4f, 0.8f));
                    }
                    if (_audio != null) _audio.Play(Sfx.Explosion, pos, 1f, 1f);
                    Vector3 cam = Camera.main != null ? Camera.main.transform.position : pos;
                    float dist = Vector3.Distance(cam, pos);
                    Shake = Mathf.Max(Shake, Mathf.Clamp(1.1f / (1f + dist * 0.12f), 0f, 0.6f));
                    break;
                }
                case EffectKind.Fire:
                    Emit(_flame, pos, Vector3.up * 1.5f, new Color(1f, 0.6f, 0.2f, 1f), 0.8f, 0.7f);
                    break;
                case EffectKind.Splash:
                    for (int i = 0; i < 12; i++)
                        Emit(_debris, pos, new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), UnityEngine.Random.Range(1f, 3f), UnityEngine.Random.Range(-1.5f, 1.5f)), new Color(0.4f, 0.6f, 1f, 1f), 0.1f, 0.6f);
                    if (_audio != null) _audio.Play(Sfx.Splash, pos);
                    break;
                case EffectKind.Smoke:
                    Emit(_smoke, pos, Vector3.up * 1.2f, new Color(0.4f, 0.4f, 0.4f, 0.8f), 1.2f, 1.6f);
                    break;
                case EffectKind.Hit:
                    for (int i = 0; i < 6; i++)
                        Emit(_debris, pos, UnityEngine.Random.insideUnitSphere * 2.5f, new Color(0.9f, 0.1f, 0.1f, 1f), 0.1f, 0.4f);
                    break;
            }
        }

        private void OnRemoteBreak(int entityId, Vec3i pos, int stage)
        {
            Transform t;
            if (stage == 255)
            {
                if (_otherCracks.TryGetValue(entityId, out t)) { Destroy(t.gameObject); _otherCracks.Remove(entityId); }
                return;
            }
            if (!_otherCracks.TryGetValue(entityId, out t))
            {
                GameObject go = new GameObject("Remote crack");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = BlockMeshes.CrackCube(0.003f);
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = new Material(_crackMaterial);
                r.shadowCastingMode = ShadowCastingMode.Off;
                t = go.transform;
                _otherCracks[entityId] = t;
            }
            t.position = new Vector3(pos.X, pos.Y, pos.Z);
            t.GetComponent<MeshRenderer>().sharedMaterial.SetTexture("_MainTex", _crackTextures[Mathf.Clamp(stage, 0, 9)]);
        }

        private void Update()
        {
            if (_client == null || _client.State != ClientState.Joined) return;

            if (_client.TargetBlock.Hit && !_client.Dead)
            {
                _selection.position = new Vector3(_client.TargetBlock.X, _client.TargetBlock.Y, _client.TargetBlock.Z);
                _selectionRenderer.enabled = true;
            }
            else _selectionRenderer.enabled = false;

            if (_client.IsBreaking && _client.BreakProgress > 0.02f)
            {
                _crack.position = new Vector3(_client.BreakingPos.X, _client.BreakingPos.Y, _client.BreakingPos.Z);
                int stage = Mathf.Clamp((int)(_client.BreakProgress * 10f), 0, 9);
                _crackMaterial.SetTexture("_MainTex", _crackTextures[stage]);
                _crackRenderer.enabled = true;
                _fireTimer += Time.deltaTime;
                if (_fireTimer > 0.18f && _audio != null)
                {
                    _fireTimer = 0f;
                    Vec3 p = new Vec3(_client.BreakingPos.X + 0.5f, _client.BreakingPos.Y + 0.5f, _client.BreakingPos.Z + 0.5f);
                    _audio.Play(Sfx.Place, new Vector3(p.X, p.Y, p.Z), 0.5f, 0.8f);
                }
            }
            else _crackRenderer.enabled = false;

            // Ambient flames above burning blocks and near lava within a short range of the player.
            if (_client.World != null && Time.frameCount % 6 == 0)
            {
                Vector3 c = _client.Player.Pos.X == 0 && _client.Player.Pos.Z == 0 ? Vector3.zero : new Vector3(_client.Player.Pos.X, _client.Player.Pos.Y, _client.Player.Pos.Z);
                for (int i = 0; i < 4; i++)
                {
                    int x = Mathf.FloorToInt(c.x) + UnityEngine.Random.Range(-14, 15);
                    int y = Mathf.FloorToInt(c.y) + UnityEngine.Random.Range(-6, 8);
                    int z = Mathf.FloorToInt(c.z) + UnityEngine.Random.Range(-14, 15);
                    byte id = _client.World.GetBlock(x, y, z);
                    if (id == B.Fire) Emit(_flame, new Vector3(x + 0.5f, y + 0.4f, z + 0.5f), Vector3.up * 1.2f, new Color(1f, 0.65f, 0.25f, 1f), 0.7f, 0.6f);
                    else if (id == B.Torch) Emit(_flame, new Vector3(x + 0.5f, y + 0.8f, z + 0.5f), Vector3.up * 0.4f, new Color(1f, 0.8f, 0.4f, 1f), 0.18f, 0.35f);
                    else if (id == B.FurnaceLit) Emit(_smoke, new Vector3(x + 0.5f, y + 1.1f, z + 0.5f), Vector3.up * 0.8f, new Color(0.4f, 0.4f, 0.4f, 0.6f), 0.5f, 1.2f);
                }
            }
            Shake = Mathf.MoveTowards(Shake, 0f, Time.deltaTime * 0.9f);
        }
    }
}
