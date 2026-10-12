using System;
using System.Runtime.InteropServices;

namespace BlockRealm
{
    /// <summary>
    /// One vertex, 32 bytes, laid out so Unity can upload the array directly. The field order matches Unity's
    /// required attribute order within a stream: position, normal, colour, then texture coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshVertex
    {
        public float PX, PY, PZ;                // position (12 bytes)
        public sbyte NX, NY, NZ, NW;            // normal as signed normalised bytes (4)
        public byte Sky, Block, Ao, Flags;      // colour: sky light, block light, ambient occlusion, flags (4)
        public float U, V, Layer;               // texture-array coordinates (12)
    }

    /// <summary>Geometry for one section: a shared vertex array and three index ranges (opaque, cutout, water).</summary>
    public sealed class MeshBuffers
    {
        public MeshVertex[] Vertices;
        public int VertexCount;
        public ushort[] Indices;
        public int OpaqueIndexCount, CutoutIndexCount, WaterIndexCount;
        public int ColumnX, SectionY, ColumnZ;
        public bool IsEmpty { get { return Indices == null || Indices.Length == 0; } }
        public int TriangleCount { get { return Indices == null ? 0 : Indices.Length / 3; } }
    }

    /// <summary>Turns a padded section into triangles with hidden-face culling, ambient occlusion and smooth light.</summary>
    public static class SectionMesher
    {
        // Corner offsets (x,y,z in 0/1) of each face, wound so the cross product points along the outward normal.
        private static readonly int[,,] FaceCorners =
        {
            // East  +X
            { {1,0,0}, {1,1,0}, {1,1,1}, {1,0,1} },
            // West  -X
            { {0,0,1}, {0,1,1}, {0,1,0}, {0,0,0} },
            // Up    +Y
            { {0,1,0}, {0,1,1}, {1,1,1}, {1,1,0} },
            // Down  -Y
            { {0,0,1}, {0,0,0}, {1,0,0}, {1,0,1} },
            // South +Z
            { {1,0,1}, {1,1,1}, {0,1,1}, {0,0,1} },
            // North -Z
            { {0,0,0}, {0,1,0}, {1,1,0}, {1,0,0} },
        };
        private static readonly int[,] FaceNormal =
        {
            {1,0,0}, {-1,0,0}, {0,1,0}, {0,-1,0}, {0,0,1}, {0,0,-1},
        };
        private static readonly float[,] CornerUv = { { 0, 0 }, { 0, 1 }, { 1, 1 }, { 1, 0 } };

        [ThreadStatic] private static MeshVertex[] _verts;
        [ThreadStatic] private static ushort[] _opaque, _cutout, _water;

        private const int MaxVerts = 65000;

        /// <summary>The 4 corner offsets of face f, for tests.</summary>
        public static int[] Corner(int face, int corner)
        {
            return new[] { FaceCorners[face, corner, 0], FaceCorners[face, corner, 1], FaceCorners[face, corner, 2] };
        }

