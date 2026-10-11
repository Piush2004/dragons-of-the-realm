using UnityEngine;
using UnityEngine.Rendering;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>
    /// The player's eyes: first and third person cameras, field-of-view effects, view bob, the held item and the
    /// player's own body model in third person. Also drives footstep and swing sounds.
    /// </summary>
    public sealed class PlayerView : MonoBehaviour
    {
        public Camera Cam;
        private GameClient _client;
        private InputProvider _input;
        private AudioBank _audio;
        private EffectsController _effects;

        public int CameraMode;                   // 0 first person, 1 behind, 2 in front

        private CreatureView _body;
        private RemoteEntity _bodyEntity;
        private GameObject _held;
        private MeshFilter _heldMesh;
        private MeshRenderer _heldRenderer;
        private Material _heldBlockMaterial, _heldItemMaterial;
        private ushort _heldId = 0xFFFF;
        private float _swing, _eyeOffset = 1.62f, _bobPhase, _stepDistance, _fovBoost;
        private bool _prevOnGround = true, _prevAttack;
        private float _lastFall;
        private Vector3 _lastPos;
        private SkyController _sky;

        public void Init(Camera cam, GameClient client, InputProvider input, AudioBank audio, EffectsController effects, SkyController sky)
        {
            Cam = cam;
            _client = client;
            _input = input;
            _audio = audio;
            _effects = effects;
            _sky = sky;
            Cam.nearClipPlane = 0.06f;

            _bodyEntity = new RemoteEntity { Id = -1, Type = EntityType.Player, Name = "" };
            _body = new CreatureView(_bodyEntity);
            _body.Root.SetActive(false);

            _held = new GameObject("Held item");
            _held.transform.SetParent(Cam.transform, false);
            _heldMesh = _held.AddComponent<MeshFilter>();
            _heldRenderer = _held.AddComponent<MeshRenderer>();
            _heldRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _heldRenderer.receiveShadows = false;
            _heldBlockMaterial = new Material(GameAssets.OpaqueMaterial);
            _heldItemMaterial = new Material(GameAssets.UnlitAlpha);
            _heldItemMaterial.SetFloat("_SrcBlend", 1f);
            _heldItemMaterial.SetFloat("_DstBlend", 0f);
            _heldItemMaterial.SetFloat("_Cull", 0f);
            _heldItemMaterial.SetFloat("_Cutoff", 0.4f);
        }

        public void SetClient(GameClient client) { _client = client; }

        public void CycleCamera()
        {
            CameraMode = (CameraMode + 1) % 3;
        }

        public void TriggerSwing() { _swing = 1f; }

        private void UpdateHeldItem()
        {
            ushort id = _client.Held.IsEmpty ? (ushort)0 : _client.Held.Id;
            if (id == _heldId) return;
            _heldId = id;
            if (id == 0)
            {
                // Bare hand: a small skin-coloured block.
                _heldMesh.sharedMesh = BlockMeshes.Cube(B.Clay, 0.22f);
                _heldRenderer.sharedMaterial = _heldBlockMaterial;
                _held.transform.localScale = new Vector3(0.6f, 1.6f, 0.6f);
                return;
            }
            _held.transform.localScale = Vector3.one;
            BlockInfo info = id < 256 ? Blocks.All[id] : null;
            if (info != null && (info.Render == RenderKind.Cube || info.Render == RenderKind.Cutout))
            {
                _heldMesh.sharedMesh = BlockMeshes.Cube((byte)id, 0.34f);
                _heldRenderer.sharedMaterial = _heldBlockMaterial;
            }
            else
            {
                _heldMesh.sharedMesh = BlockMeshes.Quad(0.5f);
                _heldItemMaterial.SetTexture("_MainTex", GameAssets.Icon(id));
                _heldRenderer.sharedMaterial = _heldItemMaterial;
            }
        }

        private void LateUpdate()
        {
            if (_client == null || _client.State != ClientState.Joined || _client.World == null) return;
            float dt = Time.deltaTime;
            PlayerController p = _client.Player;
            Vector3 feet = new Vector3(p.Pos.X, p.Pos.Y, p.Pos.Z);

            // Eye height eases when sneaking.
            float targetEye = p.EyeOffset;
            _eyeOffset = Mathf.Lerp(_eyeOffset, targetEye, 1f - Mathf.Exp(-14f * dt));
            Vector3 eye = feet + Vector3.up * _eyeOffset;

            // View bob while walking on the ground.
            float planar = new Vector2(feet.x - _lastPos.x, feet.z - _lastPos.z).magnitude;
            _lastPos = feet;
            float speed = planar / Mathf.Max(dt, 1e-4f);
            bool walking = p.OnGround && speed > 0.5f && !p.Riding;
            if (walking) _bobPhase += planar * 5.2f;
            float bobAmount = walking ? Mathf.Clamp01(speed / 5f) : 0f;
            Vector3 bob = new Vector3(Mathf.Sin(_bobPhase) * 0.03f, Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.045f - 0.02f, 0f) * bobAmount;

            // Footsteps and landing thumps.
            if (walking)
            {
                _stepDistance += planar;
                if (_stepDistance > (p.Sprinting ? 2.4f : 1.9f))
                {
                    _stepDistance = 0f;
                    if (_audio != null) _audio.Play(Sfx.Step, feet, 0.45f);
                }
            }
            if (p.OnGround && !_prevOnGround && p.LandedFall > 2.5f && _audio != null)
            {
                _audio.Play(Sfx.Place, feet, Mathf.Clamp01(p.LandedFall / 8f), 0.6f);
                p.LandedFall = 0f;
            }
            _prevOnGround = p.OnGround;

            float yaw = _input.Yaw * Mathf.Rad2Deg;
            float pitch = _input.Pitch * Mathf.Rad2Deg;
            Quaternion look = Quaternion.Euler(pitch, yaw, 0f);

            // Field of view widens a little when sprinting, flying or riding fast.
            float fovTarget = GameSettings.Fov + (p.Sprinting ? 7f : 0f) + (p.Flying ? 4f : 0f) + (p.Riding ? 6f + p.RideSpeedNow * 0.25f : 0f);
            _fovBoost = Mathf.Lerp(_fovBoost, fovTarget, 1f - Mathf.Exp(-6f * dt));
            Cam.fieldOfView = _fovBoost;

            int mode = p.Riding ? 1 : CameraMode;
            Vector3 camPos;
            Quaternion camRot = look;
            if (mode == 0)
            {
                camPos = eye + look * bob;
            }
            else
            {
                float dist = p.Riding ? 11f : 4.2f;
                Vector3 dir = look * (mode == 2 ? Vector3.forward : Vector3.back);
                Vector3 origin = eye + (p.Riding ? Vector3.up * 2.0f : Vector3.zero);
                RayHit hit = VoxelPhysics.Raycast(_client.World, new Vec3(origin.x, origin.y, origin.z), new Vec3(dir.x, dir.y, dir.z), dist);
                float use = hit.Hit ? Mathf.Max(0.3f, hit.Distance - 0.3f) : dist;
                camPos = origin + dir * use;
                if (mode == 2) camRot = Quaternion.LookRotation(-dir);
            }

            if (_effects != null && _effects.Shake > 0.001f)
            {
                camPos += Random.insideUnitSphere * _effects.Shake * 0.25f;
            }
            Cam.transform.SetPositionAndRotation(camPos, camRot);

            // Our own body: visible only outside first person, and while riding the dragon carries us.
            bool showBody = mode != 0;
            _body.Root.SetActive(showBody);
            if (showBody)
            {
                _bodyEntity.RenderPos = p.Pos;
                _bodyEntity.TargetPos = p.Pos;
                _bodyEntity.RenderYaw = p.Riding ? p.RideYaw : _input.Yaw;
                _bodyEntity.Pitch = _input.Pitch;
                _bodyEntity.Flags = p.Sneaking ? EntityFlags.Sneaking : EntityFlags.None;
                if (p.OnGround) _bodyEntity.Flags |= EntityFlags.OnGround;
                if (_client.Dead) _bodyEntity.Flags |= EntityFlags.Dead;
                _body.Update(dt, _sky, _client);
            }

            // Held item: bobs with the walk, swings when mining or attacking.
            UpdateHeldItem();
            bool attackHeld = _client.IsBreaking || _client.TargetEntity != null && _input.Actions.AttackHeld;
            if (attackHeld) _swing = Mathf.Max(_swing, 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 9f)));
            else _swing = Mathf.MoveTowards(_swing, 0f, dt * 4f);
            if (_input.Actions.UsePressed) _swing = 1f;
            if (_input.Actions.AttackHeld && !_prevAttack && _audio != null) _audio.Play(Sfx.Swing, camPos, 0.5f);
            _prevAttack = _input.Actions.AttackHeld;

            _held.SetActive(mode == 0 && !_client.Dead);
            Vector3 handPos = new Vector3(0.42f, -0.36f, 0.62f) + bob * 1.6f;
            handPos += new Vector3(-0.12f, -0.12f, 0.05f) * _swing;
            Quaternion handRot = Quaternion.Euler(10f + _swing * -48f, -28f + _swing * 12f, 6f);
            if (_heldId != 0 && _heldId >= 256) handRot = Quaternion.Euler(0f, -20f, 0f) * Quaternion.Euler(_swing * -40f, 0f, 0f);
            _held.transform.localPosition = handPos;
            _held.transform.localRotation = handRot;
        }
    }
}
