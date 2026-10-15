import io
base = r'D:\Projects\Games\claude\unity\Emberbond\Assets\Scripts\Game\\'

def rd(n): return io.open(base + n, encoding='utf-8').read()
def wr(n, s): io.open(base + n, 'w', encoding='utf-8', newline='').write(s)
def rep(s, old, new):
    if old not in s: raise SystemExit('missing: ' + old[:80])
    return s.replace(old, new, 1)

s = rd('Hints.cs')
s = rep(s, "        private float _fullStaminaTime;\n", "        private float _flightTime;\n")
s = rep(s, '''            if (dr.Mounted)
            {
''', '''            if (dr.Mounted)
            {
                _flightTime += Time.unscaledDeltaTime;
''')
s = rep(s, 'if (d.PlaySeconds > 0f) Offer("land"', 'if (_flightTime > 25f) Offer("land"')
wr('Hints.cs', s)

s = rd('HudAndDirector.cs')
s = rep(s, '''                    if (Director.Dragon.RiderNear) lines.Add("[E]  Mount");
                    lines.Add("[G]  Summon Mount");
                    lines.Add("[LMB]  Strike     [RMB]  Parry");''',
'''                    if (Director.Dragon.RiderNear) lines.Add("[" + Bindings.Short(GameAction.Interact) + "]  Mount");
                    lines.Add("[" + Bindings.Short(GameAction.Summon) + "]  Summon Mount");
                    lines.Add("[" + Bindings.Short(GameAction.Attack) + "]  Strike     [" + Bindings.Short(GameAction.Block) + "]  Parry");''')
s = rep(s, '''                    lines.Add("[LMB]  Breathe fire");
                    lines.Add("[Shift]  Boost");
                    lines.Add("[E]  Land (low and slow)");''',
'''                    lines.Add("[" + Bindings.Short(GameAction.Attack) + "]  Breathe fire");
                    lines.Add("[" + Bindings.Short(GameAction.Sprint) + "]  Boost");
                    lines.Add("[" + Bindings.Short(GameAction.Interact) + "]  Land (low and slow)");''')
wr('HudAndDirector.cs', s)

s = rd('BootstrapAndAutoplay.cs')
s = rep(s, '''            Menu menu = dir.AddComponent<Menu>();''', '''            HintSystem hints = dir.AddComponent<HintSystem>();
            hints.Director = d;

            Menu menu = dir.AddComponent<Menu>();''')
wr('BootstrapAndAutoplay.cs', s)
print('ok')
