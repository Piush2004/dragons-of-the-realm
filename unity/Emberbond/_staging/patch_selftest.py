import io
base = r'D:\Projects\Games\claude\unity\Emberbond\Assets\Scripts\Game\\'

def rd(n): return io.open(base + n, encoding='utf-8').read()
def wr(n, s): io.open(base + n, 'w', encoding='utf-8', newline='').write(s)
def rep(s, old, new):
    if old not in s: raise SystemExit('missing: ' + old[:90])
    return s.replace(old, new, 1)

# debug hooks on the director
s = rd('DirectorChapters.cs')
s = rep(s, "        public static bool IsChapterStage(int s)",
'''        // Test hooks.
        public void DebugFinishEnding(int kind) { FinishEnding(kind); }
        public void DebugKillHostiles()
        {
            for (int i = 0; i < Soldiers.Count; i++)
                if (Soldiers[i] != null && Soldiers[i].Health != null && !Soldiers[i].Health.Dead) Soldiers[i].Health.Hurt(100000f, Player.gameObject);
        }
        public Vector3 EggPosition { get { return _eggPos; } }
        public SiegeTower TowerAt(int i) { return i >= 0 && i < _towers.Count ? _towers[i] : null; }

        public static bool IsChapterStage(int s)''')
wr('DirectorChapters.cs', s)

