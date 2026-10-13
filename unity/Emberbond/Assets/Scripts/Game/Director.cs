using System.Collections.Generic;
using UnityEngine;

namespace Emberbond
{
    /// <summary>
    /// Runs the story state: new game and continue, the throne-room council with timed tone choices, quests, death and
    /// respawn at checkpoints, autosave, and a small API the self-test uses to drive and inspect the game.
    /// Stages: 5 market fight, 1 mount, 2 fly to the volcano, 3 rival wyrm, 4 chapter complete (free roam).
    /// </summary>
    public sealed class Director : MonoBehaviour
    {
        public ThirdPersonPlayer Player;
        public DragonFlight Dragon;
        public EnemyDragon Enemy;
        public CameraRig Rig;
        public Reputation Rep = new Reputation();

        public string QuestTitle = "";
        public string QuestHint = "";
        public Vector3? Objective;
        public string Toast = "";
        private float _toastTimer;

        // Dialogue
        public bool InDialogue;
        public string Speaker = "", Line = "";
        public List<DialogueOption> Options = new List<DialogueOption>();
        public float DialogueTimer, DialogueDuration = 18f;

        // Progress
        public bool Started;                 // false while the title menu is up
        public int Stage { get { return _stage; } }
        public int Deaths;
        public int Respawns;
        public float PlaySeconds;
        public float DeathProgress { get { return Mathf.Clamp01(_deathTimer / 2.8f); } }
        public readonly List<Soldier> Soldiers = new List<Soldier>();

        private int _stage;
        private float _stageTime;
        private float _deathTimer;
        private Vector3 _checkPlayer, _checkDragon;

        public static Director Instance;

        private void Awake() { Instance = this; }

        public void Say(string text, float seconds = 4f) { Toast = text; _toastTimer = seconds; }

        // ------------------------------------------------------------ starting, continuing, saving

        public void BeginNewGame()
        {
            Started = true;
            Rep = new Reputation();
            Deaths = 0; Respawns = 0; PlaySeconds = 0f;
            AudioDirector.StartAmbience();
            StartCouncil();
        }

        public bool Continue()
        {
            SaveData d;
            if (!SaveSystem.TryLoad(out d)) return false;
            ApplySave(d);
            return true;
        }

        public void ApplySave(SaveData d)
        {
            Started = true;
            InDialogue = false;
            ExitHall();
            ClearSoldiers();
            Rep = new Reputation { Honour = d.Honour, Fear = d.Fear, Cunning = d.Cunning };
            Deaths = d.Deaths; PlaySeconds = d.PlaySeconds;
            Player.ReviveAt(d.PlayerPos);
            Player.Health.Health = d.Health;
            Dragon.ResetTo(d.DragonPos);
            Dragon.Health.Health = d.DragonHealth;
            AudioDirector.StartAmbience();
            BeginQuest(d.Stage, false);
            Say("Continuing your story.");
        }

        public SaveData Snapshot()
        {
            return new SaveData
            {
                Stage = _stage == 0 ? 5 : _stage,
                Honour = Rep.Honour, Fear = Rep.Fear, Cunning = Rep.Cunning,
                PlayerPos = Player.transform.position,
                DragonPos = Dragon.transform.position,
                Health = Player.Health.Health,
                DragonHealth = Dragon.Health.Health,
                Deaths = Deaths,
                PlaySeconds = PlaySeconds,
            };
        }

        public bool SaveNow()
        {
            if (!Started || InDialogue) return false;
            return SaveSystem.Save(Snapshot());
        }

        // ------------------------------------------------------------ throne hall and soldiers

        private readonly List<GameObject> _hallActors = new List<GameObject>();
        private Vector3 _hall;
        private bool _inHall;

