using UnityEngine;

namespace Emberbond
{
    /// <summary>The rider on foot: movement, sprint with stamina, sword swings and a parry window.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThirdPersonPlayer : MonoBehaviour
    {
        public Damageable Health;
        public float Stamina = 100f, MaxStamina = 100f;
        public bool Mounted;
        public Transform Visual;
        public Transform SwordArm;
        public float ParryWindow;          // > 0 while a parry would land
        public float SwingTimer;
        public float YawFacing;

        private CharacterController _cc;
        private Vector3 _vel;
        private float _swingCooldown;
        private float _staminaRegenDelay;
        private float _moveBlend;

        public static ThirdPersonPlayer Create(Vector3 position)
        {
            GameObject go = new GameObject("Rider");
            go.transform.position = position;
            CharacterController cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.45f; cc.slopeLimit = 52f;
            ThirdPersonPlayer p = go.AddComponent<ThirdPersonPlayer>();
            p.Health = go.AddComponent<Damageable>();
            p.Health.MaxHealth = p.Health.Health = 120f;
            p.Health.DisplayName = "Heir-Born";
            p.BuildVisual();
            return p;
        }

        private void BuildVisual()
        {
            GameObject v = new GameObject("Visual");
            v.transform.SetParent(transform, false);
            Visual = v.transform;
            Material cloth = Mat(new Color(0.09f, 0.08f, 0.08f), 0.2f);
            Material leather = Mat(new Color(0.27f, 0.16f, 0.09f), 0.25f);
            Material steel = Mat(new Color(0.55f, 0.57f, 0.6f), 0.7f);
            Material hair = Mat(new Color(0.88f, 0.86f, 0.8f), 0.3f);
            Material skin = Mat(new Color(0.75f, 0.58f, 0.47f), 0.3f);
            Part(v, PrimitiveType.Capsule, new Vector3(0f, 0.45f, 0f), new Vector3(0.38f, 0.42f, 0.3f), cloth);       // legs
            Part(v, PrimitiveType.Cube, new Vector3(0f, 1.18f, 0f), new Vector3(0.62f, 0.62f, 0.34f), leather);        // torso
            Part(v, PrimitiveType.Cube, new Vector3(0f, 1.38f, 0f), new Vector3(0.78f, 0.22f, 0.4f), steel);           // pauldrons
            Part(v, PrimitiveType.Sphere, new Vector3(0f, 1.68f, 0f), new Vector3(0.26f, 0.3f, 0.26f), skin);          // head
            Part(v, PrimitiveType.Sphere, new Vector3(0f, 1.74f, -0.04f), new Vector3(0.29f, 0.26f, 0.3f), hair);      // hair
            Part(v, PrimitiveType.Cube, new Vector3(0f, 1.0f, -0.22f), new Vector3(0.7f, 1.35f, 0.06f), cloth);        // cloak
            GameObject arm = new GameObject("SwordArm");
            arm.transform.SetParent(v.transform, false);
            arm.transform.localPosition = new Vector3(0.42f, 1.3f, 0f);
            SwordArm = arm.transform;
            Part(arm, PrimitiveType.Capsule, new Vector3(0f, -0.28f, 0f), new Vector3(0.14f, 0.3f, 0.14f), leather);
            Part(arm, PrimitiveType.Cube, new Vector3(0f, -0.5f, 0.45f), new Vector3(0.05f, 0.04f, 0.95f), steel);     // blade
        }

        private static Material Mat(Color c, float smooth)
        {
            Material m = new Material(Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        private static GameObject Part(GameObject parent, PrimitiveType t, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject g = GameObject.CreatePrimitive(t);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        private void Start() { _cc = GetComponent<CharacterController>(); }

        public void SetMounted(bool mounted)
        {
            Mounted = mounted;
            if (_cc == null) _cc = GetComponent<CharacterController>();
            _cc.enabled = !mounted;
        }

        private void Update()
        {
            if (Mounted || Health.Dead) return;
            if (_cc == null) _cc = GetComponent<CharacterController>();
            float dt = Time.deltaTime;

            Transform cam = Camera.main != null ? Camera.main.transform : null;
            float camYaw = cam != null ? cam.eulerAngles.y : 0f;
            Vector3 fwd = Quaternion.Euler(0f, camYaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, camYaw, 0f) * Vector3.right;
            Vector3 wish = fwd * InputState.Move.y + right * InputState.Move.x;
            bool sprint = InputState.Sprint && Stamina > 5f && wish.sqrMagnitude > 0.01f;
            float speed = sprint ? 7.8f : 4.4f;
            if (InputState.Crouch) speed *= 0.5f;
            Vector3 horiz = wish * speed;

            if (_cc.isGrounded)
            {
                _vel.y = -1f;
                if (InputState.JumpDown && Stamina > 10f) { _vel.y = 6.2f; Stamina -= 8f; }
            }
            _vel.y -= 20f * dt;
            Vector3 motion = new Vector3(horiz.x, _vel.y, horiz.z) * dt;
            _cc.Move(motion);

            if (wish.sqrMagnitude > 0.01f)
            {
                YawFacing = Mathf.LerpAngle(YawFacing, Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-14f * dt));
                _moveBlend = Mathf.MoveTowards(_moveBlend, 1f, dt * 6f);
            }
            else _moveBlend = Mathf.MoveTowards(_moveBlend, 0f, dt * 6f);
            Visual.rotation = Quaternion.Euler(0f, YawFacing, 0f);
            Visual.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.time * (sprint ? 12f : 8f))) * 0.06f * _moveBlend, 0f);

            // Stamina
            if (sprint) { Stamina -= 14f * dt; _staminaRegenDelay = 0.8f; }
            else if (_staminaRegenDelay > 0f) _staminaRegenDelay -= dt;
            else Stamina = Mathf.Min(MaxStamina, Stamina + 22f * dt);
            Stamina = Mathf.Clamp(Stamina, 0f, MaxStamina);

            // Combat
            _swingCooldown -= dt;
            if (ParryWindow > 0f) ParryWindow -= dt;
            if (SwingTimer > 0f) SwingTimer -= dt;
            if (InputState.AttackDown && _swingCooldown <= 0f && Stamina >= 12f)
            {
                _swingCooldown = 0.55f; SwingTimer = 0.35f; Stamina -= 12f; _staminaRegenDelay = 0.6f;
                Strike();
            }
            if (InputState.BlockDown && Stamina >= 8f) { ParryWindow = 0.35f; Stamina -= 8f; _staminaRegenDelay = 0.6f; }

            if (SwordArm != null)
            {
                float swing = SwingTimer > 0f ? Mathf.Sin((1f - SwingTimer / 0.35f) * Mathf.PI) : 0f;
                float parry = ParryWindow > 0f ? 1f : 0f;
                SwordArm.localRotation = Quaternion.Euler(-30f * parry - 95f * swing, 0f, -10f * parry);
            }
        }

