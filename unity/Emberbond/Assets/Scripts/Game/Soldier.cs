using System.Collections.Generic;
using UnityEngine;

namespace Emberbond
{
    /// <summary>Where the builder put things, so the game can place the player, dragon and cameras.</summary>
    [System.Serializable]
    public sealed class WorldLayout
    {
        public Vector3 City;
        public float SeaYaw;
        public Vector3 Hall;
        public Vector3 VolcanoRim;

        private static WorldLayout _cached;

        public static WorldLayout Load()
        {
            if (_cached != null) return _cached;
            TextAsset t = Resources.Load<TextAsset>("layout");
            _cached = t != null ? JsonUtility.FromJson<WorldLayout>(t.text) : new WorldLayout { City = new Vector3(-540f, 110f, 660f), SeaYaw = 300f, Hall = new Vector3(0f, 1500f, 0f) };
            return _cached;
        }

        public Vector3 SeaDir { get { return new Vector3(Mathf.Sin(SeaYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(SeaYaw * Mathf.Deg2Rad)); } }
    }

    /// <summary>Creates tinted copies of the rider model for soldiers and councillors.</summary>
    public static class CharacterFactory
    {
        public static GameObject Spawn(Vector3 pos, float yaw, Color cloth, Color cloak, Color hair, string name)
        {
            GameObject prefab = Resources.Load<GameObject>("Rider");
            if (prefab == null) return null;
            GameObject go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
            go.name = name;
            foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Material[] ms = r.materials;                       // per-renderer instances
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i].name;
                    if (n.Contains("cloth")) ms[i].SetColor("_BaseColor", cloth);
                    else if (n.Contains("cloak")) ms[i].SetColor("_BaseColor", cloak);
                    else if (n.Contains("hair")) ms[i].SetColor("_BaseColor", hair);
                }
                r.materials = ms;
            }
            go.AddComponent<RiderAnimator>();
            return go;
        }
    }

    /// <summary>A hostile soldier: closes in, winds up a visible strike, and can be parried or cut down.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class Soldier : MonoBehaviour
    {
        public Damageable Health;
        public ThirdPersonPlayer Target;
        public RiderAnimator Anim;

        private CharacterController _cc;
        private float _cooldown = 0.5f, _wind, _stagger, _facing;
        private bool _winding;
        private float _deadTime;
        private bool _ragdoll;
        public int Parried, Landed;
        public float WalkSpeed = 3.1f, Damage = 12f;
        public float Aggro;                 // 0 = always hunts; otherwise only within this many metres and on the same level
        public bool Staggered { get { return _stagger > 0f; } }
        private Vector3 _knock, _lastHit;

        public static Soldier Spawn(Vector3 pos, ThirdPersonPlayer target, int index)
        {
            return Spawn(pos, target, index, new Color(0.55f, 0.1f, 0.08f), new Color(0.45f, 0.35f, 0.1f), "Soldier");
        }

        public static Soldier Spawn(Vector3 pos, ThirdPersonPlayer target, int index, Color cloth, Color cloak, string name)
        {
            GameObject go = CharacterFactory.Spawn(pos, 0f, cloth, cloak, new Color(0.15f, 0.1f, 0.07f), name + index);
            if (go == null) return null;
            CharacterController cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f); cc.stepOffset = 0.4f;
            Soldier s = go.AddComponent<Soldier>();
            s.Target = target;
            s.Anim = go.GetComponent<RiderAnimator>();
            s.Health = go.AddComponent<Damageable>();
            s.Health.MaxHealth = s.Health.Health = 55f;
            s.Health.DisplayName = name;
            return s;
        }

        private void Start()
        {
            _cc = GetComponent<CharacterController>();
            Health.OnDied += d => { _deadTime = 0.01f; };
            Health.OnDamaged += (d, amount, src) =>
            {
                if (src == null) return;
                Vector3 away = transform.position - src.transform.position;
                away.y = 0f;
                _lastHit = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
                _knock = _lastHit * 5.5f;
                _stagger = Mathf.Max(_stagger, 0.3f);
            };
        }

        /// <summary>On death the soldier becomes a rigid body that is flung by the killing blow and falls where it lands.</summary>
        private void BecomeRagdoll()
        {
            _ragdoll = true;
            _cc.enabled = false;
            CapsuleCollider cap = gameObject.AddComponent<CapsuleCollider>();
            cap.height = 1.8f; cap.radius = 0.35f; cap.center = new Vector3(0f, 0.9f, 0f);
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 80f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.AddForce(_lastHit * 4.5f + Vector3.up * 2.5f, ForceMode.Impulse);
            rb.AddTorque(Vector3.Cross(Vector3.up, _lastHit) * 90f + Random.insideUnitSphere * 12f, ForceMode.Impulse);
        }

        private void Update()
        {
            if (_cc == null || Target == null) return;
            float dt = Time.deltaTime;
            if (Health.Dead)
            {
                _deadTime += dt;
                if (!_ragdoll) BecomeRagdoll();
                if (Anim != null) { Anim.Speed = 0f; Anim.Strike = 0f; }
                if (_deadTime > 14f) Destroy(gameObject);
                return;
            }
            if (Target.Health.Dead) { if (Anim != null) Anim.Speed = 0f; return; }

            if (_knock.sqrMagnitude > 0.01f)
            {
                _cc.Move(_knock * dt);
                _knock = Vector3.Lerp(_knock, Vector3.zero, 1f - Mathf.Exp(-9f * dt));
            }
            Vector3 to = Target.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > 0.01f) _facing = Mathf.LerpAngle(_facing, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-10f * dt));
            transform.rotation = Quaternion.Euler(0f, _facing, 0f);

            if (_stagger > 0f)
            {
                _stagger -= dt;
                if (Anim != null) { Anim.Speed = 0f; Anim.Strike = 0f; }
                return;
            }
            bool aware = Aggro <= 0f || (dist < Aggro && Mathf.Abs(Target.transform.position.y - transform.position.y) < 4.5f);
            if (!aware && !_winding)
            {
                if (Anim != null) { Anim.Speed = 0f; Anim.Strike = 0f; }
                return;
            }
            _cooldown -= dt;
            if (_winding)
            {
                _wind += dt;
                if (Anim != null) { Anim.Speed = 0f; Anim.Strike = Mathf.Clamp01(_wind / 0.6f); }
                if (_wind >= 0.6f)
                {
                    _winding = false; _wind = 0f; _cooldown = 1.1f;
                    if (Anim != null) Anim.Strike = 0f;
                    if (dist < 2.6f)
                    {
                        if (Target.TryParry()) { _stagger = 1.4f; Parried++; }      // knocked off balance
                        else
                        {
                            Target.Health.Hurt(Damage, gameObject);
                            Landed++;
                            Vector3 shove = Target.transform.position - transform.position; shove.y = 0f;
                            Target.Knock(shove.normalized * 4f);
                        }
                    }
                }
                return;
            }
            if (dist > 2.1f)
            {
                _cc.SimpleMove(to.normalized * WalkSpeed);
                if (Anim != null) { Anim.Speed = 1.4f; Anim.Strike = 0f; }
            }
            else
            {
                if (Anim != null) Anim.Speed = 0f;
                if (_cooldown <= 0f) { _winding = true; _wind = 0f; }
            }
        }
    }
}
