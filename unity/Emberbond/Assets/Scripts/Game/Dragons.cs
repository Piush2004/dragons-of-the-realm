using UnityEngine;

namespace Emberbond
{
    /// <summary>The bonded wyrm: perches near the rider, can be mounted, flies with arcade momentum and breathes fire.</summary>
    public sealed class DragonFlight : MonoBehaviour
    {
        public ThirdPersonPlayer Rider;
        public CameraRig Rig;
        public Damageable Health;
        public bool Mounted;
        public float Speed;
        public float ModelYaw;               // correction between the model's forward and Unity's +Z
        public Transform Seat;               // where the rider sits

        private float _yaw, _pitch, _roll;
        private float _fireCooldown;
        private Vector3 _perchPos;
        private float _flapPhase;
        private Transform _model;
        private DragonAnimator _anim;
        private BoxCollider _body;
        private float _impactCooldown;

        public float BondTrust = 20f;
        public float BodyRadius = 2.4f;
        public int Crashes;
        public float LastImpact;

        /// <summary>Points the flight along a direction (used when placing the dragon for tests and cutscenes).</summary>
        public void Aim(Vector3 dir)
        {
            dir.Normalize();
            _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -65f, 65f);
        }

        public void Init(ThirdPersonPlayer rider, CameraRig rig)
        {
            Rider = rider; Rig = rig;
            _model = transform;
            Health = GetComponent<Damageable>();
            if (Health == null) Health = gameObject.AddComponent<Damageable>();
            Health.MaxHealth = Health.Health = 400f;
            Health.DisplayName = "Your wyrm";
            _yaw = transform.eulerAngles.y;
            _perchPos = transform.position;
            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(transform, false);
            seat.transform.localPosition = new Vector3(0f, 0.45f, 0.05f);
            Seat = seat.transform;
            _anim = GetComponent<DragonAnimator>();
            // A solid body so the player and soldiers cannot walk through a perched dragon.
            Vector3 ls = transform.lossyScale;
            _body = gameObject.AddComponent<BoxCollider>();
            _body.center = new Vector3(0f, 1.2f / Mathf.Max(ls.y, 0.01f), 0f);
            _body.size = new Vector3(3.2f / Mathf.Max(ls.x, 0.01f), 3.2f / Mathf.Max(ls.y, 0.01f), 13f / Mathf.Max(ls.z, 0.01f));
        }

        public bool RiderNear { get { return Rider != null && Vector3.Distance(Rider.transform.position, transform.position) < 14f; } }