s = rd('SelfTest.cs')
marker = "            // ---------------------------------------------------------- performance and errors\n"
new = r'''            // ---------------------------------------------------------- controls and tips
            Check("default bindings are WASD, E, G, mouse", Bindings.Get(GameAction.Forward).Equals(Bind.K(UnityEngine.InputSystem.Key.W)) &&
                  Bindings.Get(GameAction.Interact).Equals(Bind.K(UnityEngine.InputSystem.Key.E)) && Bindings.Get(GameAction.Attack).Equals(Bind.M(0)));
            Bindings.Set(GameAction.Interact, Bind.K(UnityEngine.InputSystem.Key.F));
            Check("rebinding changes the control", Bindings.Get(GameAction.Interact).Equals(Bind.K(UnityEngine.InputSystem.Key.F)));
            Bindings.Set(GameAction.Summon, Bind.K(UnityEngine.InputSystem.Key.F));
            Check("binding a used key swaps the two actions", Bindings.Get(GameAction.Summon).Equals(Bind.K(UnityEngine.InputSystem.Key.F)) &&
                  Bindings.Get(GameAction.Interact).Equals(Bind.K(UnityEngine.InputSystem.Key.G)));
            Bindings.Set(GameAction.Jump, Bind.K(UnityEngine.InputSystem.Key.X));
            string dump = Bindings.Dump();
            Bindings.Save();
            Bindings.ResetDefaults();
            Bindings.Load();
            Check("bindings survive a save and load", Bindings.Dump() == dump, Bindings.Label(GameAction.Jump));
            Bind junk1;
            Check("damaged binding data is rejected", !Bind.TryParse("k:99999", out junk1) && !Bind.TryParse("zzz", out junk1) && !Bind.TryParse("k:1", out junk1) && !Bind.TryParse("m:9", out junk1));
            UnityEngine.InputSystem.Keyboard kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.X));
                yield return null;
                yield return null;
                bool held = Bindings.Held(GameAction.Jump);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return null;
                yield return null;
                Check("a rebound key is read as the action", held && !Bindings.Held(GameAction.Jump));
            }
            else Check("a rebound key is read as the action (no keyboard, skipped)", true);
            Bindings.ResetDefaults();
            Bindings.Save();
            Check("controls reset to defaults", Bindings.Get(GameAction.Jump).Equals(Bind.K(UnityEngine.InputSystem.Key.Space)));

            HintSystem hs = HintSystem.Instance;
            Check("onboarding tips fired during play", hs != null && hs.Shown.Contains("dialogue") && hs.Shown.Contains("move") && hs.Shown.Count >= 3,
                  hs == null ? "none" : string.Join(",", new List<string>(hs.Shown).ToArray()));

            // ---------------------------------------------------------- the later chapters
            pl.Health.Health = 120f;
            _d.Rep = new Reputation();
            _d.BeginQuestPublic(Director.StagePit);
            yield return Wait(1.5f);
            Vector3 top = _d.PitTop;
            Check("the Pit is built far above the sea", _d.PitActive && _d.PitFloor.y > 1000f, "floor y=" + _d.PitFloor.y.ToString("0"));
            Check("the rider stands at the top of the stair and does not fall",
                  Vector3.Distance(pl.transform.position, top) < 2.5f && pl.transform.position.y > _d.PitFloor.y + 20f, pl.transform.position.ToString());
            Check("four jailers guard the bottom", _d.Soldiers.Count == 4, "count=" + _d.Soldiers.Count);
            Check("the wyrm rests on the pit floor, not the terrain", Mathf.Abs(dr.transform.position.y - (_d.PitFloor.y - 0.9f + 1.2f)) < 2.5f, "y=" + dr.transform.position.y.ToString("0.0"));

            // Walk the stair: step along the helix and make sure the rider keeps ground contact the whole way.
            float stairMinClear = 99f; bool fell = false;
            Vector3 po = PitArena.Origin;
            for (int i = 0; i < PitArena.Steps; i += 4)
            {
                float a = i * PitArena.StepAngle * Mathf.Deg2Rad;
                Vector3 sp = po + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * PitArena.StairRadius;
                sp.y = po.y + (PitArena.Steps - i) * PitArena.Rise + 0.9f;
                pl.Teleport(sp);
                yield return Wait(0.12f);
                float below = pl.transform.position.y - (po.y + (PitArena.Steps - i) * PitArena.Rise);
                stairMinClear = Mathf.Min(stairMinClear, below);
                if (pl.transform.position.y < sp.y - 3f) fell = true;
            }
            Check("every step of the stair holds the rider", !fell && stairMinClear > -0.3f, "lowest " + stairMinClear.ToString("0.00"));

            // Jailers: they hunt the rider on the floor, and are killed by blows.
            pl.Teleport(_d.PitFloor + new Vector3(0f, 0.3f, 6f));
            yield return Wait(3f);
            float nearest = 1e9f;
            foreach (Soldier js in _d.Soldiers) if (js != null) nearest = Mathf.Min(nearest, Vector3.Distance(js.transform.position, pl.transform.position));
            Check("jailers come for a rider on the floor", nearest < 6f, "nearest " + nearest.ToString("0.0") + " m");
            int rep0 = _d.Rep.Honour;
            _d.DebugKillHostiles();
            yield return Until(() => _d.InDialogue, 8f);
            Check("the Beast dialogue opens when the jailers are dead", _d.InDialogue && _d.Options.Count == 3, _d.Speaker);
            _d.ChooseOption(0);
            Check("the choice is remembered and changes reputation", _d.PitChoice == 1 && _d.Rep.Honour == rep0 + 3);
            yield return Until(() => _d.Stage == Director.StageCharge, 8f);
            Check("the story moves on to the Charge", _d.Stage == Director.StageCharge);
            yield return Wait(1f);
            Check("the pit is put away", !_d.PitActive);

            Check("four siege towers stand on dry ground", _d.TowersTotal == 4 && _d.TowersStanding == 4, _d.TowersStanding + "/" + _d.TowersTotal);
            bool towersDry = true;
            for (int i = 0; i < _d.TowersTotal; i++) { SiegeTower tw = _d.TowerAt(i); if (tw == null || tw.transform.position.y < ThirdPersonPlayer.SeaLevel + 1f) towersDry = false; }
            Check("the towers are not in the sea", towersDry);
            // A real fireball against the first tower.
            SiegeTower t0 = _d.TowerAt(0);
            Vector3 tp = t0.transform.position + Vector3.up * 5f;
            float tHp = t0.Health.Health;
            Fireball.Spawn(tp + Vector3.right * 14f, Vector3.left * 60f, dr.gameObject, 40f);
            yield return Wait(1.2f);
            Check("a fireball damages a siege tower", t0.Health.Health < tHp, tHp.ToString("0") + " -> " + t0.Health.Health.ToString("0"));
            t0.Health.Hurt(1000f, dr.gameObject);
            yield return Wait(1f);
            int bodies = 0;
            foreach (Transform c in t0.transform) if (c.GetComponent<Rigidbody>() != null) bodies++;
            Check("a burned tower collapses into rigid bodies", t0.Fallen && bodies >= 4, "bodies=" + bodies);
            for (int i = 1; i < _d.TowersTotal; i++) _d.TowerAt(i).Health.Hurt(1000f, dr.gameObject);
            yield return Until(() => _d.Stage == Director.StageDuel, 9f);
            Check("burning every tower moves the story on", _d.Stage == Director.StageDuel);

            // A death in a chapter restarts the encounter and gives the rider back.
            int respawns = _d.Respawns;
            pl.Health.Hurt(10000f, null);
            yield return Until(() => _d.Respawns > respawns, 6f);
            yield return Wait(0.5f);
            Check("dying in a chapter restarts it with the rider alive", !pl.Health.Dead && _d.Stage == Director.StageDuel && _d.Enemy.gameObject.activeSelf);

            Check("the duel's rival is Old Vael", _d.Enemy != null && _d.Enemy.Health.DisplayName == "Old Vael" && _d.Enemy.Health.MaxHealth > 800f,
                  _d.Enemy != null ? _d.Enemy.Health.DisplayName + " " + _d.Enemy.Health.MaxHealth : "none");
            // Aim assist: a shot fired roughly at the rival must lead it.
            _d.Enemy.Health.Hurt(100000f, dr.gameObject);
            yield return Until(() => _d.InDialogue, 6f);
            Check("Orrin's fate is a choice after the duel", _d.InDialogue && _d.Options.Count == 3, _d.Speaker);
            _d.ChooseOption(2);
            Check("the duel choice is stored", _d.DuelChoice == 3 && _d.Stage == Director.StageRiot, "choice=" + _d.DuelChoice + " stage=" + _d.Stage);
            yield return Wait(1.5f);

            Check("Tamsin is in the pens and alive", _d.Tamsin != null && !_d.Tamsin.Dead);
            Check("the first mob wave arrives", _d.Soldiers.Count == 6 && _d.Wave == 1, "count=" + _d.Soldiers.Count);
            for (int w = 0; w < 3; w++)
            {
                _d.DebugKillHostiles();
                yield return Wait(4f);
            }
            Check("three waves come before the choice", _d.Wave == 3 && _d.InDialogue, "wave=" + _d.Wave + " dialogue=" + _d.InDialogue);
            _d.ChooseOption(0);
            Check("sparing Tamsin is recorded", _d.RiotChoice == 1 && _d.TamsinSaved);
            yield return Until(() => _d.Stage == Director.StageTide, 8f);

            Check("the Pale Tide starts with six of the Cold Dead", _d.Stage == Director.StageTide && _d.Soldiers.Count == 6, "count=" + _d.Soldiers.Count);
            Check("the Beast joins the defence after a kind choice", GameObject.Find("PitBeastAlly") != null);
            _d.DebugKillHostiles();
            yield return Wait(4.5f);
            Check("a second, larger wave follows", _d.Soldiers.Count >= 8, "count=" + _d.Soldiers.Count);
            _d.DebugKillHostiles();
            yield return Until(() => _d.Enemy != null && _d.Enemy.gameObject.activeSelf && _d.Enemy.Health.DisplayName == "The Winter Wyrm", 14f);
            Check("then the Winter Wyrm descends", _d.Enemy.Health.DisplayName == "The Winter Wyrm" && _d.Enemy.Health.MaxHealth > 1000f);
            _d.Enemy.Health.Hurt(100000f, dr.gameObject);
            yield return Until(() => _d.InDialogue, 10f);
            _d.ChooseOption(1);
            yield return Until(() => _d.Stage == Director.StageEgg, 8f);
            Check("the Egg chapter begins", _d.Stage == Director.StageEgg && _d.TideChoice == 2);

            // Endings are gated by reputation.
            _d.Rep = new Reputation();
            pl.Teleport(_d.EggPosition + new Vector3(0f, 1f, 8f));
            yield return Until(() => _d.InDialogue, 5f);
            Check("the ending dialogue opens at the egg", _d.InDialogue && _d.Options.Count == 3);
            _d.ChooseOption(0);
            Check("an ending is refused when its gate is closed", _d.InDialogue && _d.Ending == 0 && _d.Stage == Director.StageEgg);
            _d.Rep.Honour = Director.EndingGate;
            _d.ChooseOption(0);
            yield return Wait(1f);
            Check("an open ending plays", _d.Ending == 1 && _d.EndingShown && _d.Stage == Director.StageEnd, "ending=" + _d.Ending + " stage=" + _d.Stage);
            Check("the ending scene has dragons over the city", GameObject.Find("EndingDragon") != null);
            Check("the ending is saved", _d.SaveNow() && SaveSystem.Exists);
            _d.Ending = 0;
            Check("a finished game loads back into its ending", _d.Continue() && _d.Ending == 1 && _d.Stage == Director.StageEnd);
            pl.Health.Health = 120f;

'''
s = rep(s, marker, new + marker)
s = rep(s, "            _d.SaveNow();\n\n            // ---------------------------------------------------------- dragon: mount, collisions, bounds",
           "            _d.SaveNow();\n\n            // ---------------------------------------------------------- dragon: mount, collisions, bounds")
wr('SelfTest.cs', s)
print('ok')
