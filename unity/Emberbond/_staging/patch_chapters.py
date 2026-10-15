import io
base = r'D:\Projects\Games\claude\unity\Emberbond\Assets\Scripts\Game\\'

def rd(n): return io.open(base + n, encoding='utf-8').read()
def wr(n, s): io.open(base + n, 'w', encoding='utf-8', newline='').write(s)
def rep(s, old, new, count=1):
    if old not in s: raise SystemExit('missing: ' + old[:90])
    return s.replace(old, new, count)

# ---------------------------------------------------------------- Director
s = rd('Director.cs')
s = rep(s, "public sealed class Director : MonoBehaviour", "public sealed partial class Director : MonoBehaviour")
s = rep(s, "    /// Stages: 5 market fight, 1 mount, 2 fly to the volcano, 3 rival wyrm, 4 chapter complete (free roam).\n",
           "    /// Stages: 5 market fight, 1 mount, 2 fly to the volcano, 3 rival wyrm, 4 chapter complete, then the later chapters\n"
           "    /// 10 pit, 11 charge, 12 duel, 13 riot, 14 pale tide, 15 last egg, 16 epilogue (see DirectorChapters.cs).\n")
s = rep(s, "            Deaths = 0; Respawns = 0; PlaySeconds = 0f;\n            AudioDirector.StartAmbience();\n            StartCouncil();",
           "            Deaths = 0; Respawns = 0; PlaySeconds = 0f;\n            PitChoice = DuelChoice = RiotChoice = TideChoice = 0; TamsinSaved = false; Ending = 0; EndingShown = false;\n"
           "            AudioDirector.StartAmbience();\n            StartCouncil();")
s = rep(s, "            Deaths = d.Deaths; PlaySeconds = d.PlaySeconds;\n",
           "            Deaths = d.Deaths; PlaySeconds = d.PlaySeconds;\n"
           "            PitChoice = d.PitChoice; DuelChoice = d.DuelChoice; RiotChoice = d.RiotChoice; TideChoice = d.TideChoice;\n"
           "            TamsinSaved = d.TamsinSaved == 1; Ending = d.Ending;\n")
s = rep(s, "                Deaths = Deaths,\n                PlaySeconds = PlaySeconds,\n",
           "                Deaths = Deaths,\n                PlaySeconds = PlaySeconds,\n"
           "                PitChoice = PitChoice, DuelChoice = DuelChoice, RiotChoice = RiotChoice, TideChoice = TideChoice,\n"
           "                TamsinSaved = TamsinSaved ? 1 : 0, Ending = Ending,\n")
# choose option with gates and a continuation
s = rep(s, '''        public void ChooseOption(int i)
        {
            if (!InDialogue || i < 0 || i >= Options.Count) return;
            if (Options[i].Effect != null) Options[i].Effect(this);
            InDialogue = false;
            ExitHall();
            AudioDirector.Ui();
            BeginQuest(5);
        }''', '''        /// <summary>The option picked when the timer runs out: the cold stare (second), else the first one that is open.</summary>
        private int DefaultChoice()
        {
            if (Options.Count > 1 && IsOpen(Options[1])) return 1;
            for (int i = 0; i < Options.Count; i++) if (IsOpen(Options[i])) return i;
            return Options.Count - 1;
        }

        private bool IsOpen(DialogueOption o) { return o.Gate == null || o.Gate(this); }

        public void ChooseOption(int i)
        {
            if (!InDialogue || i < 0 || i >= Options.Count) return;
            if (!IsOpen(Options[i]))
            {
                if (Toast != "This path is closed to you." || _toastTimer < 0.2f) Say("This path is closed to you.", 2f);
                return;
            }
            if (Options[i].Effect != null) Options[i].Effect(this);
            InDialogue = false;
            ExitHall();
            AudioDirector.Ui();
            Action<int> after = _afterDialogue;
            _afterDialogue = null;
            if (after != null) after(i); else BeginQuest(5);
        }''')
