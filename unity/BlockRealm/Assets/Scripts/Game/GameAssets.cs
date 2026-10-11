using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockRealm.Game
{
    /// <summary>
    /// Loads everything the Blender pipeline produced (tiles, icons, UI sprites, models) from Resources, builds the
    /// block texture array and the shared materials.
    /// </summary>
    public static class GameAssets
    {
        public const string Root = "BlockRealm/";

        public static bool Loaded { get; private set; }
        public static Texture2DArray TileArray { get; private set; }
        public static Material OpaqueMaterial, CutoutMaterial, WaterMaterial;
        public static Material[] ChunkMaterials;
        public static Material EntityMaterialTemplate;
        public static Material UnlitAlpha, UnlitAdditive;
        public static Font UiFont;
        public static Texture2D White;

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<int, Color> TileAverage = new Dictionary<int, Color>();
        private static Texture2D _missing;

        public static Shader LoadShader(string name)
        {
            Shader s = Resources.Load<Shader>(Root + "Shaders/" + name);
            if (s == null) s = Shader.Find("BlockRealm/" + name);
            if (s == null) s = Shader.Find("Unlit/Texture");
            return s;
        }

        public static Texture2D Load(string path)
        {
            Texture2D t;
            if (Cache.TryGetValue(path, out t)) return t;
            t = Resources.Load<Texture2D>(Root + path);
            if (t == null) t = Missing();
            else
            {
                t.filterMode = FilterMode.Point;
                t.wrapMode = TextureWrapMode.Clamp;
            }
            Cache[path] = t;
            return t;
        }

        public static Texture2D TryLoad(string path)
        {
            Texture2D t;
            if (Cache.TryGetValue(path, out t)) return t == _missing ? null : t;
            t = Resources.Load<Texture2D>(Root + path);
            if (t != null) { t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp; }
            Cache[path] = t == null ? _missing : t;
            return t;
        }

        private static Texture2D Missing()
        {
            if (_missing != null) return _missing;
            _missing = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            _missing.filterMode = FilterMode.Point;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    _missing.SetPixel(x, y, ((x / 4 + y / 4) & 1) == 0 ? new Color(1, 0, 1) : Color.black);
            _missing.Apply();
            return _missing;
        }

        /// <summary>Reads pixels from any texture, readable or not, by blitting through a render texture.</summary>
        public static Color32[] ReadPixels(Texture2D source)
        {
            if (source.isReadable)
            {
                try { return source.GetPixels32(); } catch (Exception) { }
            }
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = copy.GetPixels32();
            UnityEngine.Object.Destroy(copy);
            return px;
        }

        public static void Load()
        {
            if (Loaded) return;
            Loaded = true;
            White = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            White.SetPixel(0, 0, Color.white);
            White.Apply();

            try { UiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (Exception) { UiFont = Font.CreateDynamicFontFromOSFont("Arial", 16); }

            BuildTileArray();

            Shader block = LoadShader("Block");
            Shader water = LoadShader("Water");
            OpaqueMaterial = new Material(block) { name = "Blocks (opaque)" };
            OpaqueMaterial.SetTexture("_TileArray", TileArray);
            OpaqueMaterial.SetFloat("_Cutoff", 0.0f);
            OpaqueMaterial.SetFloat("_Cull", 2f);
            CutoutMaterial = new Material(block) { name = "Blocks (cutout)" };
            CutoutMaterial.SetTexture("_TileArray", TileArray);
            CutoutMaterial.SetFloat("_Cutoff", 0.5f);
            CutoutMaterial.SetFloat("_Cull", 0f);                       // plants and leaves are seen from both sides
            WaterMaterial = new Material(water) { name = "Water" };
            WaterMaterial.SetTexture("_TileArray", TileArray);
            WaterMaterial.SetFloat("_Alpha", 0.66f);
            ChunkMaterials = new[] { OpaqueMaterial, CutoutMaterial, WaterMaterial };

            Shader entity = LoadShader("Entity");
            EntityMaterialTemplate = new Material(entity) { name = "Entity" };

            Shader unlit = LoadShader("Unlit");
            UnlitAlpha = new Material(unlit) { name = "Unlit alpha" };
            UnlitAlpha.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            UnlitAlpha.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            UnlitAdditive = new Material(unlit) { name = "Unlit additive" };
            UnlitAdditive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            UnlitAdditive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        }

        private static void BuildTileArray()
        {
            int n = Blocks.TileNames.Count;
            TileArray = new Texture2DArray(16, 16, n, TextureFormat.RGBA32, true, false);
            TileArray.filterMode = FilterMode.Point;
            TileArray.wrapMode = TextureWrapMode.Clamp;
            TileArray.anisoLevel = 0;
            for (int i = 0; i < n; i++)
            {
                Texture2D t = Load("Tiles/" + Blocks.TileNames[i]);
                Color32[] px;
                if (t.width == 16 && t.height == 16) px = ReadPixels(t);
                else px = ReadPixels(_missing ?? Missing());
                TileArray.SetPixels32(px, i, 0);
                float r = 0, g = 0, b = 0, w = 0;
                for (int k = 0; k < px.Length; k++)
                {
                    if (px[k].a < 128) continue;
                    r += px[k].r; g += px[k].g; b += px[k].b; w += 1f;
                }
                TileAverage[i] = w > 0 ? new Color(r / w / 255f, g / w / 255f, b / w / 255f, 1f) : Color.gray;
            }
            TileArray.Apply(true, true);
        }

        /// <summary>Mean colour of a tile, used to tint break particles.</summary>
        public static Color AverageColor(int layer)
        {
            Color c;
            return TileAverage.TryGetValue(layer, out c) ? c : Color.gray;
        }

        public static Texture2D TileTexture(int layer)
        {
            if (layer < 0 || layer >= Blocks.TileNames.Count) return Missing();
            return Load("Tiles/" + Blocks.TileNames[layer]);
        }

        /// <summary>The picture of an item or block shown in the inventory and hotbar.</summary>
        public static Texture2D Icon(ushort id)
        {
            if (id == 0) return null;
            if (id < 256)
            {
                BlockInfo info = Blocks.All[id];
                if (info == null) return Missing();
                if (info.Render == RenderKind.Cube || info.Render == RenderKind.Cutout)
                {
                    Texture2D icon = TryLoad("BlockIcons/block_" + id);
                    if (icon != null) return icon;
                }
                return TileTexture(info.LayerSide);
            }
            ItemInfo item = Items.Get(id);
            return Load("Items/" + item.Icon);
        }

        public static Texture2D Ui(string name) { return Load("UI/" + name); }
    }
}
