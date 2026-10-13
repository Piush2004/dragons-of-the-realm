import os, re

root = r'D:\Projects\Games\claude\unity\Emberbond'


def rd(rel):
    s = open(os.path.join(root, rel), encoding='utf-8', newline='').read()
    return s.replace('\r\n', '\n'), '\r\n' in s


def wr(rel, s, crlf):
    if crlf:
        s = s.replace('\n', '\r\n')
    open(os.path.join(root, rel), 'w', encoding='utf-8', newline='').write(s)


def patch(rel, pairs):
    s, crlf = rd(rel)
    for a, b in pairs:
        assert s.count(a) == 1, (rel, a[:90], s.count(a))
        s = s.replace(a, b)
    wr(rel, s, crlf)


G = 'Assets/Scripts/Game/'

# ---- the Director now lives in its own file
s, crlf = rd(G + 'HudAndDirector.cs')
a = s.index('    public sealed class Director : MonoBehaviour')
a = s.rfind('    /// <summary>', 0, a)
b = s.index('    /// <summary>The on-screen interface, styled after the reference frames')
s = s[:a] + s[b:]
wr(G + 'HudAndDirector.cs', s, crlf)

patch(G + 'HudAndDirector.cs', [
    ('''        private void OnGUI()
        {
            if (Director == null) return;
            Styles();
            float scale = Mathf.Min(Screen.width / W, Screen.height / H);''',
     '''        private float _fps = 60f;

        private void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
        }

        private void OnGUI()
        {
            if (Director == null || !Director.Started) return;
            Styles();
            float scale = Mathf.Min(Screen.width / W, Screen.height / H) * GameSettings.UiScale;'''),
    ('''                GUI.Label(new Rect(W * 0.5f - 410f, 160f, 820f, 40f), Director.Toast, _title);
            }
''',
     '''                GUI.Label(new Rect(W * 0.5f - 410f, 160f, 820f, 40f), Director.Toast, _title);
            }
            if (GameSettings.ShowFps) GUI.Label(new Rect(W - 150f, 70f, 130f, 28f), Mathf.RoundToInt(_fps) + " fps", _small);
            if (Director.DeathProgress > 0f)
            {
                Rect(0f, 0f, W, H, new Color(0.3f, 0f, 0f, 0.6f * Director.DeathProgress));
                GUI.Label(new Rect(0f, H * 0.4f, W, 80f), "You fell", _title);
            }
'''),
    ('''            float y = H - 40f - lines.Count * 28f;''',
     '''            lines.Add("[Esc]  Menu");
            float y = H - 40f - lines.Count * 28f;'''),
])

# ---- input: sensitivity, invert, blocked while in a menu, scripted move and boost
patch(G + 'InputState.cs', [
    ('''        public static bool ScriptedAttack, ScriptedBlock;''',
     '''        public static bool ScriptedAttack, ScriptedBlock, ScriptedBoost;
        public static Vector2 ScriptedMove;
        public static bool Blocked;                        // true while a menu is open'''),
    ('''                if (Move.sqrMagnitude < 0.01f) Move = new Vector2(0f, 0.0f);''',
     '''                if (ScriptedMove.sqrMagnitude > 0.01f) Move = ScriptedMove;
                if (ScriptedBoost) { Boost = true; Sprint = true; }'''),
    ('''            Move = Vector2.ClampMagnitude(Move, 1f);''',
     '''            if (!Scripted) Look *= GameSettings.MouseSens;
            if (GameSettings.InvertY) Look.y = -Look.y;
            if (Blocked)
            {
                Move = Vector2.zero; Look = Vector2.zero; Vertical = 0f; Choice = -1;
                Jump = JumpDown = Sprint = Crouch = false;
                Interact = Summon = Attack = AttackDown = Block = BlockDown = Fire = FireDown = Boost = false;
            }
            Move = Vector2.ClampMagnitude(Move, 1f);'''),
])