s = rep(s, "using System.Collections.Generic;\nusing UnityEngine;", "using System;\nusing System.Collections.Generic;\nusing UnityEngine;")
# begin quest: leave chapter scenery, and the chapters themselves
s = rep(s, '''            _stage = stage; _stageTime = 0f;
            switch (stage)
            {''', '''            _stage = stage; _stageTime = 0f;
            if (!IsChapterStage(stage) && (PitActive || _chapterObjects.Count > 0 || _grade != null || Dragon.GroundOverride.HasValue))
            {
                LeavePit();
                for (int i = 0; i < _chapterObjects.Count; i++) if (_chapterObjects[i] != null) Destroy(_chapterObjects[i]);
                _chapterObjects.Clear();
                _gradeTarget = 0f;
                FadeSnow(false);
            }
            switch (stage)
            {''')
s = rep(s, '''                    Say("Chapter 2 complete: First Flight", 8f);
                    AudioDirector.Play(Sfx.Fanfare, Vector3.zero, 0.8f);
                    break;
            }''', '''                    Say("Chapter 2 complete: First Flight", 8f);
                    AudioDirector.Play(Sfx.Fanfare, Vector3.zero, 0.8f);
                    break;
                default:
                    if (IsChapterStage(stage)) BeginChapter(stage);
                    break;
            }''')
s = rep(s, '''            if (_stage == 5) SpawnSoldiers();
            if (Rig != null) { Rig.Flight = false; Rig.Frozen = false; }
            Say(message, 3f);''', '''            if (_stage == 5) SpawnSoldiers();
            if (IsChapterStage(_stage) && _stage != StageEnd) BeginQuest(_stage, false);       // retry the encounter from its start
            if (Rig != null) { Rig.Flight = false; Rig.Frozen = false; }
            Say(message, 3f);''')
s = rep(s, '''            bool dead = Player.Health.Dead || (Dragon.Mounted && Dragon.Health.Dead);
            if (!dead) { _deathTimer = 0f; return; }''', '''            if (Dragon.Health.Dead && !Dragon.Mounted) Dragon.Health.Health = Dragon.Health.MaxHealth;     // a grounded wyrm is simply knocked out
            bool dead = Player.Health.Dead || (Dragon.Mounted && Dragon.Health.Dead);
            if (!dead) { _deathTimer = 0f; return; }''')
s = rep(s, '''            InputState.Poll();
            if (_toastTimer > 0f)''', '''            InputState.Poll();
            InputState.Cinematic = Started && InDialogue;
            if (_toastTimer > 0f)''')
s = rep(s, "else if (DialogueTimer <= 0f) ChooseOption(1);          // silence reads as a cold stare",
           "else if (DialogueTimer <= 0f) ChooseOption(DefaultChoice());          // silence reads as a cold stare")
s = rep(s, '''                case 3:
                    if (Enemy != null && Enemy.Health != null && Enemy.Health.Dead) BeginQuest(4);
                    break;
            }''', '''                case 3:
                    if (Enemy != null && Enemy.Health != null && Enemy.Health.Dead) BeginQuest(4);
                    break;
                case 4:
                    if (_stageTime > 7f) BeginQuest(StagePit);
                    break;
                default:
                    if (IsChapterStage(_stage)) UpdateChapter(dt);
                    break;
            }''')
s = rep(s, 'QuestTitle = "Chapter complete"; QuestHint = "The old wyrm falls into the sea. Explore freely.";',
           'QuestTitle = "Chapter complete"; QuestHint = "The old wyrm falls into the sea. The Pit awaits.";')
wr('Director.cs', s)

# ---------------------------------------------------------------- Hud: gates, lock marker, ending card
s = rd('HudAndDirector.cs')
s = rep(s, "    public sealed class DialogueOption { public Tone Tone; public string Text; public System.Action<Director> Effect; }",
           "    public sealed class DialogueOption\n    {\n        public Tone Tone; public string Text; public System.Action<Director> Effect;\n"
           "        public System.Func<Director, bool> Gate; public string GateText;      // a gated option is shown but cannot be taken until the gate opens\n    }")
