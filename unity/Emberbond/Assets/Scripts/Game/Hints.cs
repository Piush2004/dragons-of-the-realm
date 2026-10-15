using System.Collections.Generic;
using UnityEngine;

namespace Emberbond
{
    /// <summary>
    /// Onboarding tips. Each tip appears once, at the moment it becomes useful, names the player's current bindings, and is
    /// remembered between sessions. Turn them off with Settings > Show tips.
    /// </summary>
    public sealed class HintSystem : MonoBehaviour
    {
        public Director Director;
        public static HintSystem Instance;

        /// <summary>Ids shown in this session (the self-test reads this).</summary>
        public readonly HashSet<string> Shown = new HashSet<string>();
        public string CurrentText = "";
        public string CurrentId = "";

        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Queue<KeyValuePair<string, string>> _queue = new Queue<KeyValuePair<string, string>>();
        private float _timer, _gap;
        private Texture2D _white;
        private GUIStyle _style;
        private const string PrefKey = "emberbond.hints.seen";
        private const float W = 1920f, H = 1080f;
        private float _flightTime;

        private void Awake()
        {
            Instance = this;
            string saved = PlayerPrefs.GetString(PrefKey, "");
            foreach (string id in saved.Split(',')) if (id.Length > 0) _seen.Add(id);
        }

        private void Start()
        {
            _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply();
        }

        private static string K(GameAction a) { return "[" + Bindings.Short(a) + "]"; }

        private void Offer(string id, string text)
        {
            if (_seen.Contains(id) || Shown.Contains(id)) return;
            foreach (KeyValuePair<string, string> q in _queue) if (q.Key == id) return;
            if (id == CurrentId) return;
            _queue.Enqueue(new KeyValuePair<string, string>(id, text));
        }

        private void Persist(string id)
        {
            if (InputState.Scripted) return;                      // test runs must not use up the player's tips
            _seen.Add(id);
            PlayerPrefs.SetString(PrefKey, string.Join(",", new List<string>(_seen).ToArray()));
            PlayerPrefs.Save();
        }

        public void ResetSeen()
        {
            _seen.Clear(); Shown.Clear();
            PlayerPrefs.DeleteKey(PrefKey);
        }

        private void Update()
        {
            Director d = Director;
            if (d == null || !d.Started || !GameSettings.ShowHints) { return; }
            float dt = Time.unscaledDeltaTime;
            Evaluate(d);

            if (_timer > 0f)
            {
                _timer -= dt;
                if (_timer <= 0f) { CurrentText = ""; CurrentId = ""; _gap = 1.5f; }
                return;
            }
            if (_gap > 0f) { _gap -= dt; return; }
            if (d.InDialogue && _queue.Count > 0 && _queue.Peek().Key != "dialogue") return;
            if (_queue.Count == 0) return;
            KeyValuePair<string, string> next = _queue.Dequeue();
            CurrentId = next.Key; CurrentText = next.Value;
            Shown.Add(next.Key);
            Persist(next.Key);
            _timer = Mathf.Clamp(3.5f + next.Value.Length * 0.045f, 4.5f, 9f);
        }

        private void Evaluate(Director d)
        {
            if (d.Player == null || d.Dragon == null) return;
            ThirdPersonPlayer p = d.Player;
            DragonFlight dr = d.Dragon;

            if (d.InDialogue)
            {
                Offer("dialogue", "Press 1, 2 or 3 to answer the council. Each tone changes who you are. Silence is an answer too.");
                return;
            }
            Offer("move", "Move with " + K(GameAction.Forward) + K(GameAction.Left) + K(GameAction.Back) + K(GameAction.Right) +
                  ". Hold " + K(GameAction.Sprint) + " to sprint; sprinting spends stamina.");

            if (d.Stage == 5 || d.Stage >= 10)
            {
                bool near = false;
                for (int i = 0; i < d.Soldiers.Count; i++)
                {
                    Soldier s = d.Soldiers[i];
                    if (s != null && !s.Health.Dead && (s.transform.position - p.transform.position).sqrMagnitude < 14f * 14f) { near = true; break; }
                }
                if (near)
                {
                    Offer("strike", K(GameAction.Attack) + " strikes with your sword. Tap " + K(GameAction.Block) + " just before a blow lands to parry and stagger the attacker.");
                }
            }
            if (p.Stamina < 18f && !p.Mounted) Offer("stamina", "Out of breath. Sprinting, striking and swimming spend stamina; stop and it returns.");
            if (!p.Mounted && p.transform.position.y + 0.9f < ThirdPersonPlayer.SeaLevel)
                Offer("water", "You are swimming. Hold " + K(GameAction.Jump) + " to climb out. Stamina drains in the water, and an empty bar means drowning.");
            if (p.Health.Health < p.Health.MaxHealth * 0.3f && !p.Health.Dead)
                Offer("lowhp", "Badly hurt. Parry instead of trading blows. If you fall you return to the last checkpoint at full health.");

            if (d.Stage == 1) Offer("mount", "Walk to your wyrm and press " + K(GameAction.Interact) + " to mount. " + K(GameAction.Summon) + " calls it to you.");
            if (dr.Mounted)
            {
                _flightTime += Time.unscaledDeltaTime;
                Offer("fly", "Move the mouse to steer. " + K(GameAction.Sprint) + " boosts, " + K(GameAction.Attack) + " breathes fire, " +
                      K(GameAction.Jump) + " climbs and " + K(GameAction.Crouch) + " dives.");
                if (dr.Crashes > 0) Offer("crash", "Hard impacts hurt your wyrm and shake the rider. Pull up early and fly around walls, not through them.");
                if (d.Enemy != null && d.Enemy.gameObject.activeInHierarchy) Offer("lock", "Fire near the rival wyrm and your fireballs lead the target. Stay moving; its fire is aimed too.");
                if (_flightTime > 25f) Offer("land", "To land, slow down close to the ground and press " + K(GameAction.Interact) + ".");
            }
            if (d.Stage >= 10) Offer("autosave", "The story autosaves at the start of every chapter. Esc opens the menu to save, change controls or return to the title.");
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(CurrentText) || !GameSettings.ShowHints) return;
            if (Director != null && Director.InDialogue && CurrentId != "dialogue") return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(0.97f, 0.94f, 0.85f) } };
            }
            float scale = Mathf.Min(Screen.width / W, Screen.height / H) * GameSettings.UiScale;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), Vector2.zero);
            float fade = Mathf.Clamp01(Mathf.Min(_timer, 0.4f) / 0.4f);
            float w = 900f, h = 76f, x = (W - w) * 0.5f, y = H - 215f;
            Color c = GUI.color;
            GUI.color = new Color(0.03f, 0.025f, 0.02f, 0.72f * fade);
            GUI.DrawTexture(new Rect(x, y, w, h), _white);
            GUI.color = new Color(0.85f, 0.7f, 0.38f, fade);
            GUI.DrawTexture(new Rect(x, y, 4f, h), _white);
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.Label(new Rect(x + 20f, y, w - 40f, h), CurrentText, _style);
            GUI.color = c;
        }
    }
}