        private void EnterHall()
        {
            if (GameBootstrap.Has("-nohall")) return;
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
            Vector3 p = _hall + new Vector3(0.6f + Mathf.Sin(Time.unscaledTime * 0.25f) * 0.35f, 1.55f, -1.2f);
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

        private void ClearSoldiers()
        {
            for (int i = 0; i < Soldiers.Count; i++) if (Soldiers[i] != null) Destroy(Soldiers[i].gameObject);
            Soldiers.Clear();
        }

        private void SpawnSoldiers()
        {
            ClearSoldiers();
            if (GameBootstrap.Has("-nosoldiers")) return;
            Vector3 c = Player.transform.position;
            Vector3 fwd = Camera.main != null ? Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f) * Vector3.forward : Vector3.forward;
            for (int i = 0; i < 4; i++)
            {
                float a = (i - 1.5f) * 22f;
                Vector3 pos = c + Quaternion.Euler(0f, a, 0f) * fwd * (15f + i * 2f);
                Terrain t = Terrain.activeTerrain;
                if (t != null) pos.y = t.SampleHeight(pos) + t.transform.position.y + 0.3f;
                Soldier s = Soldier.Spawn(pos, Player, i);
                if (s != null) Soldiers.Add(s);
            }
        }

        // ------------------------------------------------------------ dialogue

        private void StartCouncil()
        {
            EnterHall();
            InDialogue = true;
            Speaker = "Lord Hand Otrek Hale";
            Line = "The old king is dead and the realm waits. Your mother's claim is strong, and so is her brother's. Will you ask the council to bend the knee?";
            Options = new List<DialogueOption>
            {
                new DialogueOption { Tone = Tone.Diplomatic, Text = "We must reach an agreement", Effect = d => { d.Rep.Honour += 2; d.Say("The council hears you out."); } },
                new DialogueOption { Tone = Tone.Aggressive, Text = "Challenge his loyalty", Effect = d => { d.Rep.Fear += 2; d.Say("The Hand's smile thins."); } },
                new DialogueOption { Tone = Tone.Deceptive, Text = "Agree, for now", Effect = d => { d.Rep.Cunning += 2; d.Say("He believes you. For now."); } },
            };
            DialogueTimer = DialogueDuration;
        }

        public void ChooseOption(int i)
        {
            if (!InDialogue || i < 0 || i >= Options.Count) return;
            if (Options[i].Effect != null) Options[i].Effect(this);
            InDialogue = false;
            ExitHall();
            AudioDirector.Ui();
            BeginQuest(5);
        }

        // ------------------------------------------------------------ quests

        private void SetCheckpoint(bool atPlayer)
        {
            WorldLayout lay = WorldLayout.Load();
            if (atPlayer && Player != null) _checkPlayer = Player.transform.position;
            else _checkPlayer = GroundAt(lay.City + lay.SeaDir * 105f + Vector3.Cross(Vector3.up, lay.SeaDir) * 5f, 0.5f);
            _checkDragon = GroundAt(lay.City + lay.SeaDir * 215f, 1.2f);
        }

        private static Vector3 GroundAt(Vector3 p, float up)
        {
            Terrain t = Terrain.activeTerrain;
            if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y + up;
            return p;
        }

        private void BeginQuest(int stage, bool save = true)
        {
            _stage = stage; _stageTime = 0f;
            switch (stage)
            {
                case 5:
                    QuestTitle = "Blades in the Market"; QuestHint = "Cut them down. Parry (RMB) their strikes.";
                    SetCheckpoint(true);
                    SpawnSoldiers();
                    break;
                case 1:
                    QuestTitle = "First Flight"; QuestHint = "Mount your wyrm (E)";
                    SetCheckpoint(false);
                    Objective = Dragon.transform.position;
                    break;
                case 2:
                    QuestTitle = "First Flight"; QuestHint = "Fly to the rim of the volcano";
                    SetCheckpoint(false);
                    Objective = VolcanoRim();
                    break;
                case 3:
                    QuestTitle = "Confront the Beast"; QuestHint = "Defeat the rival wyrm";
                    SetCheckpoint(false);
                    if (Enemy != null)
                    {
                        Enemy.gameObject.SetActive(true);
                        Enemy.transform.position = Dragon.transform.position + new Vector3(160f, 60f, 120f);
                        Enemy.Init(Dragon.transform);
                    }
                    Objective = null;
                    break;
                case 4:
                    QuestTitle = "Chapter complete"; QuestHint = "The old wyrm falls into the sea. Explore freely.";
                    SetCheckpoint(false);
                    Objective = null;
                    Say("Chapter 2 complete: First Flight", 8f);
                    AudioDirector.Play(Sfx.Fanfare, Vector3.zero, 0.8f);
                    break;
            }
            if (save && Started) SaveNow();
        }

