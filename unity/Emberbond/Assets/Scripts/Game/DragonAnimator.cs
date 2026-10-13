using System.Collections.Generic;
using UnityEngine;

namespace Emberbond
{
    /// <summary>
    /// Drives the Blender dragon rig procedurally: wing beats with a travelling wave through elbow and fingers, a
    /// swimming tail, a breathing neck and a jaw that opens when the dragon breathes fire. Rotations are expressed in the
    /// dragon's own space and converted to each bone's local space, so they compound down the hierarchy.
    /// </summary>
    public sealed class DragonAnimator : MonoBehaviour
    {
        public float FlapRate = 1.4f;          // beats per second
        public float FlapAmplitude = 0.35f;    // 0 = wings still, 1 = full beats
        public float TailSwing = 1f;
        public float JawOpen;                  // 0..1, set by whoever breathes fire
        public float Bank;                     // -1..1, tail and neck lean into turns

        private sealed class BoneState
        {
            public Transform T;
            public Quaternion RestLocal;
            public Quaternion ParentRestInModel;
        }

        private readonly Dictionary<string, BoneState> _bones = new Dictionary<string, BoneState>();
        private float _phase;
        private Transform _root;

        private void Awake()
        {
            _root = transform;
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == _root || t.parent == null) continue;
                BoneState b = new BoneState
                {
                    T = t,
                    RestLocal = t.localRotation,
                    ParentRestInModel = Quaternion.Inverse(_root.rotation) * t.parent.rotation,
                };
                if (!_bones.ContainsKey(t.name)) _bones.Add(t.name, b);
            }
        }

        public bool HasRig { get { return _bones.ContainsKey("wing_L0"); } }

        /// <summary>Applies a model-space rotation to a bone, relative to its rest pose.</summary>
        private void Pose(string name, Quaternion modelDelta)
        {
            BoneState b;
            if (!_bones.TryGetValue(name, out b)) return;
            b.T.localRotation = Quaternion.Inverse(b.ParentRestInModel) * modelDelta * b.ParentRestInModel * b.RestLocal;
        }

        private void LateUpdate()
        {
            if (!HasRig) return;
            float dt = Time.deltaTime;
            _phase += dt * FlapRate * Mathf.PI * 2f;
            float amp = FlapAmplitude;

            // The dragon's length runs along its local Z; wings fold and beat about that axis.
            Vector3 along = Vector3.forward;
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.right;

            float beat = Mathf.Sin(_phase);
            // Downstroke is faster than the recovery.
            float shaped = beat > 0f ? beat : beat * 0.7f;

            for (int s = -1; s <= 1; s += 2)
            {
                string sd = s < 0 ? "L" : "R";
                float m = s;                                // mirror sign for the right wing
                float shoulder = shaped * 38f * amp;
                float elbow = Mathf.Sin(_phase - 0.55f) * 26f * amp;
                float tip = Mathf.Sin(_phase - 1.1f) * 22f * amp;
                Pose("wing_" + sd + "0", Quaternion.AngleAxis(m * shoulder, along) * Quaternion.AngleAxis(m * 6f * Bank, up));
                Pose("wing_" + sd + "1", Quaternion.AngleAxis(m * elbow, along));
                for (int f = 2; f <= 5; f++)
                    Pose("wing_" + sd + f, Quaternion.AngleAxis(m * tip * (0.7f + 0.15f * f), along));
            }

            // Tail: a travelling sine in the horizontal plane, leaning with the bank.
            for (int i = 0; i <= 13; i++)
            {
                float w = Mathf.Sin(_phase * 0.5f - i * 0.45f) * 5f * TailSwing + Bank * 4f;
                float v = Mathf.Sin(_phase - i * 0.35f) * 2.2f * amp;
                Pose("tail_" + i, Quaternion.AngleAxis(w, up) * Quaternion.AngleAxis(v, side));
            }

            // Neck: slow breathing and a lean into turns.
            for (int i = 0; i <= 9; i++)
            {
                float breathe = Mathf.Sin(_phase * 0.25f + i * 0.3f) * 1.4f;
                float lean = Bank * 3f;
                Pose("neck_" + i, Quaternion.AngleAxis(breathe, side) * Quaternion.AngleAxis(lean, up));
            }
            Pose("jaw", Quaternion.AngleAxis(JawOpen * 28f, side));

            // Legs tuck while flapping hard.
            float tuck = Mathf.Clamp01(amp * 1.5f) * 22f;
            Pose("leg_L0", Quaternion.AngleAxis(-tuck, side));
            Pose("leg_R0", Quaternion.AngleAxis(-tuck, side));
        }
    }
}
