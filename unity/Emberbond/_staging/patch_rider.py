import os, shutil


def patch(p, pairs):
    s = open(p, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    s = s.replace('\r\n', '\n')
    for a, b in pairs:
        assert s.count(a) == 1, (p, a[:70], s.count(a))
        s = s.replace(a, b)
    if crlf:
        s = s.replace('\n', '\r\n')
    open(p, 'w', encoding='utf-8', newline='').write(s)


root = r'D:\Projects\Games\claude\unity\Emberbond'
os.makedirs(os.path.join(root, 'Assets', 'Art', 'Characters'), exist_ok=True)
os.makedirs(os.path.join(root, 'Assets', 'Resources'), exist_ok=True)
shutil.copy(os.path.join(os.environ['TEMP'], 'char_out', 'rider.fbx'), os.path.join(root, 'Assets', 'Art', 'Characters', 'rider.fbx'))

patch(os.path.join(root, 'Assets/Scripts/Game/PlayerAndCamera.cs'), [
    ('''        private void BuildVisual()
        {
            GameObject v = new GameObject("Visual");
            v.transform.SetParent(transform, false);
            Visual = v.transform;''',
     '''        public RiderAnimator Anim;

        private void BuildVisual()
        {
            GameObject prefab = Resources.Load<GameObject>("Rider");
            if (prefab != null)
            {
                GameObject rider = Instantiate(prefab, transform);
                rider.name = "Visual";
                rider.transform.localPosition = Vector3.zero;
                Visual = rider.transform;
                Anim = rider.AddComponent<RiderAnimator>();
                foreach (Collider col in rider.GetComponentsInChildren<Collider>()) Destroy(col);
                return;
            }
            GameObject v = new GameObject("Visual");
            v.transform.SetParent(transform, false);
            Visual = v.transform;'''),
    ('''            Visual.rotation = Quaternion.Euler(0f, YawFacing, 0f);
            Visual.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.time * (sprint ? 12f : 8f))) * 0.06f * _moveBlend, 0f);''',
     '''            Visual.rotation = Quaternion.Euler(0f, YawFacing, 0f);
            Visual.localPosition = Anim != null ? Vector3.zero : new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.time * (sprint ? 12f : 8f))) * 0.06f * _moveBlend, 0f);
            if (Anim != null)
            {
                Anim.Seated = false;
                Anim.Speed = _moveBlend * (sprint ? 2f : 1f) * (InputState.Crouch ? 0.5f : 1f);
                Anim.Strike = SwingTimer > 0f ? 1f - SwingTimer / 0.35f : 0f;
                Anim.Parry = ParryWindow > 0f ? 1f : 0f;
            }'''),
    ('''        public void SetMounted(bool mounted)
        {
            Mounted = mounted;''',
     '''        public void SetMounted(bool mounted)
        {
            Mounted = mounted;
            if (Anim != null) Anim.Seated = mounted;'''),
])
patch(os.path.join(root, 'Assets/Editor/IslandBuilder.cs'), [
    ('''            CreateFxAssets();''', '''            CreateFxAssets();
            CharacterBuilder.Build();'''),
])
print('rider patch ok')
