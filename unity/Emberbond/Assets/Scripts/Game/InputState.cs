using UnityEngine;
using UnityEngine.InputSystem;

namespace Emberbond
{
    /// <summary>One place that reads keyboard, mouse and gamepad through the Input System package.</summary>
    public static class InputState
    {
        public static Vector2 Move;          // x strafe, y forward
        public static Vector2 Look;          // delta, already scaled
        public static bool Jump, JumpDown, Sprint, Crouch;
        public static bool Interact, Summon, Attack, AttackDown, Block, BlockDown, Fire, FireDown, Boost;
        public static float Vertical;        // +1 climb, -1 dive while flying
        public static bool Pause, Map;
        public static int Choice = -1;       // 0..2 dialogue choice pressed this frame
        public static bool UsingPad;
        public static bool Scripted;                       // set by the autoplay test driver
        public static int ScriptedChoice = -1;
        public static Vector2 ScriptedLook;
        public static bool ScriptedFire;

        public static void Poll()
        {
            Keyboard k = Keyboard.current;
            Mouse m = Mouse.current;
            Gamepad g = Gamepad.current;

            Move = Vector2.zero; Look = Vector2.zero;
            Jump = JumpDown = Sprint = Crouch = false;
            Interact = Summon = Attack = AttackDown = Block = BlockDown = Fire = FireDown = Boost = false;
            Vertical = 0f; Pause = Map = false; Choice = -1;

            if (k != null)
            {
                float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                Move += new Vector2(x, y);
                Jump = k.spaceKey.isPressed; JumpDown = k.spaceKey.wasPressedThisFrame;
                Sprint = k.leftShiftKey.isPressed; Crouch = k.leftCtrlKey.isPressed;
                Interact = k.eKey.wasPressedThisFrame;
                Summon = k.gKey.wasPressedThisFrame;
                Pause = k.escapeKey.wasPressedThisFrame; Map = k.mKey.wasPressedThisFrame;
                Boost = k.leftShiftKey.isPressed;
                Vertical = (k.spaceKey.isPressed ? 1f : 0f) - (k.leftCtrlKey.isPressed ? 1f : 0f);
                if (k.digit1Key.wasPressedThisFrame) Choice = 0;
                if (k.digit2Key.wasPressedThisFrame) Choice = 1;
                if (k.digit3Key.wasPressedThisFrame) Choice = 2;
            }
            if (m != null)
            {
                Look += m.delta.ReadValue() * 0.06f;
                Attack = m.leftButton.isPressed; AttackDown = m.leftButton.wasPressedThisFrame;
                Block = m.rightButton.isPressed; BlockDown = m.rightButton.wasPressedThisFrame;
                Fire = m.leftButton.isPressed; FireDown = m.leftButton.wasPressedThisFrame;
            }
            UsingPad = false;
            if (g != null)
            {
                Vector2 ls = g.leftStick.ReadValue();
                Vector2 rs = g.rightStick.ReadValue();
                if (ls.sqrMagnitude > 0.04f) { Move += ls; UsingPad = true; }
                if (rs.sqrMagnitude > 0.04f) { Look += rs * 140f * Time.deltaTime; UsingPad = true; }
                Jump |= g.buttonSouth.isPressed; JumpDown |= g.buttonSouth.wasPressedThisFrame;
                Interact |= g.buttonWest.wasPressedThisFrame;
                Summon |= g.dpad.up.wasPressedThisFrame;
                Attack |= g.rightTrigger.isPressed; AttackDown |= g.rightTrigger.wasPressedThisFrame;
                Fire |= g.rightTrigger.isPressed; FireDown |= g.rightTrigger.wasPressedThisFrame;
                Block |= g.leftTrigger.isPressed; BlockDown |= g.leftTrigger.wasPressedThisFrame;
                Sprint |= g.leftStickButton.isPressed;
                Boost |= g.rightShoulder.isPressed;
                Vertical += (g.rightShoulder.isPressed ? 0f : 0f) + (g.buttonSouth.isPressed ? 1f : 0f) - (g.buttonEast.isPressed ? 1f : 0f);
                Pause |= g.startButton.wasPressedThisFrame;
            }
            if (Scripted)
            {
                Look = ScriptedLook;
                if (ScriptedFire) { Fire = true; FireDown = true; }
                if (ScriptedChoice >= 0) { Choice = ScriptedChoice; ScriptedChoice = -1; }
                if (Move.sqrMagnitude < 0.01f) Move = new Vector2(0f, 0.0f);
            }
            Move = Vector2.ClampMagnitude(Move, 1f);
        }
    }
}
