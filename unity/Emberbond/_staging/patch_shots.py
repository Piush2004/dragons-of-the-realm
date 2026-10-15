import io
base = r'D:\Projects\Games\claude\unity\Emberbond\Assets\Scripts\Game\\'

def rd(n): return io.open(base + n, encoding='utf-8').read()
def wr(n, s): io.open(base + n, 'w', encoding='utf-8', newline='').write(s)
def rep(s, old, new):
    if old not in s: raise SystemExit('missing: ' + old[:90])
    return s.replace(old, new, 1)

s = rd('SoakAndShots.cs')
s = s.rstrip()
assert s.endswith("}")
s = s[:-1].rstrip() + '''

    /// <summary>Jumps through each later chapter and takes a screenshot, so they can be looked at without playing to them.</summary>
    public sealed class ChapterShots : MonoBehaviour
    {
        private Director _d;

        private void Start()
        {
            _d = GetComponent<Director>();
            InputState.Scripted = true;
            SaveSystem.PathOverride = Path.Combine(Application.temporaryCachePath, "chaptershots_save.json");
            StartCoroutine(Run());
        }

        private static void Shot(string name)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "chapters");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            Debug.Log("[SHOT] " + name);
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(4f);
            _d.ChooseOption(0);
            yield return new WaitForSecondsRealtime(1f);
            ThirdPersonPlayer pl = _d.Player;

            _d.BeginQuestPublic(Director.StagePit);
            yield return new WaitForSecondsRealtime(3.5f);
            Shot("10a_pit_top");
            yield return new WaitForSecondsRealtime(0.5f);
            pl.Teleport(_d.PitFloor + new Vector3(8f, 0.3f, 8f));
            _d.Rig.Yaw = 225f;
            yield return new WaitForSecondsRealtime(3f);
            Shot("10b_pit_bottom");
            yield return new WaitForSecondsRealtime(0.5f);

            _d.BeginQuestPublic(Director.StageCharge);
            yield return new WaitForSecondsRealtime(4f);
            Shot("11a_charge_start");
            SiegeTower t = _d.TowerAt(0);
            if (t != null)
            {
                pl.Teleport(t.transform.position + new Vector3(-26f, 1f, -10f));
                _d.Rig.Yaw = 60f;
            }
            yield return new WaitForSecondsRealtime(3f);
            Shot("11b_tower");
            if (t != null) t.Health.Hurt(1000f, pl.gameObject);
            yield return new WaitForSecondsRealtime(1.6f);
            Shot("11c_tower_falls");

            _d.BeginQuestPublic(Director.StageDuel);
            yield return new WaitForSecondsRealtime(1.5f);
            _d.Dragon.Mount();
            yield return new WaitForSecondsRealtime(5f);
            Shot("12_duel");

            _d.BeginQuestPublic(Director.StageRiot);
            yield return new WaitForSecondsRealtime(5f);
            Shot("13_riot");

            _d.BeginQuestPublic(Director.StageTide);
            yield return new WaitForSecondsRealtime(9f);
            Shot("14_tide");

            _d.BeginQuestPublic(Director.StageEgg);
            yield return new WaitForSecondsRealtime(1.5f);
            pl.Teleport(_d.EggPosition + new Vector3(0f, 1f, 9f));
            yield return new WaitForSecondsRealtime(2.5f);
            Shot("15_egg");
            _d.Rep.Honour = 3; _d.Rep.Fear = 3; _d.Rep.Cunning = 3;
            yield return new WaitForSecondsRealtime(0.5f);
            _d.ChooseOption(1);
            yield return new WaitForSecondsRealtime(4f);
            Shot("16_ending_wildfire");
            _d.DebugFinishEnding(1);
            yield return new WaitForSecondsRealtime(4f);
            Shot("16_ending_hearthfire");
            _d.DebugFinishEnding(3);
            yield return new WaitForSecondsRealtime(4f);
            Shot("16_ending_unbound");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(0);
        }
    }
}
'''
wr('SoakAndShots.cs', s)

s = rd('BootstrapAndAutoplay.cs')
s = rep(s, '''bool headless = Has("-autoplay") || Has("-selftest") || Has("-nomenu") || Has("-soak");''',
           '''bool headless = Has("-autoplay") || Has("-selftest") || Has("-nomenu") || Has("-soak") || Has("-chaptershots");''')
s = rep(s, '''            if (Has("-menushots")) dir.AddComponent<MenuShots>();''',
           '''            if (Has("-menushots")) dir.AddComponent<MenuShots>();
            if (Has("-chaptershots")) dir.AddComponent<ChapterShots>();''')
wr('BootstrapAndAutoplay.cs', s)

s = rd('Menu.cs')
s = rep(s, '''GameBootstrap.Has("-menushots"));''', '''GameBootstrap.Has("-menushots") || GameBootstrap.Has("-chaptershots"));''')
wr('Menu.cs', s)
print('ok')
