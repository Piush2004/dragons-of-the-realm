import os

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
        assert s.count(a) == 1, (rel, a[:80], s.count(a))
        s = s.replace(a, b)
    wr(rel, s, crlf)


# HDRP: enable dynamic resolution on every pipeline asset (upscaled with a sharpening filter).
patch('Assets/Editor/IslandBuilder.cs', [
    ('''                asset.currentPlatformRenderPipelineSettings = s;''',
     '''                GlobalDynamicResolutionSettings dr = s.dynamicResolutionSettings;
                dr.enabled = true;
                dr.dynResType = DynamicResolutionType.Software;
                dr.upsampleFilter = DynamicResUpscaleFilter.CatmullRom;
                dr.minPercentage = 40f;
                dr.maxPercentage = 100f;
                s.dynamicResolutionSettings = dr;
                asset.currentPlatformRenderPipelineSettings = s;'''),
])

# Trees and rocks get colliders so the player, soldiers and the dragon cannot pass through them.
patch('Assets/Editor/VegetationBuilder.cs', [
    ('''            GameObject unpacked = inst;
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);''',
     '''            GameObject unpacked = inst;
            if (fbxName.StartsWith("pine") || fbxName.StartsWith("oak"))
            {
                CapsuleCollider trunk = unpacked.AddComponent<CapsuleCollider>();
                trunk.radius = 0.45f; trunk.height = fbxName.StartsWith("pine") ? 16f : 5f;
                trunk.center = new Vector3(0f, trunk.height * 0.5f, 0f);
            }
            else if (fbxName.StartsWith("rock"))
            {
                foreach (MeshFilter mf in unpacked.GetComponentsInChildren<MeshFilter>())
                    if (mf.GetComponent<MeshCollider>() == null) mf.gameObject.AddComponent<MeshCollider>();
            }
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);'''),
])

# Bootstrap: props near the spawn, fixed physics settings.
patch('Assets/Scripts/Game/BootstrapAndAutoplay.cs', [
    ('''            ThirdPersonPlayer player = ThirdPersonPlayer.Create(spawn);''',
     '''            ThirdPersonPlayer player = ThirdPersonPlayer.Create(spawn);
            Vector3 inland = -lay.SeaDir;
            Props.SpawnMarket(spawn + inland * 14f + Vector3.Cross(Vector3.up, inland) * -3f, inland);'''),
])

