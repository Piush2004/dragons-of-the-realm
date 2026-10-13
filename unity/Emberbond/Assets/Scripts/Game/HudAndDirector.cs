using System.Collections.Generic;
using UnityEngine;

namespace Emberbond
{
    public enum Tone { Diplomatic, Aggressive, Deceptive }

    [System.Serializable]
    public sealed class DialogueOption { public Tone Tone; public string Text; public System.Action<Director> Effect; }

    /// <summary>Reputation values that dialogue and deeds change and that later chapters read.</summary>
    public sealed class Reputation
    {
        public int Honour, Fear, Cunning;
    }

    /// <summary>The on-screen interface, styled after the reference frames: thin bars, compass, prompts.</summary>
    public sealed class Hud : MonoBehaviour
    {
        public Director Director;
        private Texture2D _white, _badge;
        private GUIStyle _label, _small, _title, _quest;
        private const float W = 1920f, H = 1080f;

        private void Start()
        {
            _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply();
            _badge = MakeBadge(160);
        }

        private static Texture2D MakeBadge(int s)
        {
            Texture2D t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c) / (s * 0.5f);
                    Color col = Color.clear;
                    if (d < 1f) col = new Color(0.07f, 0.06f, 0.06f, 1f);
                    if (d < 0.94f && d > 0.82f) col = new Color(0.62f, 0.5f, 0.28f, 1f);
                    if (d < 0.78f) col = new Color(0.55f, 0.07f, 0.06f, 1f);
                    // three-headed sigil: three diverging spikes
                    float dx = (x - c.x) / (s * 0.5f), dy = (y - c.y) / (s * 0.5f);
                    for (int k = -1; k <= 1; k++)
                    {
                        float a = k * 0.55f;
                        float rx = dx * Mathf.Cos(a) - dy * Mathf.Sin(a), ry = dx * Mathf.Sin(a) + dy * Mathf.Cos(a);
                        if (ry > -0.45f && ry < 0.55f && Mathf.Abs(rx) < 0.11f * (1.15f - (ry + 0.45f))) col = new Color(0.9f, 0.82f, 0.62f, 1f);
                    }
                    t.SetPixel(x, y, col);
                }
            t.Apply();
            return t;
        }

        private void Styles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 22, normal = { textColor = new Color(0.92f, 0.9f, 0.84f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 17, normal = { textColor = new Color(0.86f, 0.84f, 0.78f) } };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.95f, 0.92f, 0.85f) } };
            _quest = new GUIStyle(GUI.skin.label) { fontSize = 24, normal = { textColor = Color.white } };
        }

        private void Rect(float x, float y, float w, float h, Color c)
        {
            GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), _white); GUI.color = Color.white;
        }

        // Text with a soft dark drop shadow so it stays readable over bright sky, snow and sunlit grass.
        private static GUIStyle _shadowStyle;
        private static void Lbl(Rect r, string text, GUIStyle style)
        {
            if (_shadowStyle == null) _shadowStyle = new GUIStyle();
            _shadowStyle.font = style.font; _shadowStyle.fontSize = style.fontSize; _shadowStyle.fontStyle = style.fontStyle;
            _shadowStyle.alignment = style.alignment; _shadowStyle.wordWrap = style.wordWrap; _shadowStyle.clipping = style.clipping;
            _shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            Rect s = r; s.x += 1.5f; s.y += 1.5f;
            GUI.Label(s, text, _shadowStyle);
            GUI.Label(r, text, style);
        }

        private void Bar(float x, float y, float w, float h, float frac, Color fill, Color tip)
        {
            Rect(x - 2, y - 2, w + 4, h + 4, new Color(0.05f, 0.05f, 0.06f, 0.92f));
            Rect(x, y, w, h, new Color(0.18f, 0.18f, 0.2f, 0.9f));
            Rect(x, y, w * Mathf.Clamp01(frac), h, fill);
            Rect(x + w * Mathf.Clamp01(frac) - 6f, y, 6f, h, tip);
            Rect(x - 2, y - 2, w + 4, 1.5f, new Color(0.6f, 0.5f, 0.3f, 0.8f));
            Rect(x - 2, y + h + 0.5f, w + 4, 1.5f, new Color(0.6f, 0.5f, 0.3f, 0.8f));
        }

        private float _fps = 60f;

        private void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
        }

        private void OnGUI()
        {
            if (Director == null || !Director.Started) return;
            Styles();
            float scale = Mathf.Min(Screen.width / W, Screen.height / H) * GameSettings.UiScale;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), Vector2.zero);

            DrawCompass();
            DrawVitals();
            DrawQuest();
            DrawBoss();
            if (!Director.InDialogue) DrawPrompts();
            if (Director.InDialogue) DrawDialogue();
            if (!string.IsNullOrEmpty(Director.Toast))
            {
                Rect(W * 0.5f - 420f, 150f, 840f, 52f, new Color(0f, 0f, 0f, 0.55f));
                Lbl(new Rect(W * 0.5f - 410f, 160f, 820f, 40f), Director.Toast, _title);
            }
            if (GameSettings.ShowFps) Lbl(new Rect(W - 150f, 70f, 130f, 28f), Mathf.RoundToInt(_fps) + " fps", _small);
            if (Director.DeathProgress > 0f)
            {
                Rect(0f, 0f, W, H, new Color(0.3f, 0f, 0f, 0.6f * Director.DeathProgress));
                Lbl(new Rect(0f, H * 0.4f, W, 80f), "You fell", _title);
            }
        }

        private void DrawCompass()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float cx = W * 0.5f, y = 36f, half = 330f;
            Rect(cx - half, y + 10f, half * 2f, 2f, new Color(0.8f, 0.72f, 0.5f, 0.7f));
            float yaw = cam.transform.eulerAngles.y;
            string[] names = { "N", "E", "S", "W" };
            for (int i = 0; i < 4; i++)
            {
                float d = Mathf.DeltaAngle(yaw, i * 90f);
                if (Mathf.Abs(d) > 70f) continue;
                float px = cx + d / 70f * half;
                Lbl(new Rect(px - 12f, y - 18f, 28f, 28f), names[i], _label);
            }
            for (int t = -6; t <= 6; t++)
            {
                float d = Mathf.DeltaAngle(yaw, Mathf.Round(yaw / 15f) * 15f + t * 15f);
                if (Mathf.Abs(d) > 70f) continue;
                Rect(cx + d / 70f * half, y + 4f, 1.5f, 10f, new Color(0.8f, 0.72f, 0.5f, 0.6f));
            }
            if (Director.Objective.HasValue)
            {
                Vector3 to = Director.Objective.Value - cam.transform.position;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float d = Mathf.Clamp(Mathf.DeltaAngle(yaw, bearing), -70f, 70f);
                float px = cx + d / 70f * half;
                Rect(px - 9f, y - 2f, 18f, 18f, new Color(0.95f, 0.78f, 0.3f, 1f));
                Rect(px - 5f, y + 2f, 10f, 10f, new Color(0.1f, 0.08f, 0.05f, 1f));
                string dist = Mathf.RoundToInt(to.magnitude).ToString() + " m";
                Lbl(new Rect(px - 30f, y + 18f, 90f, 24f), dist, _small);
            }
        }

        private void DrawVitals()
        {
            if (Director.Player == null) return;
            float x = 40f, y = H - 150f;
            GUI.DrawTexture(new Rect(x, y, 110f, 110f), _badge);
            Damageable h = Director.Player.Health;
            Bar(x + 100f, y + 36f, 330f, 18f, h.Fraction, new Color(0.72f, 0.1f, 0.08f, 1f), new Color(0.95f, 0.5f, 0.1f, 1f));
            Bar(x + 100f, y + 66f, 280f, 12f, Director.Player.Stamina / Director.Player.MaxStamina, new Color(0.2f, 0.62f, 0.15f, 1f), new Color(0.65f, 0.9f, 0.4f, 1f));
            if (Director.Dragon != null && Director.Dragon.Mounted)
                Bar(x + 100f, y + 90f, 330f, 8f, Director.Dragon.Health.Fraction, new Color(0.85f, 0.55f, 0.1f, 1f), new Color(1f, 0.85f, 0.4f, 1f));
        }

        private void DrawQuest()
        {
            if (string.IsNullOrEmpty(Director.QuestTitle)) return;
            Rect(30f, 70f, 480f, 82f, new Color(0f, 0f, 0f, 0.35f));
            Rect(30f, 70f, 4f, 82f, new Color(0.85f, 0.7f, 0.35f, 1f));
            Lbl(new Rect(48f, 74f, 460f, 30f), "Quest Active:", _small);
            Lbl(new Rect(48f, 98f, 460f, 34f), Director.QuestTitle, _quest);
            Lbl(new Rect(48f, 126f, 460f, 26f), Director.QuestHint, _small);
        }

        private void DrawBoss()
        {
            if (Director.Enemy == null || !Director.Enemy.gameObject.activeInHierarchy || Director.Enemy.Health == null) return;
            if (Director.Enemy.Health.Dead) return;
            float w = 560f;
            Bar(W * 0.5f - w * 0.5f, 100f, w, 14f, Director.Enemy.Health.Fraction, new Color(0.72f, 0.1f, 0.08f, 1f), new Color(0.95f, 0.5f, 0.1f, 1f));
            Lbl(new Rect(W * 0.5f - w * 0.5f, 114f, w, 28f), Director.Enemy.Health.DisplayName, _small);
        }

        private void DrawPrompts()
        {
            List<string> lines = new List<string>();
            if (Director.Dragon != null)
            {
                if (!Director.Dragon.Mounted)
                {
                    if (Director.Dragon.RiderNear) lines.Add("[E]  Mount");
                    lines.Add("[G]  Summon Mount");
                    lines.Add("[LMB]  Strike     [RMB]  Parry");
                }
                else
                {
                    lines.Add("[LMB]  Breathe fire");
                    lines.Add("[Shift]  Boost");
                    lines.Add("[E]  Land (low and slow)");
                }
            }
            lines.Add("[Esc]  Menu");
            float y = H - 40f - lines.Count * 28f;
            Rect(W - 436f, y - 8f, 424f, lines.Count * 28f + 16f, new Color(0f, 0f, 0f, 0.3f));
            for (int i = 0; i < lines.Count; i++)
                Lbl(new Rect(W - 420f, y + i * 28f, 400f, 28f), lines[i], _small);
        }

        private void DrawDialogue()
        {
            Rect(0f, 0f, W, H, new Color(0f, 0f, 0f, 0.3f));
            float bw = 1100f, bx = W * 0.5f - bw * 0.5f, by = H - 400f;
            Rect(bx, by, bw, 150f, new Color(0.04f, 0.035f, 0.03f, 0.88f));
            Rect(bx, by, bw, 2f, new Color(0.8f, 0.68f, 0.4f, 1f));
            Lbl(new Rect(bx + 20f, by + 8f, bw - 40f, 30f), Director.Speaker, _quest);
            GUIStyle wrap = new GUIStyle(_small) { wordWrap = true, fontSize = 20 };
            Lbl(new Rect(bx + 20f, by + 42f, bw - 40f, 100f), Director.Line, wrap);

            Rect(bx, by + 152f, bw, 80f, new Color(0.04f, 0.035f, 0.03f, 0.78f));
            string[] tags = { "[Diplomatic]", "[Aggressive]", "[Deceptive]" };
            Color[] cols = { new Color(0.85f, 0.85f, 0.95f), new Color(0.95f, 0.35f, 0.3f), new Color(0.65f, 0.9f, 0.55f) };
            for (int i = 0; i < Director.Options.Count; i++)
            {
                DialogueOption o = Director.Options[i];
                GUIStyle tag = new GUIStyle(_small) { fontSize = 20, wordWrap = true };
                tag.normal.textColor = cols[(int)o.Tone];
                float ox = bx + i * (bw / 3f);
                Lbl(new Rect(ox + 10f, by + 160f, bw / 3f - 16f, 60f), (i + 1) + "  " + tags[(int)o.Tone] + " " + o.Text, tag);
            }
            Lbl(new Rect(bx + bw - 120f, by + 196f, 120f, 26f), Mathf.CeilToInt(Director.DialogueTimer) + " s", _small);
        }
    }
}