s = rep(s, "        private GUIStyle _label, _small, _title, _quest;", "        private GUIStyle _label, _small, _title, _quest, _big, _wrapBig;")
s = rep(s, "            _quest = new GUIStyle(GUI.skin.label) { fontSize = 24, normal = { textColor = Color.white } };",
           "            _quest = new GUIStyle(GUI.skin.label) { fontSize = 24, normal = { textColor = Color.white } };\n"
           "            _big = new GUIStyle(GUI.skin.label) { fontSize = 60, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.98f, 0.86f, 0.55f) } };\n"
           "            _wrapBig = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = new Color(0.95f, 0.93f, 0.88f) } };")
s = rep(s, "            DrawBoss();\n", "            DrawBoss();\n            DrawLock(scale);\n")
s = rep(s, "            if (GameSettings.ShowFps) Lbl(", "            DrawEnding();\n            if (GameSettings.ShowFps) Lbl(")
s = rep(s, '''                DialogueOption o = Director.Options[i];
                GUIStyle tag = new GUIStyle(_small) { fontSize = 20, wordWrap = true };
                tag.normal.textColor = cols[(int)o.Tone];
                float ox = bx + i * (bw / 3f);
                Lbl(new Rect(ox + 10f, by + 160f, bw / 3f - 16f, 60f), (i + 1) + "  " + tags[(int)o.Tone] + " " + o.Text, tag);''',
'''                DialogueOption o = Director.Options[i];
                bool locked = o.Gate != null && !o.Gate(Director);
                GUIStyle tag = new GUIStyle(_small) { fontSize = 20, wordWrap = true };
                tag.normal.textColor = locked ? new Color(0.5f, 0.5f, 0.5f) : cols[(int)o.Tone];
                float ox = bx + i * (bw / 3f);
                string label = (i + 1) + "  " + tags[(int)o.Tone] + " " + o.Text + (locked ? "  (" + o.GateText + ")" : "");
                Lbl(new Rect(ox + 10f, by + 158f, bw / 3f - 16f, 90f), label, tag);''')
s = rep(s, "Rect(bx, by + 152f, bw, 80f, new Color(0.04f, 0.035f, 0.03f, 0.78f));", "Rect(bx, by + 152f, bw, 100f, new Color(0.04f, 0.035f, 0.03f, 0.78f));")
s = rep(s, "Lbl(new Rect(bx + bw - 120f, by + 196f, 120f, 26f),", "Lbl(new Rect(bx + bw - 120f, by + 226f, 120f, 26f),")
s = rep(s, '''        private void DrawBoss()''', '''        /// <summary>Brackets the rival wyrm while you are in the air, so the duel can be read at a glance.</summary>
        private void DrawLock(float scale)
        {
            if (Director.Dragon == null || !Director.Dragon.Mounted) return;
            EnemyDragon en = Director.Enemy;
            if (en == null || !en.gameObject.activeInHierarchy || en.Health == null || en.Health.Dead) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(en.transform.position);
            if (sp.z <= 0f) return;
            float gx = Mathf.Clamp(sp.x / scale, 50f, W - 50f), gy = Mathf.Clamp((Screen.height - sp.y) / scale, 50f, H - 50f);
            float dist = Vector3.Distance(cam.transform.position, en.transform.position);
            float s = Mathf.Lerp(46f, 20f, Mathf.Clamp01(dist / 500f));
            Color c = new Color(1f, 0.78f, 0.3f, 0.95f);
            float t = 3f, l = s * 0.55f;
            Rect(gx - s, gy - s, l, t, c); Rect(gx - s, gy - s, t, l, c);
            Rect(gx + s - l, gy - s, l, t, c); Rect(gx + s - t, gy - s, t, l, c);
            Rect(gx - s, gy + s - t, l, t, c); Rect(gx - s, gy + s - l, t, l, c);
            Rect(gx + s - l, gy + s - t, l, t, c); Rect(gx + s - t, gy + s - l, t, l, c);
            Lbl(new Rect(gx - 40f, gy + s + 4f, 100f, 24f), Mathf.RoundToInt(dist) + " m", _small);
        }

        private void DrawEnding()
        {
            if (!Director.EndingShown) return;
            Rect(0f, H * 0.5f, W, H * 0.5f, new Color(0f, 0f, 0f, 0.74f));
            Lbl(new Rect(0f, H * 0.5f + 30f, W, 90f), Director.EndingTitle, _big);
            Lbl(new Rect(W * 0.5f - 600f, H * 0.5f + 130f, 1200f, 150f), Director.EndingText, _wrapBig);
            Lbl(new Rect(0f, H * 0.5f + 300f, W, 34f), Director.EndingStats, _label);
            Lbl(new Rect(0f, H - 70f, W, 34f), "Enter: keep exploring       Esc: menu", _small);
        }

        private void DrawBoss()''')
