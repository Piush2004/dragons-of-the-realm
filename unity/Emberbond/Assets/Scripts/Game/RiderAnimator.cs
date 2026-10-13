using System.Collections.Generic;
using UnityEngine;

namespace Emberbond
{
    /// <summary>Procedural animation for the Blender humanoid: idle, walk, run, sword strike, parry and riding pose.</summary>
    public sealed class RiderAnimator : MonoBehaviour
    {
        public float Speed;          // 0 idle .. 1 walk .. 2 run
        public float Strike;         // 0..1 progress of the current sword swing, 0 when not swinging
        public float Parry;          // 0..1 how raised the guard is
        public bool Seated;          // astride a dragon

        private sealed class B { public Transform T; public Quaternion Rest; public Quaternion ParentRest; }
        private readonly Dictionary<string, B> _b = new Dictionary<string, B>();
        private float _phase, _lastS;
        private Transform _root;

        private void Awake()
        {
            _root = transform;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t == _root || t.parent == null) continue;
                if (!_b.ContainsKey(t.name))
                    _b.Add(t.name, new B { T = t, Rest = t.localRotation, ParentRest = Quaternion.Inverse(_root.rotation) * t.parent.rotation });
            }
        }

        private void Pose(string n, Quaternion model)
        {
            B b;
            if (_b.TryGetValue(n, out b)) b.T.localRotation = Quaternion.Inverse(b.ParentRest) * model * b.ParentRest * b.Rest;
        }

        private static Quaternion X(float deg) { return Quaternion.AngleAxis(deg, Vector3.right); }
        private static Quaternion Y(float deg) { return Quaternion.AngleAxis(deg, Vector3.up); }
        private static Quaternion Z(float deg) { return Quaternion.AngleAxis(deg, Vector3.forward); }

        private void LateUpdate()
        {
            if (!_b.ContainsKey("hips")) return;
            float dt = Time.deltaTime;
            _phase += dt * (3.2f + Speed * 4.2f);
            float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);
            float amp = Mathf.Clamp01(Speed);
            float run = Mathf.Clamp01(Speed - 1f);

            if (Seated)
            {
                Pose("thigh_L", X(-75f) * Z(-18f)); Pose("thigh_R", X(-75f) * Z(18f));
                Pose("shin_L", X(70f)); Pose("shin_R", X(70f));
                Pose("upper_arm_L", X(-55f)); Pose("upper_arm_R", X(-55f));
                Pose("lower_arm_L", X(-35f)); Pose("lower_arm_R", X(-35f));
                Pose("spine", X(-12f)); Pose("chest", X(-6f));
                Pose("braid", X(Mathf.Sin(_phase * 0.7f) * 8f));
                Pose("cloak_top", X(-18f + Mathf.Sin(_phase) * 5f));
                Pose("cloak_low", X(-24f + Mathf.Sin(_phase * 1.3f) * 8f));
                return;
            }

            if (Speed > 0.4f && Mathf.Sign(s) != Mathf.Sign(_lastS)) AudioDirector.Play(Sfx.Step, transform.position, 0.3f);
            _lastS = s;
            float stride = 32f * amp + 22f * run;
            Pose("thigh_L", X(-s * stride));
            Pose("thigh_R", X(s * stride));
            Pose("shin_L", X(Mathf.Max(0f, c) * 48f * amp + 12f * run));
            Pose("shin_R", X(Mathf.Max(0f, -c) * 48f * amp + 12f * run));
            Pose("foot_L", X(Mathf.Max(0f, -c) * 14f * amp));
            Pose("foot_R", X(Mathf.Max(0f, c) * 14f * amp));
            Pose("hips", Y(s * 5f * amp) * Z(s * 2f * amp));
            Pose("spine", Y(-s * 6f * amp) * X(run * -9f));
            Pose("chest", Y(-s * 4f * amp));
            float breathe = Mathf.Sin(Time.time * 1.6f) * 1.2f;
            Pose("neck", X(breathe * 0.5f));
            Pose("head", Y(s * 2f * amp));

            // Arms swing against the legs; the sword arm (right) is overridden while striking or parrying.
            float armSwing = 26f * amp + 18f * run;
            Pose("upper_arm_L", X(s * armSwing) * Z(-4f));
            Pose("lower_arm_L", X(-12f - Mathf.Max(0f, -s) * 20f * amp));
            Quaternion rightUpper = X(-s * armSwing) * Z(4f);
            Quaternion rightLower = X(-12f - Mathf.Max(0f, s) * 20f * amp);
            if (Strike > 0f)
            {
                float t = Strike;
                float raise = Mathf.Sin(t * Mathf.PI);
                float over = t < 0.35f ? Mathf.Lerp(0f, -140f, t / 0.35f) : Mathf.Lerp(-140f, 25f, (t - 0.35f) / 0.65f);
                rightUpper = X(over) * Y(raise * -12f);
                rightLower = X(-25f - raise * 30f);
                Pose("spine", Y(Mathf.Lerp(-18f, 22f, t)));
            }
            else if (Parry > 0f)
            {
                rightUpper = X(-70f * Parry) * Z(20f * Parry);
                rightLower = X(-60f * Parry);
            }
            Pose("upper_arm_R", rightUpper);
            Pose("lower_arm_R", rightLower);

            float sway = Mathf.Sin(_phase * 0.5f) * (4f + 10f * amp);
            Pose("braid", X(-8f * amp + sway * 0.5f));
            Pose("cloak_top", X(8f * amp + Mathf.Sin(_phase * 0.6f) * 3f) * Y(s * 3f * amp));
            Pose("cloak_low", X(14f * amp + 10f * run + Mathf.Sin(_phase * 0.9f) * 5f));
        }
    }
}
