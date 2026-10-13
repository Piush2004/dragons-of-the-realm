import os, shutil

root = r'D:\Projects\Games\claude\unity\Emberbond'


def patch(rel, pairs):
    p = os.path.join(root, rel)
    s = open(p, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    s = s.replace('\r\n', '\n')
    for a, b in pairs:
        assert s.count(a) == 1, (rel, a[:80], s.count(a))
        s = s.replace(a, b)
    if crlf:
        s = s.replace('\n', '\r\n')
    open(p, 'w', encoding='utf-8', newline='').write(s)


os.makedirs(os.path.join(root, 'Assets', 'Art', 'Hall'), exist_ok=True)
shutil.copy(os.path.join(os.environ['TEMP'], 'hall_out', 'throne_hall.fbx'), os.path.join(root, 'Assets', 'Art', 'Hall', 'throne_hall.fbx'))

# ---- input: scripted attack and block for the unattended test driver
patch('Assets/Scripts/Game/InputState.cs', [
    ('public static bool ScriptedFire;', 'public static bool ScriptedFire;\n        public static bool ScriptedAttack, ScriptedBlock;'),
    ('''                if (ScriptedChoice >= 0) { Choice = ScriptedChoice; ScriptedChoice = -1; }''',
     '''                if (ScriptedChoice >= 0) { Choice = ScriptedChoice; ScriptedChoice = -1; }
                if (ScriptedAttack) { Attack = true; AttackDown = true; ScriptedAttack = false; }
                if (ScriptedBlock) { Block = true; BlockDown = true; ScriptedBlock = false; }'''),
])

# ---- player: aim assist toward the nearest enemy when swinging
patch('Assets/Scripts/Game/PlayerAndCamera.cs', [
    ('''            Vector3 origin = transform.position + Vector3.up * 1.1f;
            Vector3 dir = Quaternion.Euler(0f, YawFacing, 0f) * Vector3.forward;''',
     '''            Vector3 origin = transform.position + Vector3.up * 1.1f;
            // Aim assist: turn toward the closest living enemy within reach.
            Collider[] near = Physics.OverlapSphere(transform.position, 3.2f);
            float best = 99f;
            for (int i = 0; i < near.Length; i++)
            {
                Damageable nd = near[i].GetComponentInParent<Damageable>();
                if (nd == null || nd == Health || nd.Dead) continue;
                Vector3 off = nd.transform.position - transform.position; off.y = 0f;
                if (off.magnitude < best) { best = off.magnitude; YawFacing = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg; }
            }
            Vector3 dir = Quaternion.Euler(0f, YawFacing, 0f) * Vector3.forward;'''),
])

# ---- director: hall council, soldiers
patch('Assets/Scripts/Game/HudAndDirector.cs', [
    ('''        // ------------------------------------------------------------ dialogue
''',
     '''        // ------------------------------------------------------------ throne hall and soldiers

        private readonly List<GameObject> _hallActors = new List<GameObject>();
        private readonly List<Soldier> _soldiers = new List<Soldier>();
        private Vector3 _hall;
        private bool _inHall;

        private void EnterHall()
        {
            WorldLayout lay = WorldLayout.Load();
            _hall = lay.Hall;
            _inHall = true;
            if (Rig != null) Rig.Frozen = true;
            GameObject prince = CharacterFactory.Spawn(_hall + new Vector3(-2.4f, 0.05f, -8f), 90f,
                new Color(0.13f, 0.11f, 0.11f), new Color(0.06f, 0.05f, 0.05f), new Color(0.88f, 0.87f, 0.82f), "HallPrince");
            GameObject lord = CharacterFactory.Spawn(_hall + new Vector3(2.4f, 0.05f, -8f), -90f,
                new Color(0.1f, 0.2f, 0.2f), new Color(0.08f, 0.14f, 0.12f), new Color(0.35f, 0.33f, 0.3f), "HallLord");
            if (prince != null) _hallActors.Add(prince);
            if (lord != null) _hallActors.Add(lord);
            HallCamera();
        }

        private void HallCamera()
        {
            if (!_inHall || Rig == null) return;
            Camera cam = Rig.GetComponent<Camera>();
            Vector3 p = _hall + new Vector3(0.6f + Mathf.Sin(Time.time * 0.25f) * 0.35f, 1.55f, -1.2f);
            Vector3 look = _hall + new Vector3(0f, 1.5f, -8f);
            Rig.transform.position = p;
            Rig.transform.rotation = Quaternion.LookRotation(look - p, Vector3.up);
            if (cam != null) cam.fieldOfView = 36f;
        }

        private void ExitHall()
        {
            _inHall = false;
            for (int i = 0; i < _hallActors.Count; i++) if (_hallActors[i] != null) Destroy(_hallActors[i]);
            _hallActors.Clear();
            if (Rig != null) Rig.Frozen = false;
        }

        private void SpawnSoldiers()
        {
            _soldiers.Clear();
            Vector3 c = Player.transform.position;
            Vector3 fwd = Camera.main != null ? Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f) * Vector3.forward : Vector3.forward;
            for (int i = 0; i < 4; i++)
            {
                float a = (i - 1.5f) * 22f;
                Vector3 pos = c + Quaternion.Euler(0f, a, 0f) * fwd * (15f + i * 2f);
                Terrain t = Terrain.activeTerrain;
                if (t != null) pos.y = t.SampleHeight(pos) + t.transform.position.y + 0.3f;
                Soldier s = Soldier.Spawn(pos, Player, i);
                if (s != null) _soldiers.Add(s);
            }
        }

        // ------------------------------------------------------------ dialogue
'''),
    ('''            InDialogue = true;
            Speaker = "Lord Hand Otrek Hale";''',
     '''            EnterHall();
            InDialogue = true;
            Speaker = "Lord Hand Otrek Hale";'''),
    ('''            InDialogue = false;
            BeginQuest(1);''',
     '''            InDialogue = false;
            ExitHall();
            BeginQuest(5);'''),
    ('''                case 1:
                    QuestTitle = "First Flight"; QuestHint = "Mount your wyrm (E)";''',
     '''                case 5:
                    QuestTitle = "Blades in the Market"; QuestHint = "Cut them down. Parry (RMB) their strikes.";
                    SpawnSoldiers();
                    break;
                case 1:
                    QuestTitle = "First Flight"; QuestHint = "Mount your wyrm (E)";'''),
    ('''            if (InDialogue)
            {
                DialogueTimer -= Time.deltaTime;''',
     '''            if (InDialogue)
            {
                HallCamera();
                DialogueTimer -= Time.deltaTime;'''),
    ('''            switch (_stage)
            {
                case 1:
                    Objective = Dragon.transform.position;''',
     '''            switch (_stage)
            {
                case 5:
                    {
                        int alive = 0;
                        Vector3? nearest = null;
                        float bestD = 1e9f;
                        for (int i = 0; i < _soldiers.Count; i++)
                        {
                            Soldier s = _soldiers[i];
                            if (s == null || s.Health == null || s.Health.Dead) continue;
                            alive++;
                            float d = (s.transform.position - Player.transform.position).sqrMagnitude;
                            if (d < bestD) { bestD = d; nearest = s.transform.position; }
                        }
                        Objective = nearest;
                        if (alive == 0 && _stageTime > 1f) { Say("The market falls quiet."); BeginQuest(1); }
                    }
                    break;
                case 1:
                    Objective = Dragon.transform.position;'''),
])

# ---- bootstrap: place player and dragon relative to the city
patch('Assets/Scripts/Game/BootstrapAndAutoplay.cs', [
    ('''            Vector3 dp = dragonGo.transform.position;''',
     '''            WorldLayout lay = WorldLayout.Load();
            Vector3 dp = lay.City + lay.SeaDir * 215f;'''),
    ('''            Vector3 spawn = dragonGo.transform.position + new Vector3(0f, 0f, -26f);''',
     '''            Vector3 spawn = lay.City + lay.SeaDir * 105f + Vector3.Cross(Vector3.up, lay.SeaDir) * 5f;'''),
    ('''            rig.Yaw = 0f;                                   // look toward the dragon''',
     '''            rig.Yaw = lay.SeaYaw + 180f;                    // look inland, toward the keep'''),
])
print('hall patch ok')