wr('HudAndDirector.cs', s)

# ---------------------------------------------------------------- Soldier
s = rd('Soldier.cs')
s = rep(s, "        public int Parried, Landed;\n", "        public int Parried, Landed;\n        public float WalkSpeed = 3.1f, Damage = 12f;\n        public float Aggro;                 // 0 = always hunts; otherwise only within this many metres and on the same level\n")
s = rep(s, '''        public static Soldier Spawn(Vector3 pos, ThirdPersonPlayer target, int index)
        {
            Color cloth = new Color(0.55f, 0.1f, 0.08f);
            Color cloak = new Color(0.45f, 0.35f, 0.1f);
            GameObject go = CharacterFactory.Spawn(pos, 0f, cloth, cloak, new Color(0.15f, 0.1f, 0.07f), "Soldier" + index);''',
'''        public static Soldier Spawn(Vector3 pos, ThirdPersonPlayer target, int index)
        {
            return Spawn(pos, target, index, new Color(0.55f, 0.1f, 0.08f), new Color(0.45f, 0.35f, 0.1f), "Soldier");
        }

        public static Soldier Spawn(Vector3 pos, ThirdPersonPlayer target, int index, Color cloth, Color cloak, string name)
        {
            GameObject go = CharacterFactory.Spawn(pos, 0f, cloth, cloak, new Color(0.15f, 0.1f, 0.07f), name + index);''')
s = rep(s, '''            s.Health.DisplayName = "Soldier";''', '''            s.Health.DisplayName = name;''')
s = rep(s, '''            _cooldown -= dt;
            if (_winding)''', '''            bool aware = Aggro <= 0f || (dist < Aggro && Mathf.Abs(Target.transform.position.y - transform.position.y) < 4.5f);
            if (!aware && !_winding)
            {
                if (Anim != null) { Anim.Speed = 0f; Anim.Strike = 0f; }
                return;
            }
            _cooldown -= dt;
            if (_winding)''')
s = rep(s, "Target.Health.Hurt(12f, gameObject);", "Target.Health.Hurt(Damage, gameObject);")
s = rep(s, "_cc.SimpleMove(to.normalized * 3.1f);", "_cc.SimpleMove(to.normalized * WalkSpeed);")
wr('Soldier.cs', s)

# ---------------------------------------------------------------- Dragons
s = rd('Dragons.cs')
s = rep(s, "        public float LastImpact;\n", "        public float LastImpact;\n        public float? GroundOverride;        // a perch or floor that is not the terrain (the Pit)\n")
s = rep(s, '''        private float GroundHeight(Vector3 p)
        {
''', '''        private float GroundHeight(Vector3 p)
        {
            if (GroundOverride.HasValue) return GroundOverride.Value;
''')
s = rep(s, '''                Vector3 mouth = transform.position + fwd * 5.5f + Vector3.up * 0.3f;
                Fireball.Spawn(mouth, fwd * 120f + fwd * Speed, gameObject, 40f);''', '''                Vector3 mouth = transform.position + fwd * 5.5f + Vector3.up * 0.3f;
                float shotSpeed = 120f + Speed;
                Vector3 shot = fwd;
                EnemyDragon en = Director.Instance != null ? Director.Instance.Enemy : null;
                if (en != null && en.gameObject.activeInHierarchy && en.Health != null && !en.Health.Dead)
                {
                    // Aim assist: inside a cone, lead the target so the duel is about positioning and not pixel hunting.
                    Vector3 to = en.transform.position - mouth;
                    float dist = to.magnitude;
                    if (dist > 10f && dist < 700f && Vector3.Angle(fwd, to) < 16f)
                        shot = ((en.transform.position + en.Velocity * (dist / shotSpeed)) - mouth).normalized;
                }
                Fireball.Spawn(mouth, shot * shotSpeed, gameObject, 40f);''')
