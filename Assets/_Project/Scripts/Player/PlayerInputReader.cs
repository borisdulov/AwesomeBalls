using AwesomeBalls.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AwesomeBalls.Player
{
    /// <summary>
    /// Reads the project-wide "Player" action map every frame and accumulates button presses
    /// until the next simulation tick consumes them.
    /// </summary>
    public sealed class PlayerInputReader
    {
        readonly InputAction move;
        readonly InputAction look;
        readonly InputAction jump;
        readonly InputAction dash;
        readonly InputAction slide;
        readonly InputAction fire;
        readonly InputAction reload;

        bool jumpPressed;
        bool dashPressed;

        public Vector2 Move { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool SlideHeld { get; private set; }

        // Combat runs per frame, not per movement tick, so these read the actions directly.
        public bool FireHeld => fire.IsPressed();
        public bool ReloadPressed => reload.WasPressedThisFrame();

        public PlayerInputReader()
        {
            InputActionMap map = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            move = map.FindAction("Move", true);
            look = map.FindAction("Look", true);
            jump = map.FindAction("Jump", true);
            dash = map.FindAction("Dash", true);
            slide = map.FindAction("Slide", true);
            fire = map.FindAction("Attack", true);
            reload = map.FindAction("Reload", true);
            map.Enable();
        }

        /// <summary>Call once per frame before ticking. Returns the look delta in degrees.</summary>
        public Vector2 Poll(float mouseSensitivity, float gamepadLookSpeed)
        {
            Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            JumpHeld = jump.IsPressed();
            SlideHeld = slide.IsPressed();
            jumpPressed |= jump.WasPressedThisFrame();
            dashPressed |= dash.WasPressedThisFrame();

            Vector2 lookValue = look.ReadValue<Vector2>();
            bool isGamepad = look.activeControl?.device is Gamepad;
            return isGamepad
                ? lookValue * (gamepadLookSpeed * Time.unscaledDeltaTime)
                : lookValue * mouseSensitivity;
        }

        public MotorInput ConsumeTickInput(float yaw, float pitch)
        {
            var input = new MotorInput
            {
                Move = Move,
                Yaw = yaw,
                Pitch = pitch,
                JumpPressed = jumpPressed,
                JumpHeld = JumpHeld,
                DashPressed = dashPressed,
                SlideHeld = SlideHeld,
            };
            jumpPressed = false;
            dashPressed = false;
            return input;
        }
    }
}
