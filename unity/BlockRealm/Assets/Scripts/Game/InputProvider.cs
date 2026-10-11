using System;
using UnityEngine;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>
    /// Reads keyboard, mouse and gamepad into the game's input structs. Uses Unity's classic Input class; the
    /// gamepad axes are defined by the project setup script (see ProjectSetup.cs) using the standard Xbox layout.
    /// </summary>
    public sealed class InputProvider
    {
        public float Yaw, Pitch;
        public PlayerInput Move;
        public ClientActions Actions;

        // One-shot commands, true for exactly one frame.
        public bool InventoryPressed, ChatPressed, CommandPressed, PausePressed, DebugPressed, CameraPressed;
        public bool DropPressed, DropStackPressed, JumpDoubleTapped, ClosePressed, JumpPressed;
        public int HotbarDirect = -1;
        public int HotbarDelta;

        // Interface navigation (menus, inventory): a cursor moved by the stick on a gamepad.
        public bool UsingGamepad;
        public Vector2 PadCursorDelta;
        public bool PadClick, PadRightClick, PadShiftClick;

        private float _lastJumpTap = -10f, _lastForwardTap = -10f;
        private bool _sprintLatched;
        private bool _padOk = true;
        private bool _prevRt, _prevLt, _prevDpadUp, _prevDpadDown, _prevDpadLeft, _prevDpadRight;

        private float Axis(string name)
        {
            if (!_padOk) return 0f;
            try { return Input.GetAxisRaw(name); }
            catch (ArgumentException) { _padOk = false; return 0f; }
        }

        private static float Dead(float v, float dz = 0.18f)
        {
            float a = Mathf.Abs(v);
            if (a < dz) return 0f;
            return Mathf.Sign(v) * (a - dz) / (1f - dz);
        }

        /// <param name="uiOpen">When a menu or inventory is open, look and movement are suspended.</param>
        public void Update(float dt, bool uiOpen)
        {
            InventoryPressed = ChatPressed = CommandPressed = PausePressed = DebugPressed = CameraPressed = false;
            DropPressed = DropStackPressed = JumpDoubleTapped = ClosePressed = JumpPressed = false;
            HotbarDirect = -1; HotbarDelta = 0;
            PadClick = PadRightClick = PadShiftClick = false;
            PadCursorDelta = Vector2.zero;
            Actions = new ClientActions();

            float lx = Dead(Axis("PadLX")), ly = -Dead(Axis("PadLY"));
            float rx = Dead(Axis("PadRX")), ry = -Dead(Axis("PadRY"));
            float lt = Mathf.Max(Axis("PadLT"), 0f), rt = Mathf.Max(Axis("PadRT"), 0f);
            float dx = Axis("PadDX"), dy = Axis("PadDY");
            bool padActive = Mathf.Abs(lx) + Mathf.Abs(ly) + Mathf.Abs(rx) + Mathf.Abs(ry) + lt + rt > 0.2f ||
                             Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.JoystickButton1);
            if (padActive) UsingGamepad = true;
            if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) + Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f || Input.anyKeyDown && !padActive)
                UsingGamepad = false;

            bool rtDown = rt > 0.5f, ltDown = lt > 0.5f;
            bool dUp = dy > 0.5f, dDown = dy < -0.5f, dLeft = dx < -0.5f, dRight = dx > 0.5f;

            // ------------------------------------------------ commands
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton3)) InventoryPressed = true;
            if (Input.GetKeyDown(KeyCode.T)) ChatPressed = true;
            if (Input.GetKeyDown(KeyCode.Slash)) CommandPressed = true;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) ChatPressed = true;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton7)) PausePressed = true;
            if (Input.GetKeyDown(KeyCode.F3)) DebugPressed = true;
            if (Input.GetKeyDown(KeyCode.F5) || Input.GetKeyDown(KeyCode.JoystickButton9)) CameraPressed = true;
            if (Input.GetKeyDown(KeyCode.Q) || (dDown && !_prevDpadDown)) DropPressed = true;
            if (Input.GetKeyDown(KeyCode.Q) && (Input.GetKey(KeyCode.LeftControl))) DropStackPressed = true;
            if (Input.GetKeyDown(KeyCode.JoystickButton1)) ClosePressed = true;

            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) HotbarDirect = i;
            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0.5f) HotbarDelta = -1; else if (scroll < -0.5f) HotbarDelta = 1;
            if (Input.GetKeyDown(KeyCode.JoystickButton4)) HotbarDelta = -1;
            if (Input.GetKeyDown(KeyCode.JoystickButton5)) HotbarDelta = 1;

            if (uiOpen)
            {
                // Stick moves a cursor; A / X / Y act as left, right and shift clicks.
                PadCursorDelta = new Vector2(lx, -ly) * 900f * dt;
                if (Input.GetKeyDown(KeyCode.JoystickButton0)) PadClick = true;
                if (Input.GetKeyDown(KeyCode.JoystickButton2)) PadRightClick = true;
                if (Input.GetKeyDown(KeyCode.JoystickButton3)) { PadShiftClick = true; InventoryPressed = false; }
                if (rtDown && !_prevRt) PadClick = true;
                if (ltDown && !_prevLt) PadRightClick = true;
                Move = new PlayerInput { Yaw = Yaw, Pitch = Pitch };
                _prevRt = rtDown; _prevLt = ltDown;
                _prevDpadUp = dUp; _prevDpadDown = dDown; _prevDpadLeft = dLeft; _prevDpadRight = dRight;
                return;
            }

            // ------------------------------------------------ look
            float sens = GameSettings.MouseSensitivity;
            float lookX = Input.GetAxisRaw("Mouse X") * 2.2f * sens;
            float lookY = Input.GetAxisRaw("Mouse Y") * 2.2f * sens;
            float stick = GameSettings.StickSensitivity;
            lookX += rx * 150f * stick * dt;
            lookY += ry * 110f * stick * dt;
            Yaw += lookX * Mathf.Deg2Rad;
            float invert = GameSettings.InvertY ? 1f : -1f;
            Pitch = Mathf.Clamp(Pitch + invert * lookY * Mathf.Deg2Rad, -1.55f, 1.55f);
            if (Yaw > Mathf.PI * 2f) Yaw -= Mathf.PI * 2f;
            if (Yaw < -Mathf.PI * 2f) Yaw += Mathf.PI * 2f;

            // ------------------------------------------------ movement
            float mx = 0f, mz = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) mz += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) mz -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) mx += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) mx -= 1f;
            mx += lx; mz += ly;
            mx = Mathf.Clamp(mx, -1f, 1f); mz = Mathf.Clamp(mz, -1f, 1f);

            bool jump = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton0);
            bool jumpDown = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0);
            bool sneak = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.JoystickButton1);
            bool sprintKey = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.JoystickButton8);

            // Double-tap forward to sprint, like the original; releasing forward ends it.
            if (Input.GetKeyDown(KeyCode.W))
            {
                if (Time.unscaledTime - _lastForwardTap < 0.3f) _sprintLatched = true;
                _lastForwardTap = Time.unscaledTime;
            }
            if (mz <= 0.1f) _sprintLatched = false;
            JumpPressed = jumpDown;
            if (jumpDown)
            {
                if (Time.unscaledTime - _lastJumpTap < 0.3f) JumpDoubleTapped = true;
                _lastJumpTap = Time.unscaledTime;
            }

            Move = new PlayerInput
            {
                MoveX = mx, MoveZ = mz, Jump = jump, Sneak = sneak, Sprint = sprintKey || _sprintLatched,
                Yaw = Yaw, Pitch = Pitch,
            };

            // ------------------------------------------------ actions
            Actions.AttackHeld = Input.GetMouseButton(0) || rtDown;
            Actions.UsePressed = Input.GetMouseButtonDown(1) || (ltDown && !_prevLt);
            Actions.UseHeld = Input.GetMouseButton(1) || ltDown;
            Actions.DismountPressed = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.JoystickButton1);

            _prevRt = rtDown; _prevLt = ltDown;
            _prevDpadUp = dUp; _prevDpadDown = dDown; _prevDpadLeft = dLeft; _prevDpadRight = dRight;
        }

        public void SetLook(float yaw, float pitch) { Yaw = yaw; Pitch = pitch; }
    }
}
