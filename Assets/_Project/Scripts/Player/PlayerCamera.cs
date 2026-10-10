using AwesomeBalls.Movement;
using UnityEngine;

namespace AwesomeBalls.Player
{
    /// <summary>
    /// First-person camera: look, interpolated follow, eye height, dynamic FOV, tilt and landing dip
    /// (GDD 5.9). Lives on its own GameObject so it renders smoothly between simulation ticks.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCamera : MonoBehaviour
    {
        [SerializeField] PlayerController target;

        [Header("Look")]
        [Tooltip("Degrees per mouse count.")]
        [SerializeField] float mouseSensitivity = 0.08f;
        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField] float gamepadLookSpeed = 220f;

        [Header("Eye")]
        [SerializeField] float standEyeHeight = 1.6f;
        [SerializeField] float crouchEyeHeight = 0.8f;
        [SerializeField] float eyeHeightSpeed = 8f;

        [Header("FOV")]
        [SerializeField] float baseFov = 90f;
        [Tooltip("Extra horizontal FOV at speedFovMax and above.")]
        [SerializeField] float speedFovBonus = 20f;
        [Tooltip("Horizontal speed (m/s) where the FOV starts widening. Just above run speed.")]
        [SerializeField] float speedFovMin = 11.5f;
        [SerializeField] float speedFovMax = 26f;
        [Tooltip("How fast the FOV widens when speeding up.")]
        [SerializeField] float fovRiseSharpness = 8f;
        [Tooltip("How fast the FOV settles back when slowing down.")]
        [SerializeField] float fovFallSharpness = 3f;
        [SerializeField] float dashFovKick = 6f;
        [SerializeField] float dashKickDuration = 0.15f;

        [Header("Juice")]
        [Tooltip("Turns off tilt, landing dip and FOV effects (motion sickness option).")]
        [SerializeField] bool reduceCameraEffects;
        [SerializeField] float slideTilt = 3f;
        [SerializeField] float wallSlideTilt = 5f;
        [SerializeField] float wallRunTilt = 10f;
        [SerializeField] float tiltSharpness = 10f;
        [SerializeField] float maxLandingDip = 0.15f;
        [SerializeField] float landingDipFullSpeed = 30f;
        [SerializeField] float dipStiffness = 150f;
        [SerializeField] float dipDamping = 17f;

        Camera cam;
        float yaw;
        float pitch;
        float roll;
        float eyeHeight;
        float fov;
        float dashKickTimer;
        float dipOffset;
        float dipVelocity;

        public float Yaw => yaw;
        public float Pitch => pitch;
        public float MouseSensitivity => mouseSensitivity;
        public float GamepadLookSpeed => gamepadLookSpeed;

        void Awake()
        {
            cam = GetComponent<Camera>();
            eyeHeight = standEyeHeight;
            fov = baseFov;
        }

        void OnEnable()
        {
            if (target != null) target.Ticked += OnTicked;
        }

        void OnDisable()
        {
            if (target != null) target.Ticked -= OnTicked;
        }

        /// <summary>Follows the given player (the local one once it spawns), or nothing when null.</summary>
        public void SetTarget(PlayerController newTarget)
        {
            if (isActiveAndEnabled && target != null) target.Ticked -= OnTicked;
            target = newTarget;
            if (isActiveAndEnabled && target != null) target.Ticked += OnTicked;
        }

        public void SetLook(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = newPitch;
        }

        public void AddLook(Vector2 deltaDegrees)
        {
            yaw = Mathf.Repeat(yaw + deltaDegrees.x, 360f);
            pitch = Mathf.Clamp(pitch - deltaDegrees.y, -89f, 89f);
        }

        void OnTicked(MotorTickResult result)
        {
            if (reduceCameraEffects) return;
            if ((result.Events & MotorEvents.Dashed) != 0) dashKickTimer = dashKickDuration;
            if ((result.Events & MotorEvents.Landed) != 0)
            {
                float amount = Mathf.Clamp01(result.LandingSpeed / landingDipFullSpeed) * maxLandingDip;
                dipVelocity -= amount * Mathf.Sqrt(dipStiffness);
            }
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            ref readonly MotorState state = ref target.Motor.State;

            float targetEye = state.Crouched ? crouchEyeHeight : standEyeHeight;
            eyeHeight = Mathf.MoveTowards(eyeHeight, targetEye, eyeHeightSpeed * dt);

            float dipAccel = -dipStiffness * dipOffset - dipDamping * dipVelocity;
            dipVelocity += dipAccel * dt;
            dipOffset += dipVelocity * dt;

            transform.position = target.InterpolatedPosition + Vector3.up * (eyeHeight + dipOffset);

            float targetRoll = 0f;
            float targetFov = baseFov;
            if (!reduceCameraEffects)
            {
                if (state.Mode == MoveMode.Slide)
                {
                    targetRoll = -target.Input.Move.x * slideTilt;
                }
                else if (state.Mode is MoveMode.WallSlide or MoveMode.WallRun)
                {
                    Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    float tilt = state.Mode == MoveMode.WallRun ? wallRunTilt : wallSlideTilt;
                    targetRoll = -Mathf.Sign(Vector3.Dot(state.WallNormal, right)) * tilt;
                }

                float speed = new Vector2(state.Velocity.x, state.Velocity.z).magnitude;
                targetFov += Mathf.InverseLerp(speedFovMin, speedFovMax, speed) * speedFovBonus;
                if (dashKickTimer > 0f)
                {
                    targetFov += dashFovKick * (dashKickTimer / dashKickDuration);
                    dashKickTimer -= dt;
                }
            }

            float blend = 1f - Mathf.Exp(-tiltSharpness * dt);
            roll = Mathf.Lerp(roll, targetRoll, blend);
            float fovSharpness = targetFov > fov ? fovRiseSharpness : fovFallSharpness;
            fov = Mathf.Lerp(fov, targetFov, 1f - Mathf.Exp(-fovSharpness * dt));
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(fov, cam.aspect);

            transform.rotation = Quaternion.Euler(pitch, yaw, roll);
        }
    }
}