        public static MeshBuffers Mesh(PaddedSection p)
        {
            MeshBuffers result = new MeshBuffers();
            result.ColumnX = p.ColumnX; result.SectionY = p.SectionY; result.ColumnZ = p.ColumnZ;
            if (p.AllAir) return result;

            if (_verts == null)
            {
                _verts = new MeshVertex[MaxVerts + 64];
                _opaque = new ushort[MaxVerts * 3 / 2 + 64];
                _cutout = new ushort[MaxVerts * 3 / 2 + 64];
                _water = new ushort[MaxVerts * 3 / 2 + 64];
            }
            int nv = 0, no = 0, nc = 0, nw = 0;
            byte[] blocks = p.Blocks;
            byte[] light = p.Light;
            const int N = PaddedSection.N;

            for (int ly = 0; ly < 16; ly++)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int pi = ((ly + 1) * N + (lz + 1)) * N + (lx + 1);
                        byte id = blocks[pi];
                        if (id == 0) continue;
                        BlockInfo info = Blocks.All[id];
                        if (info == null) continue;
                        if (nv > MaxVerts - 32) goto done;

                        switch (info.Render)
                        {
                            case RenderKind.Cube:
                            case RenderKind.Cutout:
                                for (int f = 0; f < 6; f++)
                                {
                                    int nx = lx + 1 + FaceNormal[f, 0], ny = ly + 1 + FaceNormal[f, 1], nz = lz + 1 + FaceNormal[f, 2];
                                    int ni = (ny * N + nz) * N + nx;
                                    byte nid = blocks[ni];
                                    if (!FaceVisible(info, id, nid)) continue;
                                    if (info.Render == RenderKind.Cube && info.Opaque)
                                        EmitFace(p, lx, ly, lz, f, info.LayerFor((Face)f), ref nv, _opaque, ref no, 0f, false);
                                    else
                                        EmitFace(p, lx, ly, lz, f, info.LayerFor((Face)f), ref nv, _cutout, ref nc, 0f, false);
                                }
                                break;
                            case RenderKind.Liquid:
                                EmitLiquid(p, lx, ly, lz, id, info, ref nv, B.IsLava(id) ? _opaque : _water, ref no, ref nw, B.IsLava(id));
                                break;
                            case RenderKind.Cross:
                                EmitCross(p, lx, ly, lz, info.LayerSide, ref nv, _cutout, ref nc);
                                break;
                        }
                    }
                }
            }
        done:
            if (nv == 0) return result;
            result.VertexCount = nv;
            result.Vertices = new MeshVertex[nv];
            Array.Copy(_verts, result.Vertices, nv);
            result.OpaqueIndexCount = no; result.CutoutIndexCount = nc; result.WaterIndexCount = nw;
            result.Indices = new ushort[no + nc + nw];
            Array.Copy(_opaque, 0, result.Indices, 0, no);
            Array.Copy(_cutout, 0, result.Indices, no, nc);
            Array.Copy(_water, 0, result.Indices, no + nc, nw);
            return result;
        }

        private static bool FaceVisible(BlockInfo info, byte id, byte neighbour)
        {
            if (neighbour == 0) return true;
            BlockInfo n = Blocks.All[neighbour];
            if (n == null) return true;
            if (n.Opaque && n.Render == RenderKind.Cube) return false;
            if (neighbour == id && (info.Render == RenderKind.Cutout || !info.Opaque)) return false;
            return true;
        }

        private static int LightAt(PaddedSection p, int x, int y, int z, bool sky)
        {
            int v = p.Light[(y * PaddedSection.N + z) * PaddedSection.N + x];
            return sky ? v >> 4 : v & 15;
        }

        private static bool OpaqueAt(PaddedSection p, int x, int y, int z)
        {
            return Blocks.OpaqueTable[p.Blocks[(y * PaddedSection.N + z) * PaddedSection.N + x]];
        }

        private static void EmitFace(PaddedSection p, int lx, int ly, int lz, int face, int layer, ref int nv, ushort[] idx, ref int ni, float topDrop, bool unusedWater)
        {
            // Padded coordinates of the cell in front of the face.
            int fx = lx + 1 + FaceNormal[face, 0], fy = ly + 1 + FaceNormal[face, 1], fz = lz + 1 + FaceNormal[face, 2];
            int axis = face >> 1;            // 0 = x, 1 = y, 2 = z
            int baseV = nv;
            int l0s = LightAt(p, fx, fy, fz, true), l0b = LightAt(p, fx, fy, fz, false);
            float shadeBias = 0;

            for (int c = 0; c < 4; c++)
            {
                int cx = FaceCorners[face, c, 0], cy = FaceCorners[face, c, 1], cz = FaceCorners[face, c, 2];
                // Direction of this corner along the two tangent axes.
                int d0x = 0, d0y = 0, d0z = 0, d1x = 0, d1y = 0, d1z = 0;
                if (axis == 0) { d0y = cy == 1 ? 1 : -1; d1z = cz == 1 ? 1 : -1; }
                else if (axis == 1) { d0x = cx == 1 ? 1 : -1; d1z = cz == 1 ? 1 : -1; }
                else { d0x = cx == 1 ? 1 : -1; d1y = cy == 1 ? 1 : -1; }

                bool s1 = OpaqueAt(p, fx + d0x, fy + d0y, fz + d0z);
                bool s2 = OpaqueAt(p, fx + d1x, fy + d1y, fz + d1z);
                bool cn = OpaqueAt(p, fx + d0x + d1x, fy + d0y + d1y, fz + d0z + d1z);
                int ao = (s1 && s2) ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (cn ? 1 : 0));

                int skySum = l0s, blkSum = l0b;
                int skyS1 = s1 ? l0s : LightAt(p, fx + d0x, fy + d0y, fz + d0z, true);
                int blkS1 = s1 ? l0b : LightAt(p, fx + d0x, fy + d0y, fz + d0z, false);
                int skyS2 = s2 ? l0s : LightAt(p, fx + d1x, fy + d1y, fz + d1z, true);
                int blkS2 = s2 ? l0b : LightAt(p, fx + d1x, fy + d1y, fz + d1z, false);
                int skyC = (s1 && s2) || cn ? l0s : LightAt(p, fx + d0x + d1x, fy + d0y + d1y, fz + d0z + d1z, true);
                int blkC = (s1 && s2) || cn ? l0b : LightAt(p, fx + d0x + d1x, fy + d0y + d1y, fz + d0z + d1z, false);
                skySum += skyS1 + skyS2 + skyC;
                blkSum += blkS1 + blkS2 + blkC;

                MeshVertex v;
                v.PX = lx + cx; v.PY = ly + cy; v.PZ = lz + cz;
                v.NX = (sbyte)(FaceNormal[face, 0] * 127); v.NY = (sbyte)(FaceNormal[face, 1] * 127); v.NZ = (sbyte)(FaceNormal[face, 2] * 127); v.NW = 0;
                v.U = CornerUv[c, 0]; v.V = CornerUv[c, 1]; v.Layer = layer;
                v.Sky = (byte)(skySum * 255 / 60);
                v.Block = (byte)(blkSum * 255 / 60);
                v.Ao = (byte)(ao * 85);
                v.Flags = 0;
                _verts[nv++] = v;
            }
            // Flip the diagonal so ambient occlusion interpolates without a visible seam.
            int a0 = _verts[baseV].Ao + _verts[baseV + 2].Ao, a1 = _verts[baseV + 1].Ao + _verts[baseV + 3].Ao;
            if (a0 >= a1)
            {
                idx[ni++] = (ushort)baseV; idx[ni++] = (ushort)(baseV + 1); idx[ni++] = (ushort)(baseV + 2);
                idx[ni++] = (ushort)baseV; idx[ni++] = (ushort)(baseV + 2); idx[ni++] = (ushort)(baseV + 3);
            }
            else
            {
                idx[ni++] = (ushort)(baseV + 1); idx[ni++] = (ushort)(baseV + 2); idx[ni++] = (ushort)(baseV + 3);
                idx[ni++] = (ushort)(baseV + 1); idx[ni++] = (ushort)(baseV + 3); idx[ni++] = (ushort)baseV;
            }
            if (shadeBias != 0) { }
        }

        private static void EmitLiquid(PaddedSection p, int lx, int ly, int lz, byte id, BlockInfo info,
            ref int nv, ushort[] idx, ref int noOpaque, ref int nwWater, bool toOpaque)
        {
            const int N = PaddedSection.N;
            int px = lx + 1, py = ly + 1, pz = lz + 1;
            bool lava = B.IsLava(id);
            byte above = p.Blocks[((py + 1) * N + pz) * N + px];
            bool topExposed = lava ? !B.IsLava(above) : !B.IsWater(above);
            float topY = topExposed ? SurfaceHeight(id) : 1f;
            for (int f = 0; f < 6; f++)
            {
                int nx = px + FaceNormal[f, 0], ny = py + FaceNormal[f, 1], nz = pz + FaceNormal[f, 2];
                byte nid = p.Blocks[(ny * N + nz) * N + nx];
                bool sameFluid = lava ? B.IsLava(nid) : B.IsWater(nid);
                float lowFrom = 0f;
                if (sameFluid)
                {
                    // Between two cells of the same fluid only the part of this cell above its neighbour's surface shows.
                    if (f == (int)Face.Up || f == (int)Face.Down) continue;
                    byte nAbove = p.Blocks[((ny + 1) * N + nz) * N + nx];
                    float nTop = (lava ? B.IsLava(nAbove) : B.IsWater(nAbove)) ? 1f : SurfaceHeight(nid);
                    if (nTop >= topY - 0.001f) continue;
                    lowFrom = nTop;
                }
                BlockInfo n = Blocks.All[nid];
                if (!sameFluid && n != null && n.Opaque && n.Render == RenderKind.Cube) continue;
                if (f == (int)Face.Down && n != null && n.Solid && n.Opaque) continue;

                int baseV = nv;
                int layer = info.LayerSide;
                int l0s = LightAt(p, nx, ny, nz, true), l0b = LightAt(p, nx, ny, nz, false);
                if (f == (int)Face.Up) { l0s = Math.Max(l0s, LightAt(p, px, py, pz, true)); }
                for (int c = 0; c < 4; c++)
                {
                    MeshVertex v;
                    float cy = FaceCorners[f, c, 1];
                    v.PX = lx + FaceCorners[f, c, 0];
                    v.PY = ly + (cy > 0.5f ? topY : lowFrom);
                    v.PZ = lz + FaceCorners[f, c, 2];
                    v.NX = (sbyte)(FaceNormal[f, 0] * 127); v.NY = (sbyte)(FaceNormal[f, 1] * 127); v.NZ = (sbyte)(FaceNormal[f, 2] * 127); v.NW = 0;
                    v.U = CornerUv[c, 0]; v.V = CornerUv[c, 1]; v.Layer = layer;
                    v.Sky = (byte)(l0s * 255 / 15);
                    v.Block = (byte)(l0b * 255 / 15);
                    v.Ao = 255;
                    v.Flags = (byte)(f == (int)Face.Up ? 1 : 0);
                    _verts[nv++] = v;
                }
                if (toOpaque)
                {
                    idx[noOpaque++] = (ushort)baseV; idx[noOpaque++] = (ushort)(baseV + 1); idx[noOpaque++] = (ushort)(baseV + 2);
                    idx[noOpaque++] = (ushort)baseV; idx[noOpaque++] = (ushort)(baseV + 2); idx[noOpaque++] = (ushort)(baseV + 3);
                }
                else
                {
                    idx[nwWater++] = (ushort)baseV; idx[nwWater++] = (ushort)(baseV + 1); idx[nwWater++] = (ushort)(baseV + 2);
                    idx[nwWater++] = (ushort)baseV; idx[nwWater++] = (ushort)(baseV + 2); idx[nwWater++] = (ushort)(baseV + 3);
                }
            }
        }

        /// <summary>Height of a liquid's surface within its cell: sources sit a little below the top, flowing water by level.</summary>
        public static float SurfaceHeight(byte id)
        {
            if (id == B.Lava) return 0.875f;
            if (B.IsLava(id)) return 0.12f + 0.2f * B.LavaLevel(id);       // levels 1..3 -> 0.32 .. 0.72
            int level = B.WaterLevel(id);
            if (level >= 8) return 0.875f;
            return 0.12f + 0.095f * level;              // levels 1..7 -> 0.215 .. 0.785
        }

        private static void EmitCross(PaddedSection p, int lx, int ly, int lz, int layer, ref int nv, ushort[] idx, ref int ni)
        {
            int px = lx + 1, py = ly + 1, pz = lz + 1;
            int sky = LightAt(p, px, py, pz, true), blk = LightAt(p, px, py, pz, false);
            // Two diagonal quads, each visible from both sides (the cutout material renders with culling off).
            for (int q = 0; q < 2; q++)
            {
                int baseV = nv;
                float x0 = q == 0 ? 0.05f : 0.05f, x1 = q == 0 ? 0.95f : 0.95f;
                float z0 = q == 0 ? 0.05f : 0.95f, z1 = q == 0 ? 0.95f : 0.05f;
                for (int c = 0; c < 4; c++)
                {
                    MeshVertex v;
                    bool right = c >= 2;                // vertices 2,3 are at x1,z1
                    bool top = c == 1 || c == 2;
                    v.PX = lx + (right ? x1 : x0);
                    v.PY = ly + (top ? 1f : 0f);
                    v.PZ = lz + (right ? z1 : z0);
                    v.NX = 0; v.NY = 127; v.NZ = 0; v.NW = 0;
                    v.U = CornerUv[c, 0]; v.V = CornerUv[c, 1]; v.Layer = layer;
                    v.Sky = (byte)(sky * 255 / 15); v.Block = (byte)(blk * 255 / 15); v.Ao = 255; v.Flags = 2;
                    _verts[nv++] = v;
                }
                idx[ni++] = (ushort)baseV; idx[ni++] = (ushort)(baseV + 1); idx[ni++] = (ushort)(baseV + 2);
                idx[ni++] = (ushort)baseV; idx[ni++] = (ushort)(baseV + 2); idx[ni++] = (ushort)(baseV + 3);
            }
        }
    }
}
