using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberbond
{
    /// <summary>The torchlit pit under Hearthmoor: a spiral stair down to the dragon pens. Built at runtime, far above the sea.</summary>
    public sealed class PitArena
    {
        public static readonly Vector3 Origin = new Vector3(420f, 1300f, -420f);
        public const int Steps = 64;
        public const float Rise = 0.4f, StairRadius = 22f, StepAngle = 7.5f;

        public GameObject Root;
        public Vector3 Top, Bottom, BeastPos, WyrmPos;
        public float FloorY;

        public static PitArena Build()
        {
            PitArena p = new PitArena();
            p.FloorY = Origin.y;
            p.Root = new GameObject("DragonPit");
            Transform t = p.Root.transform;
            Material stone = Kit.Lit(new Color(0.23f, 0.21f, 0.2f), 0.1f);
            Material step = Kit.Lit(new Color(0.3f, 0.27f, 0.24f), 0.1f);
            Material iron = Kit.Lit(new Color(0.12f, 0.12f, 0.13f), 0.45f, 0.9f);
            Material flame = Kit.Glow(new Color(6f, 2.6f, 0.7f, 1f));
            Vector3 o = Origin;

            Kit.Box(t, new Vector3(o.x, o.y - 0.5f, o.z), new Vector3(60f, 1f, 60f), Quaternion.identity, stone, "Floor");
            Kit.Box(t, new Vector3(o.x, o.y + 39f, o.z), new Vector3(64f, 1f, 64f), Quaternion.identity, stone, "Ceiling");
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f;
                Quaternion r = Quaternion.Euler(0f, a, 0f);
                Vector3 c = o + r * new Vector3(0f, 19.5f, 28.4f);
                Kit.Box(t, c, new Vector3(8.2f, 41f, 2.4f), r, stone, "Wall");
            }
            for (int i = 0; i < Steps; i++)
            {
                float a = i * StepAngle;
                Quaternion r = Quaternion.Euler(0f, a, 0f);
                float h = o.y + (Steps - i) * Rise;
                Vector3 c = o + r * new Vector3(0f, 0f, StairRadius) + new Vector3(0f, h - 0.4f - o.y, 0f);
                Kit.Box(t, c, new Vector3(2.9f, 0.8f, 6f), r, step, "Step" + i);
                // Rail on the open side, so nobody walks off the inner edge by accident.
                Vector3 rc = o + r * new Vector3(0f, 0f, StairRadius - 3.1f) + new Vector3(0f, h + 0.55f - o.y, 0f);
                Kit.Box(t, rc, new Vector3(2.9f, 1.2f, 0.25f), r, iron, "Rail");
                if (i % 5 == 0)
                {
                    Vector3 tp = o + r * new Vector3(0f, 0f, 27f) + new Vector3(0f, h + 2.4f - o.y, 0f);
                    Kit.Box(t, tp - new Vector3(0f, 0.5f, 0f), new Vector3(0.18f, 1f, 0.18f), r, iron, "Sconce", false);
                    Kit.Ball(t, tp + new Vector3(0f, 0.25f, 0f), 0.28f, flame, "Flame");
                    Kit.PointLight(t, tp, new Color(1f, 0.58f, 0.25f), 24f, 360000f, "Torch");
                }
            }

            // Pit pillars with chains for the beast.
            p.BeastPos = o + new Vector3(-6f, 0f, -6f);
            p.WyrmPos = o + new Vector3(11f, 0f, -4f);
            for (int k = 0; k < 4; k++)
            {
                float a = 215f + k * 55f;
                Quaternion r = Quaternion.Euler(0f, a, 0f);
                Vector3 post = o + r * new Vector3(0f, 0f, 21f);
                Kit.Box(t, post + Vector3.up * 1.5f, new Vector3(0.9f, 3f, 0.9f), r, iron, "ChainPost");
                Vector3 mid = (post + p.BeastPos) * 0.5f + Vector3.up * 0.4f;
                Vector3 dir = (p.BeastPos + Vector3.up * 1.4f - (post + Vector3.up * 1.2f));
                Kit.Box(t, mid + Vector3.up * 0.7f, new Vector3(0.18f, 0.18f, dir.magnitude), Quaternion.LookRotation(dir), iron, "Chain", false);
            }
            Kit.PointLight(t, o + new Vector3(0f, 9f, 0f), new Color(1f, 0.5f, 0.2f), 46f, 900000f, "PitGlow");
            Kit.LocalGrade(t, o + new Vector3(0f, 19f, 0f), new Vector3(66f, 46f, 66f), 12.2f, new Color(1f, 0.9f, 0.8f), 70f);

            p.Top = o + Quaternion.Euler(0f, 0f, 0f) * new Vector3(0f, 0f, StairRadius) + new Vector3(0f, Steps * Rise + 0.9f, 0f);
            p.Bottom = o + new Vector3(0f, 0.9f, 0f);
            p.Root.SetActive(false);
            return p;
        }
    }

    /// <summary>The five chapters after the first flight: the Pit, the Charge, the aerial duel, the riot and the Pale Tide.</summary>
    public sealed partial class Director
    {
        public const int StagePit = 10, StageCharge = 11, StageDuel = 12, StageRiot = 13, StageTide = 14, StageEgg = 15, StageEnd = 16;

        // Choices that the endings and the save read: 0 not yet made, 1 diplomatic, 2 aggressive, 3 deceptive.
        public int PitChoice, DuelChoice, RiotChoice, TideChoice;
        public bool TamsinSaved;
        public int Ending;                         // 0 none, 1 Hearthfire, 2 Wildfire, 3 Unbound
        public string EndingTitle = "", EndingText = "", EndingStats = "";
        public bool EndingShown;

        private readonly List<GameObject> _chapterObjects = new List<GameObject>();
        private readonly List<SiegeTower> _towers = new List<SiegeTower>();
        private PitArena _pit;
        private int _sub, _wave;
        private float _subTime;
        private Action<int> _afterDialogue;
        private Damageable _tamsin;
        private float _tamsinAcc;
        private Volume _grade;
        private float _gradeTarget;
        private GameObject _snow;
        private Vector3 _eggPos;

        // Test hooks.
        public void DebugFinishEnding(int kind) { FinishEnding(kind); }
        public void DebugKillHostiles()
        {
            for (int i = 0; i < Soldiers.Count; i++)
                if (Soldiers[i] != null && Soldiers[i].Health != null && !Soldiers[i].Health.Dead) Soldiers[i].Health.Hurt(100000f, Player.gameObject);
        }
        public Vector3 EggPosition { get { return _eggPos; } }
        public SiegeTower TowerAt(int i) { return i >= 0 && i < _towers.Count ? _towers[i] : null; }

        public static bool IsChapterStage(int s) { return s >= StagePit && s <= StageEnd; }
        public int TowersStanding { get { int n = 0; for (int i = 0; i < _towers.Count; i++) if (_towers[i] != null && !_towers[i].Fallen) n++; return n; } }
        public int TowersTotal { get { return _towers.Count; } }
        public Vector3 PitFloor { get { return _pit != null ? _pit.Bottom : Vector3.zero; } }
        public Vector3 PitTop { get { return _pit != null ? _pit.Top : Vector3.zero; } }
        public bool PitActive { get { return _pit != null && _pit.Root.activeSelf; } }
        public Damageable Tamsin { get { return _tamsin; } }
        public int Wave { get { return _wave; } }
        public int SubStage { get { return _sub; } }

        private int AliveSoldiers()
        {
            int n = 0;
            for (int i = 0; i < Soldiers.Count; i++) if (Soldiers[i] != null && Soldiers[i].Health != null && !Soldiers[i].Health.Dead) n++;
            return n;
        }

        private T Track<T>(T go) where T : UnityEngine.Object
        {
            GameObject g = go as GameObject;
            if (g != null) _chapterObjects.Add(g);
            return go;
        }

        private void ClearChapter()
        {
            for (int i = 0; i < _chapterObjects.Count; i++) if (_chapterObjects[i] != null) Destroy(_chapterObjects[i]);
            _chapterObjects.Clear();
            _towers.Clear();
            ClearSoldiers();
            _tamsin = null;
            _beastObj = null;
            _afterDialogue = null;
            _sub = 0; _wave = 0; _subTime = 0f; _bossSpawned = false;
            if (Enemy != null && Enemy.gameObject.activeSelf && _stage != 3) Enemy.gameObject.SetActive(false);
        }

        private GameObject _beastObj;
        private bool _bossSpawned;

        private void EnsurePit()
        {
            if (_pit == null) _pit = PitArena.Build();
            _pit.Root.SetActive(true);
            Dragon.GroundOverride = _pit.FloorY;
        }

        private void LeavePit()
        {
            if (_pit != null) _pit.Root.SetActive(false);
            Dragon.GroundOverride = null;
        }

        private void SetCheckpointAt(Vector3 player, Vector3 dragon) { _checkPlayer = player; _checkDragon = dragon; }

        private Soldier Hostile(Vector3 pos, string name, Color cloth, Color cloak, float hp, float speed, float damage, float aggro, int index)
        {
            Soldier s = Soldier.Spawn(pos, Player, index, cloth, cloak, name);
            if (s == null) return null;
            s.Health.MaxHealth = s.Health.Health = hp;
            s.Health.DisplayName = name;
            s.WalkSpeed = speed; s.Damage = damage; s.Aggro = aggro;
            Soldiers.Add(s);
            return s;
        }

        /// <summary>Finds dry ground at a distance from the city, deterministic so a retry looks the same.</summary>
        private List<Vector3> LandPoints(int count, float minR, float maxR, float startDeg)
        {
            WorldLayout lay = WorldLayout.Load();
            List<Vector3> pts = new List<Vector3>();
            for (int k = 0; k < 72 && pts.Count < count; k++)
            {
                float a = (startDeg + k * 17f) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(minR, maxR, (k % 5) / 4f);
                Vector3 p = lay.City + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                float h = Kit.GroundY(p);
                if (h < ThirdPersonPlayer.SeaLevel + 3f) continue;
                if (Mathf.Abs(Kit.GroundY(p + Vector3.right * 6f) - h) > 3f || Mathf.Abs(Kit.GroundY(p + Vector3.forward * 6f) - h) > 3f) continue;
                bool apart = true;
                for (int j = 0; j < pts.Count; j++) if ((pts[j] - p).sqrMagnitude < 70f * 70f) apart = false;
                if (!apart) continue;
                p.y = h;
                pts.Add(p);
            }
            while (pts.Count < count)                                  // never fewer than asked for: fall back to the beach
            {
                Vector3 p = lay.City + lay.SeaDir * (240f + 40f * pts.Count) + Vector3.Cross(Vector3.up, lay.SeaDir) * (pts.Count * 40f - 60f);
                p.y = Mathf.Max(Kit.GroundY(p), ThirdPersonPlayer.SeaLevel + 3f);
                pts.Add(p);
            }
            return pts;
        }

        private void PlaceHeroes(Vector3 playerPos, Vector3 dragonPos, float yaw)
        {
            Player.ReviveAt(playerPos);
            Dragon.ResetTo(dragonPos);
            if (Rig != null) { Rig.Flight = false; Rig.Frozen = false; Rig.Yaw = yaw; Rig.Pitch = 12f; }
            SetCheckpointAt(playerPos, dragonPos);
        }

        // ------------------------------------------------------------ chapter openings

        private void BeginChapter(int stage)
        {
            ClearChapter();
            if (stage != StagePit && stage != StageRiot) LeavePit();
            if (stage != StageTide && stage != StageEnd && _grade != null) _gradeTarget = 0f;
            if (stage != StageEnd) EndingShown = false;
            WorldLayout lay = WorldLayout.Load();
            switch (stage)
            {
                case StagePit:
                    {
                        QuestTitle = "The Stair and the Torch"; QuestHint = "Go down into the Pit. The jailers hold the keys.";
                        EnsurePit();
                        PlaceHeroes(_pit.Top, _pit.WyrmPos + Vector3.up * 1.2f, 90f);
                        Vector3[] spots = { new Vector3(-4f, 0f, 12f), new Vector3(6f, 0f, 14f), new Vector3(-14f, 0f, 2f), new Vector3(14f, 0f, 6f) };
                        for (int i = 0; i < spots.Length; i++)
                            Hostile(PitArena.Origin + spots[i] + Vector3.up * 0.3f, "Jailer", new Color(0.18f, 0.18f, 0.2f), new Color(0.1f, 0.1f, 0.1f), 60f, 3.0f, 12f, 16f, i);
                        GameObject beast = DragonModels.Spawn(_pit.BeastPos + Vector3.up * 1.5f, Quaternion.Euler(0f, 210f, 0f), 1.15f, new Color(0.6f, 0.56f, 0.55f), "PitBeast");
                        if (beast != null)
                        {
                            AmbientDragon ad = beast.AddComponent<AmbientDragon>();
                            ad.Orbiting = false;
                            _beastObj = beast;
                            Track(beast);
                        }
                        Objective = _pit.Bottom;
                        Say("Hearthmoor keeps its dragons under the stone.", 5f);
                    }
                    break;

                case StageCharge:
                    {
                        QuestTitle = "The Charge"; QuestHint = "Burn the siege line. Mount your wyrm (" + Bindings.Short(GameAction.Interact) + ").";
                        List<Vector3> pts = LandPoints(4, 330f, 520f, lay.SeaYaw + 40f);
                        Vector3 start = lay.City + lay.SeaDir * 105f;
                        start.y = Kit.GroundY(start) + 0.6f;
                        Vector3 dpos = lay.City + lay.SeaDir * 150f;
                        dpos.y = Kit.GroundY(dpos) + 1.2f;
                        PlaceHeroes(start, dpos, lay.SeaYaw + 180f);
                        for (int i = 0; i < pts.Count; i++)
                        {
                            Vector3 toCity = lay.City - pts[i];
                            float yaw = Mathf.Atan2(toCity.x, toCity.z) * Mathf.Rad2Deg;
                            SiegeTower tw = SiegeTower.Build(pts[i], yaw);
                            _towers.Add(tw);
                            Track(tw.gameObject);
                            for (int k = 0; k < 2; k++)
                            {
                                Vector3 sp = pts[i] + Quaternion.Euler(0f, yaw + 90f + k * 180f, 0f) * Vector3.forward * 9f;
                                sp.y = Kit.GroundY(sp) + 0.3f;
                                Hostile(sp, "Besieger", new Color(0.3f, 0.3f, 0.34f), new Color(0.5f, 0.12f, 0.1f), 55f, 3.2f, 12f, 40f, i * 2 + k);
                            }
                        }
                        Objective = _towers.Count > 0 ? _towers[0].transform.position : (Vector3?)null;
                        Say("Siege towers at the walls. Burn them.", 4f);
                    }
                    break;

                case StageDuel:
                    {
                        QuestTitle = "Over the Gods Lake"; QuestHint = "Bring down Old Vael. Mount your wyrm and keep moving.";
                        Vector3 start = lay.City + lay.SeaDir * 105f;
                        start.y = Kit.GroundY(start) + 0.6f;
                        Vector3 dpos = lay.City + lay.SeaDir * 150f;
                        dpos.y = Kit.GroundY(dpos) + 1.2f;
                        PlaceHeroes(start, dpos, lay.SeaYaw + 180f);
                        if (Enemy != null)
                        {
                            Enemy.gameObject.SetActive(true);
                            Enemy.transform.position = Dragon.transform.position + new Vector3(230f, 110f, 160f);
                            Enemy.Init(Dragon.Mounted ? Dragon.transform : Player.transform);
                            Enemy.Configure(900f, "Old Vael", 1.25f, 150f, 46f, 2.6f, 4.2f, 26f, new Color(0.7f, 0.62f, 0.62f), new Color(1f, 0.4f, 0.15f));
                        }
                        Objective = null;
                        Say("Prince Aemric rides the oldest wyrm in the realm.", 5f);
                    }
                    break;

                case StageRiot:
                    {
                        QuestTitle = "The Pit Riot"; QuestHint = "Hold the pens. Keep Tamsin alive.";
                        EnsurePit();
                        Vector3 o = PitArena.Origin;
                        PlaceHeroes(o + new Vector3(-9f, 1f, 9f), _pit.WyrmPos + Vector3.up * 1.2f, 0f);
                        GameObject tam = CharacterFactory.Spawn(o + new Vector3(-3f, 0.1f, 7f), 200f, new Color(0.42f, 0.36f, 0.22f), new Color(0.12f, 0.3f, 0.16f), new Color(0.36f, 0.2f, 0.1f), "Tamsin");
                        if (tam != null)
                        {
                            _tamsin = tam.AddComponent<Damageable>();
                            _tamsin.MaxHealth = _tamsin.Health = 90f;
                            _tamsin.DisplayName = "Tamsin";
                            CapsuleCollider cc = tam.AddComponent<CapsuleCollider>();
                            cc.height = 1.7f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.85f, 0f);
                            Track(tam);
                        }
                        GameObject beast = DragonModels.Spawn(_pit.BeastPos + Vector3.up * 1.5f, Quaternion.Euler(0f, 210f, 0f), 1.15f, new Color(0.6f, 0.56f, 0.55f), "PitBeast");
                        if (beast != null) { beast.AddComponent<AmbientDragon>(); Track(beast); }
                        SpawnMobWave();
                        Objective = null;
                        Say("The mob is at the pen gate!", 4f);
                    }
                    break;

                case StageTide:
                    {
                        QuestTitle = "The Pale Tide"; QuestHint = "Hold the harbour against the Cold Dead.";
                        Vector3 start = lay.City + lay.SeaDir * 105f;
                        start.y = Kit.GroundY(start) + 0.6f;
                        Vector3 dpos = lay.City + lay.SeaDir * 150f;
                        dpos.y = Kit.GroundY(dpos) + 1.2f;
                        PlaceHeroes(start, dpos, lay.SeaYaw + 180f);
                        EnsureGrade(new Color(0.7f, 0.82f, 1.05f), -30f, 650f, new Color(0.62f, 0.7f, 0.82f));
                        _gradeTarget = 1f;
                        EnsureSnow();
                        SpawnColdWave(6);
                        if (PitChoice == 1 && DragonModels.Available)
                        {
                            GameObject ally = DragonModels.Spawn(start + Vector3.up * 90f, Quaternion.identity, 1.1f, new Color(0.62f, 0.58f, 0.57f), "PitBeastAlly");
                            if (ally != null)
                            {
                                AmbientDragon ad = ally.AddComponent<AmbientDragon>();
                                ad.Orbiting = true; ad.Bombard = true; ad.Centre = start; ad.Radius = 70f; ad.Height = 80f; ad.Speed = 0.22f; ad.Protect = Player.transform;
                                Track(ally);
                                Say("The Beast circles over the harbour, and does not leave.", 5f);
                            }
                        }
                        else Say("Winter breaks the strait. The dead walk the quay.", 5f);
                        Objective = null;
                    }
                    break;

                case StageEgg:
                    {
                        QuestTitle = "The Last Egg"; QuestHint = "Fly to the rim of the volcano. Something waits in the caldera.";
                        Vector3 start = lay.City + lay.SeaDir * 105f;
                        start.y = Kit.GroundY(start) + 0.6f;
                        Vector3 dpos = lay.City + lay.SeaDir * 150f;
                        dpos.y = Kit.GroundY(dpos) + 1.2f;
                        PlaceHeroes(start, dpos, lay.SeaYaw + 180f);
                        Vector3 rim = VolcanoRim();
                        _eggPos = new Vector3(rim.x, Kit.GroundY(rim) + 1.4f, rim.z);
                        GameObject egg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        egg.name = "LastEgg";
                        egg.transform.position = _eggPos;
                        egg.transform.localScale = new Vector3(2.4f, 3.4f, 2.4f);
                        egg.GetComponent<Renderer>().sharedMaterial = Kit.Lit(new Color(0.85f, 0.55f, 0.18f), 0.85f, 0.8f);
                        Destroy(egg.GetComponent<Collider>());
                        Kit.PointLight(egg.transform, _eggPos + Vector3.up, new Color(1f, 0.55f, 0.2f), 60f, 2.5e6f, "EggGlow");
                        Track(egg);
                        Objective = rim;
                        EnsureGrade(new Color(0.9f, 0.95f, 1.05f), -12f, 0f, Color.white);
                        _gradeTarget = 0.6f;
                    }
                    break;

                case StageEnd:
                    QuestTitle = "Epilogue"; QuestHint = "The story ends here. Explore freely.";
                    Objective = null;
                    BuildEndingScene(Ending == 0 ? 1 : Ending);
                    break;
            }
        }

        // ------------------------------------------------------------ waves

        private void SpawnMobWave()
        {
            _wave++;
            int n = 4 + _wave * 2;
            Vector3 o = PitArena.Origin;
            for (int i = 0; i < n; i++)
            {
                float a = (200f + i * (150f / n) + _wave * 20f) * Mathf.Deg2Rad;
                Vector3 p = o + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 19f + Vector3.up * 0.3f;
                Hostile(p, "Rioter", new Color(0.35f, 0.28f, 0.2f), new Color(0.25f, 0.2f, 0.14f), 45f, 3.3f, 10f, 40f, i);
            }
        }

        private void SpawnColdWave(int n)
        {
            _wave++;
            WorldLayout lay = WorldLayout.Load();
            Vector3 c = Player.transform.position;
            Vector3 sea = lay.SeaDir;
            for (int i = 0; i < n; i++)
            {
                float a = ((i - (n - 1) * 0.5f) * 22f) * Mathf.Deg2Rad;
                Vector3 dir = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * sea;
                Vector3 p = c + dir * (30f + (i % 3) * 5f);
                p.y = Kit.GroundY(p) + 0.4f;
                if (p.y < ThirdPersonPlayer.SeaLevel + 0.5f) p.y = ThirdPersonPlayer.SeaLevel + 0.9f;
                Hostile(p, "Cold Dead", new Color(0.55f, 0.68f, 0.8f), new Color(0.35f, 0.45f, 0.55f), 70f, 3.7f, 14f, 60f, i);
            }
        }

        private void EnsureGrade(Color filter, float sat, float fog, Color fogColor)
        {
            if (_grade != null) Destroy(_grade.gameObject);
            _grade = Kit.GlobalGrade(transform, filter, sat, fog, fogColor);
            _grade.weight = 0f;
        }

        private void EnsureSnow()
        {
            if (_snow != null) return;
            _snow = new GameObject("Snow");
            ParticleSystem ps = _snow.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = Effects.Additive;
            var main = ps.main;
            main.loop = true; main.startLifetime = 7f; main.startSize = 0.18f; main.startSpeed = 0f;
            main.startColor = new Color(0.9f, 0.95f, 1f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1800;
            var em = ps.emission; em.rateOverTime = 260f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(90f, 1f, 90f);
            var vel = ps.velocityOverLifetime; vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f); vel.y = new ParticleSystem.MinMaxCurve(-4f, -2.5f); vel.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        }

        private void FadeSnow(bool on)
        {
            if (_snow == null) return;
            ParticleSystem ps = _snow.GetComponent<ParticleSystem>();
            var em = ps.emission; em.enabled = on;
            if (!on) { Destroy(_snow, 8f); _snow = null; }
        }

        // ------------------------------------------------------------ per-frame chapter logic

        private void UpdateChapter(float dt)
        {
            _subTime += dt;
            // Cold or ember grade fades in and out.
            if (_grade != null) _grade.weight = Mathf.MoveTowards(_grade.weight, _gradeTarget, dt * 0.25f);
            if (_snow != null && Camera.main != null) _snow.transform.position = Camera.main.transform.position + Vector3.up * 22f;
            if (_stage != StageTide && _snow != null && _stage != StageEgg) FadeSnow(false);

            // The rival tracks whoever is in the air.
            if (Enemy != null && Enemy.gameObject.activeSelf && Enemy.Health != null && !Enemy.Health.Dead)
                Enemy.Target = Dragon.Mounted ? Dragon.transform : Player.transform;

            switch (_stage)
            {
                case StagePit:
                    {
                        int alive = AliveSoldiers();
                        Vector3? target = null; float best = 1e9f;
                        for (int i = 0; i < Soldiers.Count; i++)
                        {
                            Soldier s = Soldiers[i];
                            if (s == null || s.Health.Dead) continue;
                            float d = (s.transform.position - Player.transform.position).sqrMagnitude;
                            if (d < best) { best = d; target = s.transform.position; }
                        }
                        Objective = alive > 0 ? target : (_beastObj != null ? _beastObj.transform.position : _pit.Bottom);
                        QuestHint = alive > 0 ? "Cut down the jailers (" + alive + " left). Parry their blows." : "Approach the Beast. Hold the torch steady.";
                        float flat = Vector2.Distance(new Vector2(Player.transform.position.x, Player.transform.position.z), new Vector2(_pit.BeastPos.x, _pit.BeastPos.z));
                        float dy = Mathf.Abs(Player.transform.position.y - _pit.Bottom.y);
                        if (_sub == 0 && alive == 0 && flat < 18f && dy < 5f) { _sub = 1; StartBeastDialogue(); }
                        if (_sub == 2 && _subTime > 3.5f) BeginQuest(StageCharge);
                    }
                    break;

                case StageCharge:
                    {
                        int left = TowersStanding;
                        Vector3? obj = null; float best = 1e9f;
                        for (int i = 0; i < _towers.Count; i++)
                        {
                            if (_towers[i] == null || _towers[i].Fallen) continue;
                            float d = (_towers[i].transform.position - Player.transform.position).sqrMagnitude;
                            if (d < best) { best = d; obj = _towers[i].transform.position + Vector3.up * 6f; }
                        }
                        Objective = obj;
                        QuestHint = left > 0 ? "Burn the siege towers: " + (_towers.Count - left) + " of " + _towers.Count + (Dragon.Mounted ? "" : "   Mount your wyrm (" + Bindings.Short(GameAction.Interact) + ")") : "The siege line is ash.";
                        if (left == 0 && _sub == 0) { _sub = 1; _subTime = 0f; Say("The towers fall. The charge is broken.", 4f); AudioDirector.Play(Sfx.Fanfare, Vector3.zero, 0.7f); }
                        if (_sub == 1 && _subTime > 4f) BeginQuest(StageDuel);
                    }
                    break;

                case StageDuel:
                    {
                        bool dead = Enemy != null && Enemy.Health != null && Enemy.Health.Dead;
                        Objective = (!dead && Enemy != null && Enemy.gameObject.activeSelf) ? Enemy.transform.position : (Vector3?)null;
                        QuestHint = Dragon.Mounted ? "Bring down Old Vael. Keep moving; its fire is aimed." : "Mount your wyrm (" + Bindings.Short(GameAction.Interact) + ")";
                        if (dead && _sub == 0 && _subTime > 1.2f) { _sub = 1; StartOrrinDialogue(); }
                    }
                    break;

                case StageRiot:
                    {
                        if (_tamsin != null)
                        {
                            _tamsinAcc += TamsinPressure() * dt;
                            if (_tamsinAcc >= 5f && !_tamsin.Dead) { _tamsin.Hurt(_tamsinAcc, null); _tamsinAcc = 0f; }
                            if (_tamsin.Dead && _sub == 0) { Say("Tamsin is down.", 3f); _sub = 3; }
                        }
                        int alive = AliveSoldiers();
                        QuestHint = alive > 0 ? "Hold the pens (" + alive + " left, wave " + _wave + " of 3)." : "The gate holds, for now.";
                        if (alive == 0 && !InDialogue)
                        {
                            if (_wave < 3) { if (_subTime > 3f) { SpawnMobWave(); _subTime = 0f; } }
                            else if (_sub == 0 || _sub == 3) { _sub = _sub == 3 ? 4 : 1; StartRiotDialogue(); }
                        }
                        if (_sub == 2 && _subTime > 3.5f) BeginQuest(StageTide);
                    }
                    break;

                case StageTide:
                    {
                        int alive = AliveSoldiers();
                        if (_sub == 0)
                        {
                            QuestHint = alive > 0 ? "Hold the harbour (" + alive + " of the Cold Dead left)." : "The second wave comes.";
                            if (alive == 0 && _wave < 2) { if (_subTime > 3f) { SpawnColdWave(8); _subTime = 0f; } }
                            else if (alive == 0 && _wave >= 2) { _sub = 1; _subTime = 0f; }
                        }
                        else if (_sub == 1)
                        {
                            QuestTitle = "The Pale Tide"; QuestHint = Dragon.Mounted ? "The Winter Wyrm comes out of the cloud. Burn it." : "Mount your wyrm (" + Bindings.Short(GameAction.Interact) + ")";
                            if (_subTime > 3f && Enemy != null && !_bossSpawned)
                            {
                                _bossSpawned = true;
                                Enemy.gameObject.SetActive(true);
                                Enemy.transform.position = Dragon.transform.position + new Vector3(260f, 150f, -180f);
                                Enemy.Init(Dragon.Mounted ? Dragon.transform : Player.transform);
                                Enemy.Configure(1100f, "The Winter Wyrm", 1.2f, 160f, 44f, 2.8f, 4.6f, 24f, new Color(0.7f, 0.95f, 1.5f), new Color(0.5f, 0.85f, 1f));
                                Say("The cold has a dragon of its own.", 4f);
                            }
                            bool dead = _bossSpawned && Enemy != null && Enemy.Health != null && Enemy.Health.Dead;
                            Objective = (!dead && Enemy != null && Enemy.gameObject.activeSelf) ? Enemy.transform.position : (Vector3?)null;
                            if (dead && _subTime > 4f) { _sub = 2; StartTideDialogue(); }
                        }
                        if (_sub == 3 && _subTime > 3.5f) BeginQuest(StageEgg);
                    }
                    break;

                case StageEgg:
                    {
                        Objective = _eggPos + Vector3.up * 3f;
                        float d = Vector3.Distance(Player.transform.position, _eggPos);
                        if (_sub == 0 && d < 60f) { _sub = 1; StartEggDialogue(); }
                    }
                    break;

                case StageEnd:
                    {
                        Objective = null;
                        if (EndingShown)
                        {
                            if (Rig != null)
                            {
                                WorldLayout lay = WorldLayout.Load();
                                float a = Time.unscaledTime * 0.06f;
                                Vector3 c = lay.City;
                                Vector3 p = c + new Vector3(Mathf.Sin(a) * 300f, 130f, Mathf.Cos(a) * 300f);
                                p.y = Mathf.Max(p.y, Kit.GroundY(p) + 40f);
                                Rig.Frozen = true;
                                Rig.transform.position = p;
                                Rig.transform.rotation = Quaternion.LookRotation(c + Vector3.up * 30f - p, Vector3.up);
                            }
                            if (InputState.Interact || Input_Continue())
                            {
                                EndingShown = false;
                                if (Rig != null) { Rig.Frozen = false; Rig.Flight = Dragon.Mounted; }
                            }
                        }
                    }
                    break;
            }
        }

        private static bool Input_Continue()
        {
            UnityEngine.InputSystem.Keyboard k = UnityEngine.InputSystem.Keyboard.current;
            UnityEngine.InputSystem.Gamepad g = UnityEngine.InputSystem.Gamepad.current;
            return (k != null && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame)) || (g != null && g.buttonSouth.wasPressedThisFrame);
        }

        private float TamsinPressure()
        {
            if (_tamsin == null || _tamsin.Dead) return 0f;
            float p = 0f;
            for (int i = 0; i < Soldiers.Count; i++)
            {
                Soldier s = Soldiers[i];
                if (s == null || s.Health.Dead) continue;
                if ((s.transform.position - _tamsin.transform.position).sqrMagnitude < 2.6f * 2.6f) p += 7f;
            }
            return p;
        }

        // ------------------------------------------------------------ dialogue beats

        private void OpenDialogue(string speaker, string line, List<DialogueOption> options, float seconds, Action<int> after)
        {
            InDialogue = true;
            Speaker = speaker; Line = line;
            Options = options;
            DialogueDuration = seconds;
            DialogueTimer = seconds;
            _afterDialogue = after;
            AudioDirector.Ui();
        }

        private void StartBeastDialogue()
        {
            OpenDialogue("The Beast of the Pit",
                "It does not speak. It watches the torch, the chain and you. Beside it your own wyrm strains against iron, and the jailers' keys hang from the post. Whatever you do now it will remember.",
                new List<DialogueOption>
                {
                    new DialogueOption { Tone = Tone.Diplomatic, Text = "Lower the torch. Offer your open hand", Effect = d => { d.Rep.Honour += 3; } },
                    new DialogueOption { Tone = Tone.Aggressive, Text = "Drive it back with the torch and take the keys", Effect = d => { d.Rep.Fear += 3; } },
                    new DialogueOption { Tone = Tone.Deceptive, Text = "Slip the keys and loose both chains quietly", Effect = d => { d.Rep.Cunning += 3; } },
                }, 40f,
                i =>
                {
                    PitChoice = i + 1;
                    _sub = 2; _subTime = 0f;
                    if (_beastObj != null)
                    {
                        AmbientDragon ad = _beastObj.GetComponent<AmbientDragon>();
                        if (ad != null && PitChoice != 2) ad.Orbiting = false;
                    }
                    Say(i == 0 ? "The Beast lowers its head. Your wyrm is free." : i == 1 ? "The Beast cowers. Your wyrm is free, and wary." : "The chains fall silent. Neither dragon moves until you do.", 5f);
                    AudioDirector.Play(Sfx.Fanfare, Vector3.zero, 0.7f);
                });
        }

        private void StartOrrinDialogue()
        {
            OpenDialogue("Tamsin",
                "Old Vael is down in the lake, my lord. But his last fire took the maester's tower with Orrin in it. The whole city saw the smoke. What do we tell them?",
                new List<DialogueOption>
                {
                    new DialogueOption { Tone = Tone.Diplomatic, Text = "The truth, and a vigil for him", Effect = d => { d.Rep.Honour += 2; } },
                    new DialogueOption { Tone = Tone.Aggressive, Text = "That Aemric did it. Let the city want blood", Effect = d => { d.Rep.Fear += 2; } },
                    new DialogueOption { Tone = Tone.Deceptive, Text = "That he died at his books. A quiet end", Effect = d => { d.Rep.Cunning += 2; } },
                }, 40f,
                i =>
                {
                    DuelChoice = i + 1;
                    Say("Chapter 7 complete: Over the Gods Lake", 6f);
                    BeginQuest(StageRiot);
                });
        }

        private void StartRiotDialogue()
        {
            bool alive = _tamsin != null && !_tamsin.Dead;
            OpenDialogue("The pen-keeper",
                "The last of them are down, but the gate is cracked and the young dragons are loose in the dark. " + (alive ? "Tamsin is alive behind you." : "Tamsin did not make it out of the crush.") + " One choice, quickly.",
                new List<DialogueOption>
                {
                    new DialogueOption { Tone = Tone.Diplomatic, Text = "Hold the gate and talk the city down", Effect = d => { d.Rep.Honour += 2; } },
                    new DialogueOption { Tone = Tone.Aggressive, Text = "Burn the gate shut. Let them fear it", Effect = d => { d.Rep.Fear += 3; } },
                    new DialogueOption { Tone = Tone.Deceptive, Text = "Slip out through the old drain and let it fall", Effect = d => { d.Rep.Cunning += 2; } },
                }, 40f,
                i =>
                {
                    RiotChoice = i + 1;
                    TamsinSaved = _tamsin != null && !_tamsin.Dead;
                    _sub = 2; _subTime = 0f;
                    Say(TamsinSaved ? "Tamsin lives. The pens hold." : "The pens hold. Tamsin is lost.", 5f);
                });
        }

        private void StartTideDialogue()
        {
            OpenDialogue("The cold, speaking through the ice",
                "The wyrm is down, and the tide is still. In the frozen quay a voice with no breath says: the living burn what they cannot keep. Offer the strait a bargain, or a fire.",
                new List<DialogueOption>
                {
                    new DialogueOption { Tone = Tone.Diplomatic, Text = "Offer a truce: the strait for the hatchlings", Effect = d => { d.Rep.Honour += 2; } },
                    new DialogueOption { Tone = Tone.Aggressive, Text = "Burn the quay to the waterline", Effect = d => { d.Rep.Fear += 2; } },
                    new DialogueOption { Tone = Tone.Deceptive, Text = "Promise what you will not give", Effect = d => { d.Rep.Cunning += 2; } },
                }, 40f,
                i =>
                {
                    TideChoice = i + 1;
                    _sub = 3; _subTime = 0f;
                    FadeSnow(false);
                    _gradeTarget = 0f;
                    Say("The Pale Tide recedes. For now.", 5f);
                    AudioDirector.Play(Sfx.Fanfare, Vector3.zero, 0.8f);
                });
        }

        public const int EndingGate = 3;

        private void StartEggDialogue()
        {
            Func<Director, bool> honour = d => d.Rep.Honour >= EndingGate;
            Func<Director, bool> fear = d => d.Rep.Fear >= EndingGate;
            Func<Director, bool> cunning = d => d.Rep.Cunning >= EndingGate;
            OpenDialogue("The Last Egg",
                "Warm under your hand, and it hums in the bond. The pens will not survive the winter on their own, and the old blood is thin. Whatever hatches from this will answer to what you have become.",
                new List<DialogueOption>
                {
                    new DialogueOption { Tone = Tone.Diplomatic, Text = "Hearthfire: carry it home and keep the realm warm", Gate = honour, GateText = "needs Honour " + EndingGate },
                    new DialogueOption { Tone = Tone.Aggressive, Text = "Wildfire: let it hatch in the caldera, and let it burn", Gate = fear, GateText = "needs Fear " + EndingGate },
                    new DialogueOption { Tone = Tone.Deceptive, Text = "Unbound: free every dragon, and bind none", Gate = cunning, GateText = "needs Cunning " + EndingGate },
                }, 120f,
                i => FinishEnding(i + 1));
        }

        // ------------------------------------------------------------ endings

        private static string[] EndingNames = { "", "Hearthfire", "Wildfire", "Unbound" };

        private void FinishEnding(int kind)
        {
            Ending = kind;
            switch (kind)
            {
                case 1:
                    EndingTitle = "HEARTHFIRE";
                    EndingText = "You carried the egg down through the snow to the keep and lit every hearth in Hearthmoor from its warmth. The pens were rebuilt. The city kept its dragons, and its dragons kept the city.";
                    break;
                case 2:
                    EndingTitle = "WILDFIRE";
                    EndingText = "The egg hatched in the caldera and the sky went red. You burned the Pale Tide back to the sea and Hearthmoor with it. What is left of the realm is yours, and it is ash, and it is afraid.";
                    break;
                default:
                    EndingTitle = "UNBOUND";
                    EndingText = "You opened every pen and cut every chain. The dragons rose over Hearthmoor and scattered across the strait. Some stayed. The ones that did, stayed because they wished to.";
                    break;
            }
            string tam = TamsinSaved ? "Tamsin lived." : "Tamsin did not.";
            EndingStats = "Honour " + Rep.Honour + "    Fear " + Rep.Fear + "    Cunning " + Rep.Cunning + "     " + tam + "     Deaths " + Deaths + "     Time " + Mathf.FloorToInt(PlaySeconds / 60f) + " min";
            EndingShown = true;
            AudioDirector.Play(Sfx.Fanfare, Vector3.zero, 1f);
            BeginQuest(StageEnd);
            EndingShown = true;
        }

        private void BuildEndingScene(int kind)
        {
            WorldLayout lay = WorldLayout.Load();
            if (_snow != null) FadeSnow(false);
            if (kind == 1)
            {
                EnsureGrade(new Color(1.08f, 0.96f, 0.84f), 8f, 0f, Color.white);
                _gradeTarget = 1f;
                for (int i = 0; i < 3; i++) SpawnOverheadDragon(lay.City, 110f + i * 25f, 85f + i * 10f, 0.1f + i * 0.02f, new Color(0.9f + i * 0.05f, 0.85f, 0.8f), 1.0f);
            }
            else if (kind == 2)
            {
                EnsureGrade(new Color(1.1f, 0.62f, 0.5f), -25f, 700f, new Color(0.28f, 0.14f, 0.1f));
                _gradeTarget = 1f;
                System.Random rnd = new System.Random(77);
                for (int i = 0; i < 14; i++)
                {
                    float a = (float)rnd.NextDouble() * 6.28f, r = 15f + (float)rnd.NextDouble() * 140f;
                    Vector3 p = lay.City + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                    p.y = Kit.GroundY(p) + 1f;
                    SpawnFire(p);
                }
                SpawnOverheadDragon(lay.City, 90f, 55f, 0.18f, new Color(1.2f, 0.7f, 0.6f), 1.5f);
            }
            else
            {
                EnsureGrade(new Color(0.92f, 1f, 1.1f), 10f, 0f, Color.white);
                _gradeTarget = 1f;
                Color[] tints = { new Color(1f, 0.8f, 0.8f), new Color(0.8f, 1f, 0.9f), new Color(0.85f, 0.85f, 1.1f), new Color(1.1f, 1f, 0.8f) };
                for (int i = 0; i < 8; i++) SpawnOverheadDragon(lay.City, 150f + (i % 3) * 40f, 120f + i * 8f, 0.09f + i * 0.012f, tints[i % tints.Length], 0.8f + (i % 3) * 0.15f);
            }
        }

        private void SpawnOverheadDragon(Vector3 centre, float radius, float height, float speed, Color tint, float scale)
        {
            if (!DragonModels.Available) return;
            GameObject g = DragonModels.Spawn(centre + new Vector3(radius, height, 0f), Quaternion.identity, scale, tint, "EndingDragon");
            if (g == null) return;
            AmbientDragon ad = g.AddComponent<AmbientDragon>();
            ad.Orbiting = true; ad.Centre = centre; ad.Radius = radius; ad.Height = height; ad.Speed = speed;
            Track(g);
        }

        private void SpawnFire(Vector3 p)
        {
            GameObject g = new GameObject("CityFire");
            g.transform.position = p;
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = Effects.Additive;
            var main = ps.main;
            main.loop = true; main.startLifetime = 2.2f; main.startSize = 6f; main.startSpeed = 4f;
            main.startColor = new Color(1f, 0.5f, 0.12f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 14f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 2.2f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0f), new GradientColorKey(new Color(0.25f, 0.08f, 0.04f), 1f) },
                       new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            Kit.PointLight(g.transform, p + Vector3.up * 4f, new Color(1f, 0.5f, 0.2f), 40f, 400000f, "FireLight");
            Track(g);
        }
    }
}