        public void BeginQuestPublic(int stage) { BeginQuest(stage); }

        public static Vector3 VolcanoRim()
        {
            Terrain t = Terrain.activeTerrain;
            float x = 0.62f * 3000f - 1500f + 70f, z = 0.40f * 3000f - 1500f;
            float y = t != null ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : 400f;
            return new Vector3(x, y + 30f, z);
        }

        // ------------------------------------------------------------ death and respawn

        public void Respawn(string message)
        {
            _deathTimer = 0f;
            Respawns++;
            ClearSoldiers();
            Player.ReviveAt(_checkPlayer);
            Dragon.ResetTo(_checkDragon);
            if (_stage == 3 && Enemy != null)
            {
                Enemy.gameObject.SetActive(true);
                Enemy.transform.position = _checkDragon + new Vector3(160f, 60f, 120f);
                Enemy.Init(Dragon.transform);
            }
            if (_stage == 5) SpawnSoldiers();
            if (Rig != null) { Rig.Flight = false; Rig.Frozen = false; }
            Say(message, 3f);
        }

        private void HandleDeath(float dt)
        {
            bool dead = Player.Health.Dead || (Dragon.Mounted && Dragon.Health.Dead);
            if (!dead) { _deathTimer = 0f; return; }
            if (_deathTimer <= 0f)
            {
                Deaths++;
                Say("You fell.", 3f);
                AudioDirector.Play(Sfx.Death, Vector3.zero, 0.9f);
            }
            _deathTimer += dt;
            if (_deathTimer > 2.8f) Respawn("Back on your feet.");
        }

        // ------------------------------------------------------------ per frame

        private void Update()
        {
            InputState.Poll();
            if (_toastTimer > 0f) _toastTimer -= Time.unscaledDeltaTime; else Toast = "";
            if (!Started) return;
            float dt = Time.deltaTime;
            _stageTime += dt;
            PlaySeconds += dt;

            if (InDialogue)
            {
                HallCamera();
                DialogueTimer -= dt;
                if (InputState.Choice >= 0) ChooseOption(InputState.Choice);
                else if (DialogueTimer <= 0f) ChooseOption(1);          // silence reads as a cold stare
                return;
            }

            // Safety net: nobody is allowed to fall out of the world.
            Vector3 pp = Player.transform.position;
            if (float.IsNaN(pp.x + pp.y + pp.z) || pp.y < 30f)
            {
                Respawn("You slipped out of the world.");
                return;
            }

            HandleDeath(dt);
            if (_deathTimer > 0f) return;

            switch (_stage)
            {
                case 5:
                    {
                        int alive = 0;
                        Vector3? nearest = null;
                        float bestD = 1e9f;
                        for (int i = 0; i < Soldiers.Count; i++)
                        {
                            Soldier s = Soldiers[i];
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
                    Objective = Dragon.transform.position;
                    if (Dragon.Mounted) BeginQuest(2);
                    break;
                case 2:
                    if (Vector3.Distance(Dragon.transform.position, VolcanoRim()) < 120f) BeginQuest(3);
                    break;
                case 3:
                    if (Enemy != null && Enemy.Health != null && Enemy.Health.Dead) BeginQuest(4);
                    break;
            }
        }
    }
}
