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

        public static Soldier Spawn(Vector3 pos, ThirdPersonPlayer target, int index)
        {
            Color cloth = new Color(0.55f, 0.1f, 0.08f);
            Color cloak = new Color(0.45f, 0.35f, 0.1f);
            GameObject go = CharacterFactory.Spawn(pos, 0f, cloth, cloak, new Color(0.15f, 0.1f, 0.07f), "Soldier" + index);
            if (go == null) return null;
            CharacterController cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f); cc.stepOffset = 0.4f;
            Soldier s = go.AddComponent<Soldier>();
            s.Target = target;
            s.Anim = go.GetComponent<RiderAnimator>();
            s.Health = go.AddComponent<Damageable>();
            s.Health.MaxHealth = s.Health.Health = 55f;
            s.Health.DisplayName = "Soldier";
            return s;
        }

        private void Start()
        {
            _cc = GetComponent<CharacterController>();
            Health.OnDied += d => { _deadTime = 0.01f; };
        }

        private void Update()
        {
            if (_cc == null || Target == null) return;
            float dt = Time.deltaTime;
            if (Health.Dead)
            {
                _deadTime += dt;
                _cc.enabled = false;
                float t = Mathf.Clamp01(_deadTime / 0.6f);
                transform.rotation = Quaternion.Slerp(Quaternion.Euler(0f, _facing, 0f), Quaternion.Euler(-88f, _facing, 0f), t);
                if (Anim != null) { Anim.Speed = 0f; Anim.Strike = 0f; }
                if (_deadTime > 10f) Destroy(gameObject);
                return;
            }
            if (Target.Health.Dead) { if (Anim != null) Anim.Speed = 0f; return; }

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
                        if (Target.TryParry()) _stagger = 1.4f;       // knocked off balance
                        else Target.Health.Hurt(12f, gameObject);
                    }
                }
                return;
            }
            if (dist > 2.1f)
            {
                _cc.SimpleMove(to.normalized * 3.1f);
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