# ---- camera field of view from settings; player helpers and sounds
patch(G + 'PlayerAndCamera.cs', [
    ('float tf = 62f + Mathf.Clamp(FlightSpeed - 30f, 0f, 40f) * 0.45f;',
     'float tf = GameSettings.Fov + 2f + Mathf.Clamp(FlightSpeed - 30f, 0f, 40f) * 0.45f;'),
    ('_fov = Mathf.Lerp(_fov, 58f, 1f - Mathf.Exp(-4f * Time.deltaTime));',
     '_fov = Mathf.Lerp(_fov, GameSettings.Fov - 2f, 1f - Mathf.Exp(-4f * Time.deltaTime));'),
    ('''        /// <summary>A shove from a blow or an explosion; decays over about half a second.</summary>''',
     '''        /// <summary>Moves the rider instantly without healing (used by tests and cutscenes).</summary>
        public void Teleport(Vector3 pos)
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();
            bool was = _cc.enabled;
            _cc.enabled = false;
            transform.position = pos;
            _cc.enabled = was && !Mounted;
            _vel = Vector3.zero; _impulse = Vector3.zero;
        }

        /// <summary>Brings the rider back on foot at full strength.</summary>
        public void ReviveAt(Vector3 pos)
        {
            SetMounted(false);
            Health.Health = Health.MaxHealth;
            Stamina = MaxStamina;
            ParryWindow = 0f; SwingTimer = 0f;
            Teleport(pos);
            _cc.enabled = true;
        }

        /// <summary>A shove from a blow or an explosion; decays over about half a second.</summary>'''),
    ('''            Vector3 origin = transform.position + Vector3.up * 1.1f;''',
     '''            AudioDirector.Play(Sfx.Swing, transform.position, 0.8f);
            Vector3 origin = transform.position + Vector3.up * 1.1f;'''),
    ('''            if (ParryWindow > 0f) { ParryWindow = 0f; Stamina = Mathf.Min(MaxStamina, Stamina + 15f); return true; }''',
     '''            if (ParryWindow > 0f)
            {
                ParryWindow = 0f; Stamina = Mathf.Min(MaxStamina, Stamina + 15f);
                AudioDirector.Play(Sfx.Clang, transform.position, 1f);
                return true;
            }'''),
])

# ---- dragon: reset, world bounds, sounds; enemy re-initialisation
patch(G + 'Dragons.cs', [
    ('''        /// <summary>Points the flight along a direction (used when placing the dragon for tests and cutscenes).</summary>''',
     '''        /// <summary>Puts the dragon back on the ground, unmounted and healed (respawn and loading).</summary>
        public void ResetTo(Vector3 pos)
        {
            Mounted = false; Speed = 0f; _roll = 0f; _pitch = 0f;
            if (Rig != null) Rig.Flight = false;
            if (Rider != null) Rider.SetMounted(false);
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, _yaw + ModelYaw, 0f);
            Health.Health = Health.MaxHealth;
        }

        /// <summary>Points the flight along a direction (used when placing the dragon for tests and cutscenes).</summary>'''),
    ('''            _impactCooldown -= dt;
            flight = Quaternion.Euler(_pitch, _yaw, 0f);''',
     '''            // The world ends at the edge of the map: turn the dragon back, harder the closer it gets.
            Vector3 flat = new Vector3(pos.x, 0f, pos.z);
            float rad = flat.magnitude;
            if (rad > 1250f)
            {
                Vector3 inward = -flat.normalized;
                Vector3 fw = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
                float turn = Mathf.Lerp(20f, 120f, Mathf.InverseLerp(1250f, 1480f, rad));
                Aim(Vector3.RotateTowards(fw, inward, Mathf.Deg2Rad * turn * dt, 0f));
                if (rad > 1490f) { flat = flat.normalized * 1490f; pos.x = flat.x; pos.z = flat.z; }
            }
            pos.y = Mathf.Min(pos.y, 1000f);
            _impactCooldown -= dt;
            flight = Quaternion.Euler(_pitch, _yaw, 0f);'''),
    ('''            _flapPhase += dt * (6f + Speed * 0.04f);''',
     '''            float flapStep = dt * (6f + Speed * 0.04f);
            _flapPhase += flapStep;
            if (Mathf.Floor(_flapPhase / 6.2832f) != Mathf.Floor((_flapPhase - flapStep) / 6.2832f))
                AudioDirector.Play(Sfx.Flap, transform.position, 0.9f);
            AudioDirector.SetWind(Speed / 70f);'''),
    ('''            Crashes++;
            LastImpact = impact;''',
     '''            Crashes++;
            LastImpact = impact;
            AudioDirector.Play(Sfx.Crash, point, Mathf.Clamp01(impact / 30f) + 0.3f);'''),
    ('''        private Vector3 _vel;
        private DragonAnimator _anim;''',
     '''        private Vector3 _vel;
        private DragonAnimator _anim;
        private bool _inited;

        private void Vanish() { gameObject.SetActive(false); }'''),
    ('''            Health.OnDied += d => { Destroy(gameObject, 0.5f); };''',
     '''            if (!_inited) { _inited = true; Health.OnDied += d => { Invoke("Vanish", 1.2f); }; }'''),
    ('''            Collider c = gameObject.AddComponent<SphereCollider>();
            ((SphereCollider)c).radius = 6f;''',
     '''            if (GetComponent<SphereCollider>() == null)
            {
                SphereCollider c = gameObject.AddComponent<SphereCollider>();
                c.radius = 6f;
            }'''),
])

