using UnityEngine;

namespace AwesomeBalls.Movement
{
    /// <summary>
    /// All movement tuning values (GDD section 5). Edit in the inspector, even during Play mode.
    /// </summary>
    [CreateAssetMenu(menuName = "AwesomeBalls/Movement Settings", fileName = "MovementSettings")]
    public sealed class MovementSettings : ScriptableObject
    {
        [Header("Simulation")]
        [Tooltip("Fixed simulation rate, ticks per second.")]
        public int tickRate = 60;

        [Header("Ground")]
        public float runSpeed = 11f;
        public float crouchSpeed = 4.5f;
        public float groundAcceleration = 80f;
        public float groundDeceleration = 60f;
        [Tooltip("Friction applied only to the part of the speed above runSpeed.")]
        public float overspeedFriction = 12f;
        public float maxSlopeAngle = 50f;
        public float groundSnapDistance = 0.35f;

        [Header("Air")]
        public float airAcceleration = 25f;
        [Tooltip("Air control can't accelerate beyond this speed, but never cuts existing momentum.")]
        public float airMaxSpeed = 11f;
        public float gravity = 28f;
        public float fallGravityMultiplier = 1.25f;
        public float maxFallSpeed = 45f;
        public float maxHorizontalSpeed = 35f;

        [Header("Jump")]
        public float jumpHeight = 1.5f;
        public float coyoteTime = 0.12f;
        public float jumpBufferTime = 0.12f;
        [Tooltip("Jumping within this window after landing skips ground friction.")]
        public float bhopWindow = 0.1f;
        [Tooltip("Releasing jump while rising multiplies vertical speed by this.")]
        [Range(0f, 1f)] public float jumpCutMultiplier = 0.5f;

        [Header("Double Jump")]
        public int airJumps = 1;
        public float airJumpHeight = 1.2f;
        public float airJumpRedirectAngle = 60f;
        [Range(0f, 1f)] public float airJumpSpeedKeep = 0.95f;

        [Header("Dash")]
        public int dashCharges = 2;
        public float dashRechargeTime = 1.5f;
        public float dashMinInterval = 0.2f;
        public float dashDuration = 0.14f;
        public float dashSpeed = 32f;
        public float dashExitSpeed = 13f;

        [Header("Slide")]
        public float slideMinStartSpeed = 7f;
        public float slideBoost = 5f;
        public float slideBoostCap = 18f;
        public float slideBoostCooldown = 1.2f;
        public float slideFriction = 4f;
        public float slideExitSpeed = 5f;
        public float slideTurnRate = 120f;
        [Tooltip("Fall speed above which landing into a slide converts part of it into forward speed.")]
        public float slideLandingMinFallSpeed = 10f;
        [Range(0f, 1f)] public float slideLandingConversion = 0.4f;

        [Header("Wall Jump")]
        public float wallCheckDistance = 0.35f;
        [Tooltip("Max deviation of the wall normal from horizontal, degrees.")]
        public float wallMaxNormalTilt = 25f;
        public float wallJumpMinAirTime = 0.1f;
        public float wallJumpPush = 8f;
        [Range(0f, 1f)] public float wallJumpTangentKeep = 0.9f;
        [Range(0f, 1f)] public float wallJumpInputBias = 0.3f;
        [Range(0f, 1f)] public float wallJumpChainFalloff = 0.1f;
        [Range(0f, 1f)] public float wallJumpMinVertical = 0.6f;
        public float sameWallAngle = 30f;
        public float sameWallDistance = 2f;

        [Header("Wall Run")]
        public bool wallRunEnabled = true;
        [Tooltip("Minimum speed along the wall to start running on it.")]
        public float wallRunMinSpeed = 7f;
        [Tooltip("Speed along the wall the run accelerates toward. Faster entries keep their speed.")]
        public float wallRunSpeed = 13f;
        public float wallRunAcceleration = 20f;
        public float wallRunMaxDuration = 1.2f;
        public float wallRunGravity = 6f;
        [Tooltip("Vertical speed on entry is clamped into [lift, maxEntryUp].")]
        public float wallRunEntryLift = 1.5f;
        public float wallRunMaxEntryUpSpeed = 5f;
        public float wallRunStickSpeed = 2f;
        [Tooltip("No wall run this close to the ground.")]
        public float wallRunMinHeight = 0.6f;

        [Header("Wall Slide")]
        public float wallSlideMaxFallSpeed = 5f;
        public float wallSlideDuration = 0.6f;

        [Header("Capsule")]
        public float radius = 0.45f;
        public float standHeight = 1.8f;
        public float crouchHeight = 0.9f;
        public float stepOffset = 0.4f;

        public float TickDelta => 1f / Mathf.Max(1, tickRate);
        public float JumpVelocity => Mathf.Sqrt(2f * gravity * jumpHeight);
        public float AirJumpVelocity => Mathf.Sqrt(2f * gravity * airJumpHeight);
    }
}
