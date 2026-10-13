import os

root = r'D:\Projects\Games\claude\unity\Emberbond'


def rd(rel):
    s = open(os.path.join(root, rel), encoding='utf-8', newline='').read()
    return s.replace('\r\n', '\n'), '\r\n' in s


def wr(rel, s, crlf):
    if crlf:
        s = s.replace('\n', '\r\n')
    open(os.path.join(root, rel), 'w', encoding='utf-8', newline='').write(s)


# City writes the layout the game reads at run time.
s, crlf = rd('Assets/Editor/CityBuilder.cs')
a = '''            Debug.Log("[Emberbond] Hearthmoor placed: "'''
assert s.count(a) == 1
s = s.replace(a, '''            WorldLayout layout = new WorldLayout { City = c, SeaYaw = seaYaw, Hall = HallBuilder.HallOrigin };
            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText("Assets/Resources/layout.json", JsonUtility.ToJson(layout));
            AssetDatabase.ImportAsset("Assets/Resources/layout.json");
''' + a)
wr('Assets/Editor/CityBuilder.cs', s, crlf)

s, crlf = rd('Assets/Editor/IslandBuilder.cs')
a = '''            CityBuilder.Place(terrain);'''
assert s.count(a) == 1
s = s.replace(a, a + '''
            HallBuilder.Place();''')
wr('Assets/Editor/IslandBuilder.cs', s, crlf)

# A scripted session that exercises the council, the street fight, mounting, flight, fire and the rival wyrm.
s, crlf = rd('Assets/Scripts/Game/BootstrapAndAutoplay.cs')
marker = '    /// <summary>Plays a short scripted session'
i = s.index(marker)
head = s[:i]
tail = '''    /// <summary>Plays a scripted session and saves screenshots, so the real game can be checked unattended.</summary>
    public sealed class AutoPlay : MonoBehaviour
    {
        private float _t;
        private int _step;
        private Director _d;
        private string _dir;
        private float _worst, _sum; private int _frames;
        private float _nextSwing;

        private void Start()
        {
            _d = GetComponent<Director>();
            _dir = Path.Combine(Directory.GetCurrentDirectory(), "play");
            Directory.CreateDirectory(_dir);
            InputState.Scripted = true;
            Debug.Log("[Emberbond] autoplay started");
        }

        private void Shot(string n)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, n + ".png"));
            Debug.Log("[Emberbond] shot " + n + " stage=" + _d.QuestTitle);
        }

        private void LateUpdate()
        {
            _t += Time.unscaledDeltaTime;
            if (_t > 3f) { _sum += Time.unscaledDeltaTime; _frames++; _worst = Mathf.Max(_worst, Time.unscaledDeltaTime); }

            // While the fight is on, keep swinging and raise a guard now and then.
            if (_step >= 2 && _step <= 5 && _t > _nextSwing)
            {
                _nextSwing = _t + 0.65f;
                if (Mathf.FloorToInt(_t) % 4 == 0) InputState.ScriptedBlock = true; else InputState.ScriptedAttack = true;
            }

            switch (_step)
            {
                case 0: if (_t > 5f) { Shot("01_hall"); _step++; } break;
                case 1: if (_t > 6f) { InputState.ScriptedChoice = 0; _step++; } break;
                case 2: if (_t > 9f) { Shot("02_soldiers"); _step++; } break;
                case 3: if (_t > 15f) { Shot("03_combat"); _step++; } break;
                case 4: if (_t > 22f) { Shot("04_aftermath"); _step++; } break;
                case 5:
                    if (_t > 24f)
                    {
                        _d.BeginQuestPublic(1);
                        _d.Dragon.transform.position = _d.Player.transform.position + new Vector3(8f, 0f, 0f);
                        _d.Dragon.Mount();
                        _step++;
                    }
                    break;
                case 6: if (_t > 28f) { InputState.ScriptedLook = new Vector2(0.4f, -0.1f); _step++; } break;
                case 7: if (_t > 31f) { Shot("05_flight"); InputState.ScriptedFire = true; _step++; } break;
                case 8: if (_t > 32.5f) { Shot("06_fire"); InputState.ScriptedFire = false; _d.BeginQuestPublic(3); _step++; } break;
                case 9: if (_t > 45f) { Shot("07_rival"); _step++; } break;
                case 10:
                    if (_t > 47f)
                    {
                        Debug.Log("[Emberbond] autoplay done frames=" + _frames + " avg=" + (_sum / Mathf.Max(1, _frames) * 1000f).ToString("0.0") +
                                  " ms worst=" + (_worst * 1000f).ToString("0.0") + " ms playerHealth=" + _d.Player.Health.Health.ToString("0"));
                        File.WriteAllText(Path.Combine(_dir, "done.txt"), "ok");
                        Application.Quit(0);
                        _step++;
                    }
                    break;
            }
        }
    }
}
'''
wr('Assets/Scripts/Game/BootstrapAndAutoplay.cs', head + tail, crlf)
print('hall patch 2 ok')
