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
        public static bool ScriptedAttack, ScriptedBlock, ScriptedBoost;
        public static Vector2 ScriptedMove;
        public static bool Blocked;                        // true while a menu is open
        public static bool Cinematic;                      // true during dialogue: no movement or combat, choices still work

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
                float x = (Bindings.Held(GameAction.Right) ? 1f : 0f) - (Bindings.Held(GameAction.Left) ? 1f : 0f);
                float y = (Bindings.Held(GameAction.Forward) ? 1f : 0f) - (Bindings.Held(GameAction.Back) ? 1f : 0f);
                Move += new Vector2(x, y);
                Jump = Bindings.Held(GameAction.Jump); JumpDown = Bindings.Pressed(GameAction.Jump);
                Sprint = Bindings.Held(GameAction.Sprint); Crouch = Bindings.Held(GameAction.Crouch);
                Interact = Bindings.Pressed(GameAction.Interact);
                Summon = Bindings.Pressed(GameAction.Summon);
                Pause = k.escapeKey.wasPressedThisFrame; Map = k.mKey.wasPressedThisFrame;
                Boost = Sprint;
                Vertical = (Jump ? 1f : 0f) - (Crouch ? 1f : 0f);
                if (k.digit1Key.wasPressedThisFrame) Choice = 0;
                if (k.digit2Key.wasPressedThisFrame) Choice = 1;
                if (k.digit3Key.wasPressedThisFrame) Choice = 2;
            }
            if (m != null)
            {
                Look += m.delta.ReadValue() * 0.06f;
            }
            // Strike, fire and parry follow the bindings, so they work on keys as well as mouse buttons.
            Attack = Bindings.Held(GameAction.Attack); AttackDown = Bindings.Pressed(GameAction.Attack);
            Block = Bindings.Held(GameAction.Block); BlockDown = Bindings.Pressed(GameAction.Block);
            Fire = Attack; FireDown = AttackDown;
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
                if (ScriptedAttack) { Attack = true; AttackDown = true; ScriptedAttack = false; }
                if (ScriptedBlock) { Block = true; BlockDown = true; ScriptedBlock = false; }
                if (ScriptedMove.sqrMagnitude > 0.01f) Move = ScriptedMove;
                if (ScriptedBoost) { Boost = true; Sprint = true; }
            }
            if (!Scripted) Look *= GameSettings.MouseSens;
            if (GameSettings.InvertY) Look.y = -Look.y;
            if (Blocked)
            {
                Move = Vector2.zero; Look = Vector2.zero; Vertical = 0f; Choice = -1;
                Jump = JumpDown = Sprint = Crouch = false;
                Interact = Summon = Attack = AttackDown = Block = BlockDown = Fire = FireDown = Boost = false;
            }
            if (Cinematic)
            {
                Move = Vector2.zero; Look = Vector2.zero; Vertical = 0f;
                Jump = JumpDown = Sprint = Crouch = false;
                Interact = Summon = Attack = AttackDown = Block = BlockDown = Fire = FireDown = Boost = false;
            }
            Move = Vector2.ClampMagnitude(Move, 1f);
        }
    }
}
