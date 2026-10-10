using System;
using AwesomeBalls.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AwesomeBalls.Player
{
    /// <summary>
    /// Local player loop: reads input every frame, runs the motor on a fixed tick and exposes an
    /// interpolated position for the camera. Will be replaced by network ticks in M3.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerController : MonoBehaviour
    {
        const int MaxTicksPerFrame = 5;

        [SerializeField] PlayerCamera playerCamera;
        [SerializeField] float killHeight = -30f;

        PlayerMotor motor;
        PlayerInputReader input;
        Vector3 spawnPosition;
        float spawnYaw;
        float accumulator;
        Vector3 previousTickPosition;
        Vector3 currentTickPosition;

        public PlayerMotor Motor => motor;
        public PlayerInputReader Input => input;
        public PlayerCamera Camera => playerCamera;
        /// <summary>Movement and shooting frozen (e.g. round countdown); looking around still works.</summary>
        public bool InputLocked { get; set; }
        public bool HasControl => enabled && !InputLocked && Cursor.lockState == CursorLockMode.Locked;
        public Vector3 InterpolatedPosition => Vector3.Lerp(previousTickPosition, currentTickPosition, accumulator / motor.Settings.TickDelta);

        public event Action<MotorTickResult> Ticked;
        /// <summary>Raised instead of the local respawn when something (e.g. health) handles falling off the map.</summary>
        public event Action FellOutOfWorld;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = new PlayerInputReader();
            spawnPosition = transform.position;
            spawnYaw = transform.eulerAngles.y;
            previousTickPosition = currentTickPosition = transform.position;
        }

        /// <summary>Attaches the camera that drives look direction. Call before enabling.</summary>
        public void Bind(PlayerCamera camera)
        {
            playerCamera = camera;
        }

        void Start()
        {
            playerCamera.SetLook(spawnYaw, 0f);
            LockCursor(true);
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) LockCursor(false);
            bool hasControl = Cursor.lockState == CursorLockMode.Locked;

            Vector2 look = input.Poll(playerCamera.MouseSensitivity, playerCamera.GamepadLookSpeed);
            if (hasControl) playerCamera.AddLook(look);

            float dt = motor.Settings.TickDelta;
            accumulator += Time.deltaTime;
            int ticks = 0;
            while (accumulator >= dt && ticks < MaxTicksPerFrame)
            {
                MotorInput tickInput = input.ConsumeTickInput(playerCamera.Yaw, playerCamera.Pitch);
                // Paused (cursor free) or frozen: the world keeps simulating, but the player gives no input.
                if (!hasControl || InputLocked) tickInput = new MotorInput { Yaw = tickInput.Yaw, Pitch = tickInput.Pitch };

                previousTickPosition = transform.position;
                MotorTickResult result = motor.Simulate(tickInput, dt);
                currentTickPosition = transform.position;
                // Body faces the look direction so other players see where we aim.
                transform.rotation = Quaternion.Euler(0f, playerCamera.Yaw, 0f);
                Ticked?.Invoke(result);

                if (transform.position.y < killHeight)
                {
                    if (FellOutOfWorld != null) FellOutOfWorld.Invoke();
                    else Respawn();
                    break;
                }

                accumulator -= dt;
                ticks++;
            }
            if (ticks == MaxTicksPerFrame) accumulator = 0f;
        }

        public void Respawn() => TeleportTo(spawnPosition, spawnYaw);

        public void TeleportTo(Vector3 position, float yaw)
        {
            motor.Teleport(position);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (playerCamera != null) playerCamera.SetLook(yaw, 0f);
            previousTickPosition = currentTickPosition = position;
            accumulator = 0f;
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