# Autoplay: measure a 60 fps distribution and exercise the physics.
s, crlf = rd('Assets/Scripts/Game/BootstrapAndAutoplay.cs')
i = s.index('    /// <summary>Plays a scripted session')
head = s[:i]
tail = '''    /// <summary>Plays a scripted session, saves screenshots and measures frame rate and physics, so it can be checked unattended.</summary>
    public sealed class AutoPlay : MonoBehaviour
    {
        private float _t;
        private int _step;
        private Director _d;
        private string _dir;
        private readonly List<float> _ft = new List<float>(4096);
        private float _nextSwing;
        private float _bucket; private int _bucketFrames;
        private Vector3[] _propsBefore;
        private int _crashStart;

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

        private static float Pct(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            return sorted[Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
        }

        private void LateUpdate()
        {
            float dtf = Time.unscaledDeltaTime;
            _t += dtf;
            if (_t > 8f && dtf < 0.5f) _ft.Add(dtf * 1000f);
            if (dtf > 0.5f) Debug.Log("[Emberbond] STALL " + dtf.ToString("0.0") + " s at t=" + _t.ToString("0.0") + " step=" + _step);
            _bucket += dtf; _bucketFrames++;
            if (_bucket >= 5f)
            {
                PerformanceScaler ps = FindFirstObjectByType<PerformanceScaler>();
                Debug.Log("[Emberbond] perf t=" + _t.ToString("0") + " fps=" + (_bucketFrames / _bucket).ToString("0") + " scale=" + (ps != null ? ps.Scale.ToString("0.00") : "-") + " step=" + _step);
                _bucket = 0f; _bucketFrames = 0;
            }

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
                case 4:
                    if (_t > 22f)
                    {
                        Shot("04_aftermath");
                        // Blast the crates: they must move.
                        _propsBefore = Props.Snapshot();
                        Vector3 p = _d.Player.transform.position;
                        Vector3 toward = Props.All.Count > 0 && Props.All[0] != null ? (Props.All[0].position - p) : Vector3.forward;
                        toward.y = 0f; toward.Normalize();
                        Fireball.Spawn(p + Vector3.up * 1.5f + toward * 2f, toward * 60f, _d.Player.gameObject, 40f);
                        _step++;
                    }
                    break;
                case 5:
                    if (_t > 25f)
                    {
                        Debug.Log("[Emberbond] PHYSICS crates=" + Props.All.Count + " movedByBlast=" + Props.CountMoved(_propsBefore, 0.5f));
                        _d.BeginQuestPublic(1);
                        WorldLayout lay = WorldLayout.Load();
                        _d.Dragon.transform.position = _d.Player.transform.position + new Vector3(8f, 0f, 0f);
                        _d.Dragon.Mount();
                        _step++;
                    }
                    break;
                case 6:
                    if (_t > 28f)
                    {
                        // Fly low and fast straight into the city wall: the dragon must be stopped and take a crash.
                        WorldLayout lay = WorldLayout.Load();
                        Vector3 start = lay.City + lay.SeaDir * 330f;
                        Terrain t = Terrain.activeTerrain;
                        float gy = t != null ? t.SampleHeight(start) + t.transform.position.y : lay.City.y;
                        _d.Dragon.transform.position = new Vector3(start.x, gy + 12f, start.z);
                        _d.Dragon.Aim(-lay.SeaDir);
                        _crashStart = _d.Dragon.Crashes;
                        _step++;
                    }
                    break;
                case 7:
                    if (_t > 36f)
                    {
                        WorldLayout lay = WorldLayout.Load();
                        Vector3 pos = _d.Dragon.transform.position;
                        float fromCentre = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(lay.City.x, 0f, lay.City.z));
                        Debug.Log("[Emberbond] PHYSICS dragon crashes=" + (_d.Dragon.Crashes - _crashStart) + " lastImpact=" + _d.Dragon.LastImpact.ToString("0") +
                                  " distFromCentre=" + fromCentre.ToString("0") + " dragonHealth=" + _d.Dragon.Health.Health.ToString("0"));
                        Shot("05_wall_crash");
                        _d.Dragon.transform.position = lay.City + Vector3.up * 200f + lay.SeaDir * 250f;
                        _d.Dragon.Aim(Vector3.forward);
                        InputState.ScriptedLook = new Vector2(0.4f, -0.1f);
                        _step++;
                    }
                    break;
                case 8: if (_t > 40f) { Shot("06_flight"); InputState.ScriptedFire = true; _step++; } break;
                case 9: if (_t > 41.5f) { Shot("07_fire"); InputState.ScriptedFire = false; _d.BeginQuestPublic(3); _step++; } break;
                case 10: if (_t > 52f) { Shot("08_rival"); _step++; } break;
                case 11:
                    if (_t > 54f)
                    {
                        _ft.Sort();
                        int over = 0;
                        for (int i = 0; i < _ft.Count; i++) if (_ft[i] > 17.5f) over++;
                        PerformanceScaler ps = FindFirstObjectByType<PerformanceScaler>();
                        Debug.Log("[Emberbond] autoplay done frames=" + _ft.Count + " p50=" + Pct(_ft, 0.5f).ToString("0.0") + " ms p95=" + Pct(_ft, 0.95f).ToString("0.0") +
                                  " ms p99=" + Pct(_ft, 0.99f).ToString("0.0") + " ms under60=" + (100f * (1f - over / (float)Mathf.Max(1, _ft.Count))).ToString("0") + "% scale=" +
                                  (ps != null ? ps.Scale.ToString("0.00") : "-") + " playerHealth=" + _d.Player.Health.Health.ToString("0"));
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
head = head.replace('using System;\nusing System.IO;', 'using System;\nusing System.Collections.Generic;\nusing System.IO;')
wr('Assets/Scripts/Game/BootstrapAndAutoplay.cs', head + tail, crlf)
print('perf patch ok')