# EnemyDragon
s = rep(s, '''        private bool _inited;

        private void Vanish()''', '''        private bool _inited;
        private float _orbitR = 170f, _speed = 40f, _fireMin = 3.5f, _fireMax = 6f, _damage = 22f;
        private Color _fireTint;
        private Vector3 _baseScale;
        private bool _scaleSet;
        private readonly System.Collections.Generic.List<KeyValuePair<Material, Color>> _origColors = new System.Collections.Generic.List<KeyValuePair<Material, Color>>();

        public Vector3 Velocity { get { return _vel; } }

        /// <summary>Makes this a different rival: its name, toughness, size, speed, fire rate and colour.</summary>
        public void Configure(float hp, string name, float scale, float orbit, float speed, float fireMin, float fireMax, float damage, Color tint, Color fireTint)
        {
            if (!_scaleSet) { _baseScale = transform.localScale; _scaleSet = true; }
            transform.localScale = _baseScale * scale;
            Health.MaxHealth = Health.Health = hp;
            Health.DisplayName = name;
            _orbitR = orbit; _speed = speed; _fireMin = fireMin; _fireMax = fireMax; _damage = damage; _fireTint = fireTint;
            ApplyTint(tint);
        }

        private void ApplyTint(Color tint)
        {
            if (_origColors.Count == 0)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>())
                    foreach (Material m in r.materials)
                        if (m.HasProperty("_BaseColor")) _origColors.Add(new KeyValuePair<Material, Color>(m, m.GetColor("_BaseColor")));
            }
            foreach (KeyValuePair<Material, Color> kv in _origColors)
                if (kv.Key != null) kv.Key.SetColor("_BaseColor", new Color(kv.Value.r * tint.r, kv.Value.g * tint.g, kv.Value.b * tint.b, kv.Value.a));
        }

        private void Vanish()''')
s = rep(s, '''            Health.MaxHealth = Health.Health = 600f;
            Health.DisplayName = "The Old Wyrm";
            Health.IsBoss = true;''', '''            Health.MaxHealth = Health.Health = 600f;
            Health.DisplayName = "The Old Wyrm";
            Health.IsBoss = true;
            _orbitR = 170f; _speed = 40f; _fireMin = 3.5f; _fireMax = 6f; _damage = 22f; _fireTint = default(Color);
            if (_scaleSet) { transform.localScale = _baseScale; ApplyTint(Color.white); }
            CancelInvoke("Vanish");''')
s = rep(s, "new Vector3(Mathf.Cos(_orbit) * 170f, 45f + Mathf.Sin(_orbit * 1.7f) * 20f, Mathf.Sin(_orbit) * 170f);",
           "new Vector3(Mathf.Cos(_orbit) * _orbitR, 45f + Mathf.Sin(_orbit * 1.7f) * 20f, Mathf.Sin(_orbit) * _orbitR);")
s = rep(s, "Vector3 desired = to.normalized * Mathf.Min(40f, to.magnitude);", "Vector3 desired = to.normalized * Mathf.Min(_speed, to.magnitude);")
s = rep(s, '''                _fireTimer = Random.Range(3.5f, 6f);
                Vector3 mouth = transform.position + transform.forward * 5f;
                Vector3 aim = (Target.position - mouth).normalized;
                Fireball.Spawn(mouth, aim * 95f, gameObject, 22f);''', '''                _fireTimer = Random.Range(_fireMin, _fireMax);
                Vector3 mouth = transform.position + transform.forward * 5f;
                Vector3 aim = (Target.position - mouth).normalized;
                Fireball fb = Fireball.Spawn(mouth, aim * 95f, gameObject, _damage);
                if (_fireTint.a > 0f) fb.Tint = _fireTint;''')
