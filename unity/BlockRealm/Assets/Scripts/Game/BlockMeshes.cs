using UnityEngine;

namespace BlockRealm.Game
{
    /// <summary>Small meshes built from block tiles: held blocks, dropped items, selection boxes and crack overlays.</summary>
    public static class BlockMeshes
    {
        private static readonly Vector3[][] Faces =
        {
            new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },   // +X
            new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },   // -X
            new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) },   // +Y
            new[] { new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1) },   // -Y
            new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },   // +Z
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) },   // -Z
        };
        private static readonly Vector3[] Normals =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };
        private static readonly Vector2[] CornerUv = { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };

        /// <summary>
        /// A unit cube (centred on the origin, scaled) using the block's tiles, for the Block shader. Vertex colours are set
        /// to full light so held and dropped blocks stay readable in the dark.
        /// </summary>
        public static Mesh Cube(byte blockId, float size)
        {
            BlockInfo info = Blocks.Get(blockId);
            Mesh mesh = new Mesh { name = "Block " + info.Name };
            Vector3[] verts = new Vector3[24];
            Vector3[] norms = new Vector3[24];
            Vector3[] uvs = new Vector3[24];
            Color32[] cols = new Color32[24];
            int[] tris = new int[36];
            for (int f = 0; f < 6; f++)
            {
                float shade = f == 2 ? 255f : (f == 3 ? 120f : (f < 2 ? 190f : 160f));
                Color32 c = new Color32(255, 255, (byte)shade, 0);
                for (int k = 0; k < 4; k++)
                {
                    int i = f * 4 + k;
                    verts[i] = (Faces[f][k] - new Vector3(0.5f, 0.5f, 0.5f)) * size;
                    norms[i] = Normals[f];
                    uvs[i] = new Vector3(CornerUv[k].x, CornerUv[k].y, info.LayerFor((Face)f));
                    cols[i] = c;
                }
                int t = f * 6;
                tris[t] = f * 4; tris[t + 1] = f * 4 + 1; tris[t + 2] = f * 4 + 2;
                tris[t + 3] = f * 4; tris[t + 4] = f * 4 + 2; tris[t + 5] = f * 4 + 3;
            }
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.SetUVs(0, uvs);
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat quad (facing +Z, centred) for item icons.</summary>
        public static Mesh Quad(float size)
        {
            Mesh m = new Mesh { name = "Quad" };
            float h = size * 0.5f;
            m.vertices = new[] { new Vector3(-h, -h, 0), new Vector3(-h, h, 0), new Vector3(h, h, 0), new Vector3(h, -h, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The twelve edges of a block, for the selection outline (MeshTopology.Lines).</summary>
        public static Mesh Wireframe(float inflate)
        {
            Mesh m = new Mesh { name = "Block outline" };
            float a = -inflate, b = 1f + inflate;
            Vector3[] v =
            {
                new Vector3(a, a, a), new Vector3(b, a, a), new Vector3(b, a, b), new Vector3(a, a, b),
                new Vector3(a, b, a), new Vector3(b, b, a), new Vector3(b, b, b), new Vector3(a, b, b),
            };
            int[] idx = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
            m.vertices = v;
            m.SetIndices(idx, MeshTopology.Lines, 0);
            m.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1.1f, 1.1f, 1.1f));
            return m;
        }

        /// <summary>A slightly enlarged cube with standard UVs, textured with a crack stage.</summary>
        public static Mesh CrackCube(float inflate)
        {
            Mesh mesh = new Mesh { name = "Crack cube" };
            Vector3[] verts = new Vector3[24];
            Vector2[] uvs = new Vector2[24];
            int[] tris = new int[36];
            for (int f = 0; f < 6; f++)
            {
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = Faces[f][k];
                    p = new Vector3(p.x < 0.5f ? -inflate : 1f + inflate, p.y < 0.5f ? -inflate : 1f + inflate, p.z < 0.5f ? -inflate : 1f + inflate);
                    verts[f * 4 + k] = p;
                    uvs[f * 4 + k] = CornerUv[k];
                }
                int t = f * 6;
                tris[t] = f * 4; tris[t + 1] = f * 4 + 1; tris[t + 2] = f * 4 + 2;
                tris[t + 3] = f * 4; tris[t + 4] = f * 4 + 2; tris[t + 5] = f * 4 + 3;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