patch(G + 'Damageable.cs', [
    ('''            Health = Mathf.Max(0f, Health - amount);''',
     '''            Health = Mathf.Max(0f, Health - amount);
            AudioDirector.Play(Sfx.Hit, transform.position, 0.8f);'''),
])

patch(G + 'Fireball.cs', [
    ('''            Fireball f = go.AddComponent<Fireball>();''',
     '''            Fireball f = go.AddComponent<Fireball>();
            AudioDirector.Play(Sfx.Fire, pos, 1f);'''),
    ('''            Effects.Burst(at, Radius);
            Destroy(gameObject);''',
     '''            Effects.Burst(at, Radius);
            AudioDirector.Play(Sfx.Explosion, at, 1f);
            Destroy(gameObject);'''),
])

patch(G + 'Soldier.cs', [
    ('''        private bool _ragdoll;''',
     '''        private bool _ragdoll;
        public int Parried, Landed;
        public bool Staggered { get { return _stagger > 0f; } }'''),
    ('''                        if (Target.TryParry()) _stagger = 1.4f;       // knocked off balance
                        else Target.Health.Hurt(12f, gameObject);''',
     '''                        if (Target.TryParry()) { _stagger = 1.4f; Parried++; }      // knocked off balance
                        else
                        {
                            Target.Health.Hurt(12f, gameObject);
                            Landed++;
                            Vector3 shove = Target.transform.position - transform.position; shove.y = 0f;
                            Target.Knock(shove.normalized * 4f);
                        }'''),
])

patch(G + 'RiderAnimator.cs', [
    ('''        private float _phase;''', '''        private float _phase, _lastS;'''),
    ('''            float stride = 32f * amp + 22f * run;''',
     '''            if (Speed > 0.4f && Mathf.Sign(s) != Mathf.Sign(_lastS)) AudioDirector.Play(Sfx.Step, transform.position, 0.3f);
            _lastS = s;
            float stride = 32f * amp + 22f * run;'''),
])

patch(G + 'QualityGovernor.cs', [
    ('''            bool weak = IsWeakGpu();
            string q = Arg("-quality");''',
     '''            bool weak = IsWeakGpu();
            if (GameSettings.Quality == 1) weak = false; else if (GameSettings.Quality == 2) weak = true;
            string q = Arg("-quality");'''),
    ('''            GameObject go = new GameObject("PerformanceScaler");
            Object.DontDestroyOnLoad(go);
            PerformanceScaler ps = go.AddComponent<PerformanceScaler>();''',
     '''            PerformanceScaler ps = Object.FindFirstObjectByType<PerformanceScaler>();
            if (ps == null)
            {
                GameObject go = new GameObject("PerformanceScaler");
                Object.DontDestroyOnLoad(go);
                ps = go.AddComponent<PerformanceScaler>();
            }'''),
    ('''                ps.Pinned = Mathf.Clamp(fixedScale, 0.3f, 1f);''',
     '''                ps.Pinned = Mathf.Clamp(fixedScale, 0.3f, 1f);
            else if (GameSettings.RenderScale > 0)
                ps.Pinned = GameSettings.RenderScale / 100f;'''),
])

patch(G + 'BootstrapAndAutoplay.cs', [
    ('''            QualityGovernor.Apply();''',
     '''            GameSettings.Load();
            AudioDirector.Init();
            QualityGovernor.Apply();'''),
    ('''            if (Has("-autoplay")) dir.AddComponent<AutoPlay>();
            else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }''',
     '''            string saveFile = null;
            string[] argv = Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length - 1; i++) if (argv[i] == "-savefile") saveFile = argv[i + 1];
            if (saveFile != null) SaveSystem.PathOverride = saveFile;

            Menu menu = dir.AddComponent<Menu>();
            menu.Director = d;
            menu.Rig = rig;
            bool headless = Has("-autoplay") || Has("-selftest") || Has("-nomenu");
            if (Has("-autoplay")) dir.AddComponent<AutoPlay>();
            if (Has("-selftest")) dir.AddComponent<SelfTest>();
            if (headless) d.BeginNewGame(); else menu.ShowMain();'''),
])
print('market patch ok')