        private void Strike()
        {
            Vector3 origin = transform.position + Vector3.up * 1.1f;
            Vector3 dir = Quaternion.Euler(0f, YawFacing, 0f) * Vector3.forward;
            Collider[] cols = Physics.OverlapSphere(origin + dir * 1.3f, 1.7f);
            for (int i = 0; i < cols.Length; i++)
            {
                Damageable d = cols[i].GetComponentInParent<Damageable>();
                if (d == null || d == Health || d.Dead) continue;
                d.Hurt(24f, gameObject);
            }
        }

        /// <summary>Incoming blow from an enemy; a well-timed parry negates it and staggers the attacker.</summary>
        public bool TryParry()
        {
            if (ParryWindow > 0f) { ParryWindow = 0f; Stamina = Mathf.Min(MaxStamina, Stamina + 15f); return true; }
            return false;
        }
    }

    /// <summary>Third-person orbit camera on foot, and a chase camera behind the dragon in flight.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Transform FootTarget;
        public Transform FlightTarget;
        public bool Flight;
        public float FlightSpeed;
        public float Yaw = 20f, Pitch = 12f;
        public bool Frozen;          // used by scripted shots
        public float FlightBack = 22f, FlightUp = 6f;

        private float _fov = 60f;
        private Camera _cam;
        private Vector3 _smooth;

        private void Start() { _cam = GetComponent<Camera>(); }

        private void LateUpdate()
        {
            if (Frozen || _cam == null) return;
            if (Flight && FlightTarget != null)
            {
                Vector3 f = FlightTarget.forward;
                Vector3 want = FlightTarget.position - f * FlightBack + Vector3.up * FlightUp;
                _smooth = Vector3.Lerp(_smooth == Vector3.zero ? want : _smooth, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
                transform.position = _smooth;
                Quaternion look = Quaternion.LookRotation((FlightTarget.position + f * 14f + Vector3.up * 1.5f) - transform.position, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-8f * Time.deltaTime));
                float tf = 62f + Mathf.Clamp(FlightSpeed - 30f, 0f, 40f) * 0.45f;
                _fov = Mathf.Lerp(_fov, tf, 1f - Mathf.Exp(-3f * Time.deltaTime));
            }
            else if (FootTarget != null)
            {
                Yaw += InputState.Look.x;
                Pitch = Mathf.Clamp(Pitch - InputState.Look.y, -25f, 60f);
                Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
                Vector3 pivot = FootTarget.position + Vector3.up * 1.7f + rot * Vector3.right * 0.55f;
                float dist = 4.4f;
                Vector3 dir = rot * Vector3.back;
                RaycastHit hit;
                if (Physics.SphereCast(pivot, 0.25f, dir, out hit, dist, ~0, QueryTriggerInteraction.Ignore)) dist = Mathf.Max(0.8f, hit.distance - 0.1f);
                Vector3 want = pivot + dir * dist;
                transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-18f * Time.deltaTime));
                transform.rotation = rot;
                _smooth = Vector3.zero;
                _fov = Mathf.Lerp(_fov, 58f, 1f - Mathf.Exp(-4f * Time.deltaTime));
            }
            _cam.fieldOfView = _fov;
        }
    }
}
