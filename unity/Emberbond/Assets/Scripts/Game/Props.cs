using System.Collections.Generic;
using UnityEngine;

namespace Emberbond
{
    /// <summary>Crates and barrels that are real rigid bodies: they topple, slide, and fly from sword blows and fireballs.</summary>
    public static class Props
    {
        public static readonly List<Rigidbody> All = new List<Rigidbody>();
        private static PhysicsMaterial _wood;

        private static float Ground(Vector3 p)
        {
            Terrain t = Terrain.activeTerrain;
            if (t == null) return p.y;
            return t.SampleHeight(p) + t.transform.position.y;
        }

        private static Material Mat(Color c)
        {
            Material m = new Material(Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.25f);
            return m;
        }

        private static Rigidbody Make(PrimitiveType type, Vector3 pos, Vector3 scale, float mass, Material m, string name)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.position = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            Collider c = g.GetComponent<Collider>();
            c.sharedMaterial = _wood;
            Rigidbody rb = g.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.25f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            All.Add(rb);
            return rb;
        }

        /// <summary>A market-stall clutter: three piles of crates and a row of barrels.</summary>
        public static void SpawnMarket(Vector3 centre, Vector3 forward)
        {
            if (_wood == null) _wood = new PhysicsMaterial("wood") { dynamicFriction = 0.6f, staticFriction = 0.7f, bounciness = 0.08f };
            Material crate = Mat(new Color(0.42f, 0.28f, 0.14f));
            Material barrel = Mat(new Color(0.33f, 0.2f, 0.1f));
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int pile = 0; pile < 3; pile++)
            {
                Vector3 baseP = centre + right * (pile - 1) * 6.5f;
                int rows = 4 - pile;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < rows - r; c++)
                    {
                        Vector3 p = baseP + right * (c - (rows - r - 1) * 0.5f) * 1.0f;
                        p.y = Ground(p) + 0.46f + r * 0.92f;
                        Make(PrimitiveType.Cube, p, new Vector3(0.9f, 0.9f, 0.9f), 25f, crate, "Crate");
                    }
            }
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = centre + forward * 5f + right * (i - 3.5f) * 1.4f;
                p.y = Ground(p) + 0.55f;
                Make(PrimitiveType.Cylinder, p, new Vector3(0.8f, 0.55f, 0.8f), 30f, barrel, "Barrel");
            }
        }

        /// <summary>Total distance the props have moved from where the snapshot was taken.</summary>
        public static Vector3[] Snapshot()
        {
            Vector3[] s = new Vector3[All.Count];
            for (int i = 0; i < All.Count; i++) s[i] = All[i] != null ? All[i].position : Vector3.zero;
            return s;
        }

        public static int CountMoved(Vector3[] before, float threshold)
        {
            int n = 0;
            for (int i = 0; i < before.Length && i < All.Count; i++)
                if (All[i] != null && Vector3.Distance(All[i].position, before[i]) > threshold) n++;
            return n;
        }
    }
}
