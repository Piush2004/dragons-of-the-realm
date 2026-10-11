using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>
    /// Turns the client's block replica into meshes. Meshing runs on worker threads from copies of each section;
    /// finished geometry is uploaded on the main thread within a per-frame budget so streaming never stalls a frame.
    /// </summary>
    public sealed class WorldRenderer : MonoBehaviour
    {
        private struct SectionKey : IEquatable<SectionKey>
        {
            public int X, Y, Z;
            public SectionKey(int x, int y, int z) { X = x; Y = y; Z = z; }
            public bool Equals(SectionKey o) { return X == o.X && Y == o.Y && Z == o.Z; }
            public override bool Equals(object o) { return o is SectionKey && Equals((SectionKey)o); }
            public override int GetHashCode() { return (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791); }
        }

        private sealed class SectionView
        {
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 3),
        };

        private const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices |
                                                     MeshUpdateFlags.DontNotifyMeshUsers;

        public int MaxJobsInFlight = 6;
        public int MaxScheduledPerFrame = 4;
        public int MaxAppliedPerFrame = 6;
        public Transform Viewer;

        private GameClient _client;
        private World _world;
        private readonly Dictionary<SectionKey, SectionView> _views = new Dictionary<SectionKey, SectionView>();
        private readonly Dictionary<ColumnPos, List<SectionKey>> _byColumn = new Dictionary<ColumnPos, List<SectionKey>>();
        private readonly HashSet<SectionKey> _dirty = new HashSet<SectionKey>();
        private readonly List<SectionKey> _sorted = new List<SectionKey>();
        private readonly ConcurrentQueue<MeshBuffers> _finished = new ConcurrentQueue<MeshBuffers>();
        private int _inFlight;
        private Transform _container;

        public int ActiveSections { get { return _views.Count; } }
        public int PendingSections { get { return _dirty.Count + _inFlight; } }
        public long Triangles { get; private set; }

        private void Awake()
        {
            _container = new GameObject("World Meshes").transform;
            _container.SetParent(transform, false);
        }

        public void Bind(GameClient client)
        {
            _client = client;
            AttachWorld();
            client.ColumnUnloaded = OnColumnUnloaded;
        }

        private void AttachWorld()
        {
            _world = _client != null ? _client.World : null;
            if (_world != null) _world.SectionChanged = OnSectionChanged;
        }

        public void Clear()
        {
            foreach (KeyValuePair<SectionKey, SectionView> kv in _views) DestroyView(kv.Value);
            _views.Clear();
            _byColumn.Clear();
            _dirty.Clear();
            Triangles = 0;
            MeshBuffers dropped;
            while (_finished.TryDequeue(out dropped)) { }
        }

        private void OnSectionChanged(int cx, int sy, int cz)
        {
            if (sy < 0 || sy >= WorldConst.SectionsPerColumn) return;
            if (_world == null) return;
            Column c = _world.GetColumn(cx, cz);
            if (c == null || sy > c.TopSection + 1) return;
            _dirty.Add(new SectionKey(cx, sy, cz));
        }

        private void OnColumnUnloaded(int cx, int cz)
        {
            ColumnPos cp = new ColumnPos(cx, cz);
            List<SectionKey> list;
            if (_byColumn.TryGetValue(cp, out list))
            {
                foreach (SectionKey k in list)
                {
                    SectionView v;
                    if (_views.TryGetValue(k, out v)) { DestroyView(v); _views.Remove(k); }
                }
                _byColumn.Remove(cp);
            }
            for (int sy = 0; sy < WorldConst.SectionsPerColumn; sy++) _dirty.Remove(new SectionKey(cx, sy, cz));
        }

        private void Update()
        {
            if (_client == null) return;
            if (_client.World != _world) AttachWorld();
            if (_world == null) return;
            Schedule();
            Apply();
        }

        private void Schedule()
        {
            if (_dirty.Count == 0 || _inFlight >= MaxJobsInFlight) return;
            Vector3 v = Viewer != null ? Viewer.position : Vector3.zero;
            int vx = Mathf.FloorToInt(v.x / 16f), vy = Mathf.FloorToInt(v.y / 16f), vz = Mathf.FloorToInt(v.z / 16f);

            _sorted.Clear();
            foreach (SectionKey k in _dirty) _sorted.Add(k);
            _sorted.Sort((a, b) =>
            {
                int da = (a.X - vx) * (a.X - vx) + (a.Y - vy) * (a.Y - vy) * 2 + (a.Z - vz) * (a.Z - vz);
                int db = (b.X - vx) * (b.X - vx) + (b.Y - vy) * (b.Y - vy) * 2 + (b.Z - vz) * (b.Z - vz);
                return da.CompareTo(db);
            });

            int scheduled = 0;
            for (int i = 0; i < _sorted.Count && scheduled < MaxScheduledPerFrame && _inFlight < MaxJobsInFlight; i++)
            {
                SectionKey k = _sorted[i];
                Column col = _world.GetColumn(k.X, k.Z);
                if (col == null) { _dirty.Remove(k); continue; }
                // A section needs its four neighbours to have light and blocks; wait for them unless it is on the edge of the world.
                _dirty.Remove(k);
                PaddedSection padded = new PaddedSection();
                _world.CopyPadded(k.X, k.Y, k.Z, padded);
                _inFlight++;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    MeshBuffers result;
                    try { result = SectionMesher.Mesh(padded); }
                    catch (Exception) { result = new MeshBuffers { ColumnX = padded.ColumnX, SectionY = padded.SectionY, ColumnZ = padded.ColumnZ }; }
                    _finished.Enqueue(result);
                    Interlocked.Decrement(ref _inFlight);
                });
                scheduled++;
            }
        }

        private void Apply()
        {
            int applied = 0;
            MeshBuffers m;
            while (applied < MaxAppliedPerFrame && _finished.TryDequeue(out m))
            {
                applied++;
                SectionKey k = new SectionKey(m.ColumnX, m.SectionY, m.ColumnZ);
                if (!_world.HasColumn(k.X, k.Z)) continue;
                SectionView view;
                _views.TryGetValue(k, out view);
                if (m.IsEmpty)
                {
                    if (view != null) { RemoveView(k, view); }
                    continue;
                }
                if (view == null)
                {
                    view = CreateView(k);
                    _views[k] = view;
                    ColumnPos cp = new ColumnPos(k.X, k.Z);
                    List<SectionKey> list;
                    if (!_byColumn.TryGetValue(cp, out list)) { list = new List<SectionKey>(); _byColumn[cp] = list; }
                    list.Add(k);
                }
                Upload(view.Mesh, m);
                view.Renderer.enabled = true;
            }
        }

        private SectionView CreateView(SectionKey k)
        {
            SectionView v = new SectionView();
            v.Go = new GameObject("S " + k.X + "," + k.Y + "," + k.Z);
            v.Go.transform.SetParent(_container, false);
            v.Go.transform.position = new Vector3(k.X * 16, k.Y * 16, k.Z * 16);
            v.Mesh = new Mesh { name = "Section" };
            v.Mesh.MarkDynamic();
            v.Filter = v.Go.AddComponent<MeshFilter>();
            v.Filter.sharedMesh = v.Mesh;
            v.Renderer = v.Go.AddComponent<MeshRenderer>();
            v.Renderer.sharedMaterials = GameAssets.ChunkMaterials;
            v.Renderer.shadowCastingMode = ShadowCastingMode.On;
            v.Renderer.receiveShadows = true;
            v.Renderer.lightProbeUsage = LightProbeUsage.Off;
            v.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return v;
        }

        private void Upload(Mesh mesh, MeshBuffers b)
        {
            mesh.Clear();
            mesh.SetVertexBufferParams(b.VertexCount, Layout);
            mesh.SetVertexBufferData(b.Vertices, 0, 0, b.VertexCount, 0, UploadFlags);
            mesh.SetIndexBufferParams(b.Indices.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(b.Indices, 0, 0, b.Indices.Length, UploadFlags);
            mesh.subMeshCount = 3;
            int o = b.OpaqueIndexCount, c = b.CutoutIndexCount, w = b.WaterIndexCount;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, o, MeshTopology.Triangles), UploadFlags);
            mesh.SetSubMesh(1, new SubMeshDescriptor(o, c, MeshTopology.Triangles), UploadFlags);
            mesh.SetSubMesh(2, new SubMeshDescriptor(o + c, w, MeshTopology.Triangles), UploadFlags);
            mesh.bounds = new Bounds(new Vector3(8f, 8f, 8f), new Vector3(16.5f, 16.5f, 16.5f));
            Triangles += b.TriangleCount;
        }

        private void RemoveView(SectionKey k, SectionView v)
        {
            DestroyView(v);
            _views.Remove(k);
            List<SectionKey> list;
            if (_byColumn.TryGetValue(new ColumnPos(k.X, k.Z), out list)) list.Remove(k);
        }

        private static void DestroyView(SectionView v)
        {
            if (v.Mesh != null) Destroy(v.Mesh);
            if (v.Go != null) Destroy(v.Go);
        }

        private void OnDestroy()
        {
            if (_world != null && _world.SectionChanged == (Action<int, int, int>)OnSectionChanged) _world.SectionChanged = null;
        }
    }
}