s = rep(s, "using UnityEngine;\n\nnamespace Emberbond", "using System.Collections.Generic;\nusing UnityEngine;\n\nnamespace Emberbond")
wr('Dragons.cs', s)

# ---------------------------------------------------------------- Fireball tint
s = rd('Fireball.cs')
s = rep(s, "        public float Life = 6f;\n", "        public float Life = 6f;\n        public Color Tint;                              // alpha 0 = the usual orange\n")
s = rep(s, "            _light.color = new Color(1f, 0.55f, 0.2f);", "            Color fire = Tint.a > 0f ? Tint : new Color(1f, 0.55f, 0.2f);\n            _light.color = fire;")
s = rep(s, "            main.startColor = new Color(1f, 0.6f, 0.2f, 0.28f);", "            main.startColor = Tint.a > 0f ? new Color(Tint.r, Tint.g, Tint.b, 0.3f) : new Color(1f, 0.6f, 0.2f, 0.28f);")
wr('Fireball.cs', s)

# ---------------------------------------------------------------- SaveSystem
s = rd('SaveSystem.cs')
s = rep(s, "        public float PlaySeconds;\n        public string SavedAt;", "        public float PlaySeconds;\n        public int PitChoice, DuelChoice, RiotChoice, TideChoice, TamsinSaved, Ending;\n        public string SavedAt;")
s = rep(s, "if (s.Stage != 5 && s.Stage != 1 && s.Stage != 2 && s.Stage != 3 && s.Stage != 4) return false;",
           "if (s.Stage != 5 && s.Stage != 1 && s.Stage != 2 && s.Stage != 3 && s.Stage != 4 && !(s.Stage >= 10 && s.Stage <= 16)) return false;")
s = rep(s, "                s.Honour = Mathf.Clamp(s.Honour, -99, 99);\n",
           "                s.PitChoice = Mathf.Clamp(s.PitChoice, 0, 3); s.DuelChoice = Mathf.Clamp(s.DuelChoice, 0, 3);\n"
           "                s.RiotChoice = Mathf.Clamp(s.RiotChoice, 0, 3); s.TideChoice = Mathf.Clamp(s.TideChoice, 0, 3);\n"
           "                s.TamsinSaved = Mathf.Clamp(s.TamsinSaved, 0, 1); s.Ending = Mathf.Clamp(s.Ending, 0, 3);\n"
           "                s.Honour = Mathf.Clamp(s.Honour, -99, 99);\n")
wr('SaveSystem.cs', s)

# ---------------------------------------------------------------- InputState
s = rd('InputState.cs')
s = rep(s, "        public static bool Blocked;                        // true while a menu is open\n",
           "        public static bool Blocked;                        // true while a menu is open\n        public static bool Cinematic;                      // true during dialogue: no movement or combat, choices still work\n")
s = rep(s, "            Move = Vector2.ClampMagnitude(Move, 1f);\n",
           "            if (Cinematic)\n            {\n                Move = Vector2.zero; Look = Vector2.zero; Vertical = 0f;\n                Jump = JumpDown = Sprint = Crouch = false;\n"
           "                Interact = Summon = Attack = AttackDown = Block = BlockDown = Fire = FireDown = Boost = false;\n            }\n"
           "            Move = Vector2.ClampMagnitude(Move, 1f);\n")
wr('InputState.cs', s)

# ---------------------------------------------------------------- Bootstrap: keep a dragon template
s = rd('BootstrapAndAutoplay.cs')
s = rep(s, "            if (enemyGo0 != null) Normalize(enemyGo0, 34f);\n",
           "            if (enemyGo0 != null) { Normalize(enemyGo0, 34f); DragonModels.Capture(enemyGo0); }\n")
wr('BootstrapAndAutoplay.cs', s)
print('ok')
