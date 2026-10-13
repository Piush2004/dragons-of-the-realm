import os

root = r'D:\Projects\Games\claude\unity\Emberbond'


def patch(rel, pairs):
    p = os.path.join(root, rel)
    s = open(p, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    s = s.replace('\r\n', '\n')
    for a, b in pairs:
        assert s.count(a) == 1, (rel, a[:80], s.count(a))
        s = s.replace(a, b)
    if crlf:
        s = s.replace('\n', '\r\n')
    open(p, 'w', encoding='utf-8', newline='').write(s)


# ---------------------------------------------------------------- dragon flight collisions
patch('Assets/Scripts/Game/Dragons.cs', [
    ('''        private DragonAnimator _anim;

        public float BondTrust = 20f;''',
     '''        private DragonAnimator _anim;
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
        }'''),
    ('''            Seat = seat.transform;
            _anim = GetComponent<DragonAnimator>();''',
     '''            Seat = seat.transform;
            _anim = GetComponent<DragonAnimator>();
            // A solid body so the player and soldiers cannot walk through a perched dragon.
            Vector3 ls = transform.lossyScale;
            _body = gameObject.AddComponent<BoxCollider>();
            _body.center = new Vector3(0f, 1.2f / Mathf.Max(ls.y, 0.01f), 0f);
            _body.size = new Vector3(3.2f / Mathf.Max(ls.x, 0.01f), 3.2f / Mathf.Max(ls.y, 0.01f), 13f / Mathf.Max(ls.z, 0.01f));'''),
    ('''            Vector3 pos = transform.position + fwd * Speed * dt + Vector3.up * InputState.Vertical * 8f * dt;
''',
     '''            Vector3 pos = SweepMove(transform.position, fwd * Speed * dt + Vector3.up * InputState.Vertical * 8f * dt);
            _impactCooldown -= dt;
            flight = Quaternion.Euler(_pitch, _yaw, 0f);        // the sweep may have turned us along a surface
            fwd = flight * Vector3.forward;
'''),
    ('''        private void Fly(float dt)
        {''',
     '''        /// <summary>
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
        {'''),
])

patch('Assets/Scripts/Game/PlayerAndCamera.cs', [
    ('''        public bool Frozen;          // used by scripted shots''',
     '''        public bool Frozen;          // used by scripted shots
        public float ShakeAmount;

        public void Shake(float amount) { ShakeAmount = Mathf.Max(ShakeAmount, amount); }'''),
    ('''            _cam.fieldOfView = _fov;''',
     '''            if (ShakeAmount > 0.001f)
            {
                transform.position += Random.insideUnitSphere * ShakeAmount * 0.9f;
                transform.rotation *= Quaternion.Euler(Random.Range(-1f, 1f) * ShakeAmount * 2f, Random.Range(-1f, 1f) * ShakeAmount * 2f, 0f);
                ShakeAmount = Mathf.MoveTowards(ShakeAmount, 0f, Time.deltaTime * 1.6f);
            }
            _cam.fieldOfView = _fov;'''),
    ('''            if (_cc.isGrounded)
            {
                _vel.y = -1f;
                if (InputState.JumpDown && Stamina > 10f) { _vel.y = 6.2f; Stamina -= 8f; }
            }
            _vel.y -= 20f * dt;
            Vector3 motion = new Vector3(horiz.x, _vel.y, horiz.z) * dt;
            _cc.Move(motion);''',
     '''            bool wasGrounded = _cc.isGrounded;
            float prevVy = _vel.y;
            bool swimming = transform.position.y + 0.9f < SeaLevel;
            if (swimming)
            {
                // Wading and swimming: slow, buoyant, and tiring.
                horiz *= 0.55f;
                _vel.y = Mathf.Lerp(_vel.y, InputState.Jump ? 2.6f : -0.8f, 1f - Mathf.Exp(-4f * dt));
                Stamina = Mathf.Max(0f, Stamina - 7f * dt);
                if (Stamina <= 0.5f) Health.Hurt(5f * dt, null);
            }
            else
            {
                if (wasGrounded)
                {
                    _vel.y = -1f;
                    if (InputState.JumpDown && Stamina > 10f) { _vel.y = 6.2f; Stamina -= 8f; }
                }
                _vel.y -= 20f * dt;
            }
            Vector3 motion = new Vector3(horiz.x, _vel.y, horiz.z) * dt;
            _cc.Move(motion + _impulse * dt);
            _impulse = Vector3.Lerp(_impulse, Vector3.zero, 1f - Mathf.Exp(-7f * dt));
            if (!wasGrounded && _cc.isGrounded && prevVy < -15f)
            {
                float dmg = (-prevVy - 15f) * 4f;                 // a fall from about 12 m starts to hurt
                Health.Hurt(dmg, null);
                Debug.Log("[Emberbond] landed hard " + (-prevVy).ToString("0") + " m/s damage " + dmg.ToString("0"));
            }'''),
    ('''        private CharacterController _cc;
        private Vector3 _vel;''',
     '''        private CharacterController _cc;
        private Vector3 _vel;
        private Vector3 _impulse;
        public const float SeaLevel = 80f;

        /// <summary>A shove from a blow or an explosion; decays over about half a second.</summary>
        public void Knock(Vector3 velocity) { _impulse += new Vector3(velocity.x, 0f, velocity.z); if (velocity.y > 0f) _vel.y = Mathf.Max(_vel.y, velocity.y); }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody rb = hit.collider.attachedRigidbody;
            if (rb == null || rb.isKinematic) return;
            Vector3 push = hit.moveDirection; push.y = 0f;
            if (push.sqrMagnitude < 0.01f) return;
            rb.AddForceAtPosition(push.normalized * 5.5f, hit.point, ForceMode.Impulse);
        }'''),
    ('''                if (nd == null || nd == Health || nd.Dead) continue;
                Vector3 off = nd.transform.position - transform.position; off.y = 0f;''',
     '''                if (nd == null || nd == Health || nd.Dead) continue;
                Vector3 off = nd.transform.position - transform.position; off.y = 0f;'''),
    ('''                d.Hurt(24f, gameObject);''',
     '''                d.Hurt(24f, gameObject);
                Rigidbody hrb = cols[i].attachedRigidbody;
                if (hrb != null) hrb.AddForce(dir * 6f + Vector3.up * 1.5f, ForceMode.Impulse);'''),
])

patch('Assets/Scripts/Game/Soldier.cs', [
    ('''        private float _deadTime;
''',
     '''        private float _deadTime;
        private bool _ragdoll;
        private Vector3 _knock, _lastHit;
'''),
    ('''            Health.OnDied += d => { _deadTime = 0.01f; };''',
     '''            Health.OnDied += d => { _deadTime = 0.01f; };
            Health.OnDamaged += (d, amount, src) =>
            {
                if (src == null) return;
                Vector3 away = transform.position - src.transform.position;
                away.y = 0f;
                _lastHit = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
                _knock = _lastHit * 5.5f;
                _stagger = Mathf.Max(_stagger, 0.3f);
            };'''),
    ('''                _deadTime += dt;
                _cc.enabled = false;
                float t = Mathf.Clamp01(_deadTime / 0.6f);
                transform.rotation = Quaternion.Slerp(Quaternion.Euler(0f, _facing, 0f), Quaternion.Euler(-88f, _facing, 0f), t);
                if (Anim != null) { Anim.Speed = 0f; Anim.Strike = 0f; }
                if (_deadTime > 10f) Destroy(gameObject);
                return;''',
     '''                _deadTime += dt;
                if (!_ragdoll) BecomeRagdoll();
                if (Anim != null) { Anim.Speed = 0f; Anim.Strike = 0f; }
                if (_deadTime > 14f) Destroy(gameObject);
                return;'''),
    ('''            Vector3 to = Target.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;''',
     '''            if (_knock.sqrMagnitude > 0.01f)
            {
                _cc.Move(_knock * dt);
                _knock = Vector3.Lerp(_knock, Vector3.zero, 1f - Mathf.Exp(-9f * dt));
            }
            Vector3 to = Target.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;'''),
    ('''        private void Update()
        {
            if (_cc == null || Target == null) return;''',
     '''        /// <summary>On death the soldier becomes a rigid body that is flung by the killing blow and falls where it lands.</summary>
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
            if (_cc == null || Target == null) return;'''),
])

patch('Assets/Scripts/Game/Fireball.cs', [
    ('''            Effects.Burst(at, Radius);
            Destroy(gameObject);''',
     '''            // Blast force on anything that is a rigid body, in a slightly wider ring than the damage.
            Collider[] around = Physics.OverlapSphere(at, Radius * 1.7f);
            for (int i = 0; i < around.Length; i++)
            {
                Rigidbody rb = around[i].attachedRigidbody;
                if (rb != null && !rb.isKinematic) rb.AddExplosionForce(1800f, at, Radius * 1.7f, 0.9f, ForceMode.Impulse);
                ThirdPersonPlayer pl = around[i].GetComponentInParent<ThirdPersonPlayer>();
                if (pl != null && (Owner == null || pl.gameObject != Owner))
                {
                    Vector3 away = pl.transform.position - at; away.y = 0f;
                    pl.Knock(away.normalized * 9f + Vector3.up * 3f);
                }
            }
            Effects.Burst(at, Radius);
            Destroy(gameObject);'''),
])
print('physics patch ok')