        private void Update()
        {
            if (Rider == null) return;
            float dt = Time.deltaTime;
            if (!Mounted)
            {
                // Idle: breathe, and keep an eye on the rider.
                transform.position = new Vector3(transform.position.x, GroundHeight(transform.position) + 1.2f + Mathf.Sin(Time.time * 1.1f) * 0.08f, transform.position.z);
                Vector3 to = Rider.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 4f)
                {
                    Quaternion want = Quaternion.LookRotation(to) * Quaternion.Euler(0f, ModelYaw, 0f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-1.5f * dt));
                }
                if (_anim != null) { _anim.FlapRate = 0.45f; _anim.FlapAmplitude = 0.12f; _anim.JawOpen = 0f; _anim.Bank = 0f; }
                if (InputState.Interact && RiderNear) Mount();
                if (InputState.Summon) CallToRider();
                return;
            }
            Fly(dt);
        }

        private void CallToRider()
        {
            Vector3 p = Rider.transform.position + Rider.transform.forward * -10f + Vector3.up * 0f;
            transform.position = new Vector3(p.x, GroundHeight(p) + 1.2f, p.z);
        }

        private float GroundHeight(Vector3 p)
        {
            Terrain t = Terrain.activeTerrain;
            float h = t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
            return Mathf.Max(h, IslandSea());
        }

        private static float IslandSea() { return 80f; }

        public void Mount()
        {
            Mounted = true;
            Rider.SetMounted(true);
            _yaw = transform.eulerAngles.y - ModelYaw;
            _pitch = 0f; Speed = 18f;
            Rig.Flight = true; Rig.FlightTarget = transform;
            transform.position += Vector3.up * 3f;
        }

        public void Dismount()
        {
            Mounted = false;
            Vector3 p = transform.position;
            Rider.transform.position = new Vector3(p.x + 3f, GroundHeight(p) + 0.3f, p.z);
            Rider.SetMounted(false);
            Rig.Flight = false;
            transform.rotation = Quaternion.Euler(0f, _yaw + ModelYaw, 0f);
        }

        /// <summary>
        /// Moves the dragon through the world with a swept sphere: it stops at surfaces, slides along them, loses speed,
        /// and takes damage (and shakes the camera) in proportion to how hard it hit.
        /// </summary>
        private Vector3 SweepMove(Vector3 from, Vector3 delta)
        {
            float dist = delta.magnitude;
            if (dist < 1e-4f) return from;
            Vector3 dir = delta / dist;
            RaycastHit[] hits = Physics.SphereCastAll(from, BodyRadius, dir, dist + 0.2f, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit best = default(RaycastHit);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
                if (h.collider == null) continue;
                Transform ht = h.collider.transform;
                if (ht.IsChildOf(transform) || (Rider != null && ht.IsChildOf(Rider.transform))) continue;
                if (h.distance <= 0f) continue;                  // already overlapping at the start: ignore, do not trap
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            if (!found) return from + delta;

            Vector3 n = best.normal;
            float into = Mathf.Clamp01(-Vector3.Dot(dir, n));
            float impact = Speed * into;
            Vector3 stop = from + dir * Mathf.Max(0f, best.distance - 0.05f);
            Vector3 remaining = delta - (stop - from);
            Vector3 slide = remaining - n * Vector3.Dot(remaining, n);
            Vector3 along = dir - n * Vector3.Dot(dir, n);
            if (along.sqrMagnitude > 0.02f)
            {
                Aim(along);
            }
            Speed *= Mathf.Lerp(1f, 0.4f, into);
            OnImpact(impact, best.point, n);
            return stop + n * 0.08f + slide * 0.6f;
        }

        private void OnImpact(float impact, Vector3 point, Vector3 normal)
        {
            if (impact < 5f || _impactCooldown > 0f) return;
            _impactCooldown = 0.5f;
            Crashes++;
            LastImpact = impact;
            Rig.Shake(Mathf.Clamp01(impact / 45f));
            Effects.Burst(point, Mathf.Lerp(2f, 7f, Mathf.Clamp01(impact / 40f)));
            float hurt = Mathf.Max(0f, impact - 12f) * 3f;
            if (hurt > 0f)
            {
                Health.Hurt(hurt, null);
                if (Rider != null) Rider.Health.Hurt(hurt * 0.3f, null);
            }
            Debug.Log("[Emberbond] impact " + impact.ToString("0") + " m/s damage " + hurt.ToString("0"));
        }

        private void Fly(float dt)
        {
            _yaw += InputState.Look.x * 1.2f;
            _pitch = Mathf.Clamp(_pitch + InputState.Look.y * 1.1f, -65f, 65f);
            float targetRoll = -Mathf.Clamp(InputState.Look.x * 9f, -50f, 50f);
            _roll = Mathf.Lerp(_roll, targetRoll, 1f - Mathf.Exp(-4f * dt));
            // The look input steers; releasing it levels the pitch out slowly.
            if (Mathf.Abs(InputState.Look.y) < 0.01f) _pitch = Mathf.Lerp(_pitch, 0f, 1f - Mathf.Exp(-0.8f * dt));

            float target = 34f + InputState.Move.y * 12f + (InputState.Boost ? 26f : 0f);
            target += Mathf.Clamp(-_pitch, -20f, 20f) * 0.3f;         // dive faster, climb slower
            Speed = Mathf.Lerp(Speed, target, 1f - Mathf.Exp(-1.2f * dt));

            Quaternion flight = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 fwd = flight * Vector3.forward;
            Vector3 pos = SweepMove(transform.position, fwd * Speed * dt + Vector3.up * InputState.Vertical * 8f * dt);
            _impactCooldown -= dt;
            flight = Quaternion.Euler(_pitch, _yaw, 0f);        // the sweep may have turned us along a surface
            fwd = flight * Vector3.forward;

            float floor = GroundHeight(pos) + 4f;
            if (pos.y < floor)
            {
                pos.y = floor;
                if (_pitch > 0f) _pitch = Mathf.Lerp(_pitch, -8f, 0.1f);
            }
            transform.position = pos;
            transform.rotation = flight * Quaternion.Euler(0f, ModelYaw, -_roll * 0.0f) * Quaternion.Euler(0f, 0f, _roll);

            // Gentle flapping bob; real wing animation comes with the rigged model.
            _flapPhase += dt * (6f + Speed * 0.04f);
            Rig.FlightSpeed = Speed;
            if (_anim != null)
            {
                bool climbing = _pitch < -8f;
                _anim.FlapRate = Mathf.Lerp(0.9f, 1.8f, Mathf.InverseLerp(20f, 70f, Speed)) + (climbing ? 0.5f : 0f);
                _anim.FlapAmplitude = _pitch > 22f ? 0.1f : Mathf.Lerp(0.5f, 0.85f, Mathf.InverseLerp(20f, 70f, Speed));   // glide on a dive
                _anim.Bank = Mathf.Clamp(_roll / 40f, -1f, 1f);
                _anim.JawOpen = _fireCooldown > 0.5f ? 1f : 0f;
            }

            if (Rider != null) Rider.transform.position = Seat.position;
            _fireCooldown -= dt;
            if (InputState.Fire && _fireCooldown <= 0f)
            {
                _fireCooldown = 0.9f;
                Vector3 mouth = transform.position + fwd * 5.5f + Vector3.up * 0.3f;
                Fireball.Spawn(mouth, fwd * 120f + fwd * Speed, gameObject, 40f);
            }
            if (InputState.Interact && transform.position.y - GroundHeight(transform.position) < 20f) Dismount();
        }
    }

    /// <summary>A rival dragon that circles the player and breathes fire at range.</summary>
    public sealed class EnemyDragon : MonoBehaviour
    {
        public Damageable Health;
        public Transform Target;
        public float ModelYaw;
        private float _orbit;
        private float _fireTimer = 4f;
        private Vector3 _vel;
        private DragonAnimator _anim;

        public void Init(Transform target)
        {
            Target = target;
            Health = GetComponent<Damageable>();
            if (Health == null) Health = gameObject.AddComponent<Damageable>();
            Health.MaxHealth = Health.Health = 600f;
            Health.DisplayName = "The Old Wyrm";
            Health.IsBoss = true;
            Health.OnDied += d => { Destroy(gameObject, 0.5f); };
            _anim = GetComponent<DragonAnimator>();
            Collider c = gameObject.AddComponent<SphereCollider>();
            ((SphereCollider)c).radius = 6f;
        }

        private void Update()
        {
            if (Target == null || Health.Dead) return;
            float dt = Time.deltaTime;
            _orbit += dt * 0.18f;
            Vector3 centre = Target.position;
            Vector3 want = centre + new Vector3(Mathf.Cos(_orbit) * 170f, 45f + Mathf.Sin(_orbit * 1.7f) * 20f, Mathf.Sin(_orbit) * 170f);
            Vector3 to = want - transform.position;
            Vector3 desired = to.normalized * Mathf.Min(40f, to.magnitude);
            _vel = Vector3.Lerp(_vel, desired, 1f - Mathf.Exp(-1.2f * dt));
            transform.position += _vel * dt;
            Vector3 look = (Target.position - transform.position);
            if (_vel.sqrMagnitude > 1f) look = Vector3.Lerp(_vel, look, 0.35f);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized, Vector3.up) * Quaternion.Euler(0f, ModelYaw, 0f), 1f - Mathf.Exp(-2f * dt));

            if (_anim != null) { _anim.FlapRate = 1.1f; _anim.FlapAmplitude = 0.7f; _anim.JawOpen = _fireTimer < 0.5f ? 1f : 0f; }
            _fireTimer -= dt;
            if (_fireTimer <= 0f && Vector3.Distance(Target.position, transform.position) < 260f)
            {
                _fireTimer = Random.Range(3.5f, 6f);
                Vector3 mouth = transform.position + transform.forward * 5f;
                Vector3 aim = (Target.position - mouth).normalized;
                Fireball.Spawn(mouth, aim * 95f, gameObject, 22f);
            }
        }
    }
}
