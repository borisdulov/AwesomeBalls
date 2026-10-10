using System;
using UnityEngine;

namespace AwesomeBalls.Movement
{
    public enum MoveMode : byte
    {
        Ground,
        Crouch,
        Slide,
        Air,
        WallSlide,
        WallRun,
        Dash,
    }

    /// <summary>
    /// Input for one simulation tick. Pressed flags are edges accumulated since the previous tick.
    /// </summary>
    public struct MotorInput
    {
        public Vector2 Move;
        public float Yaw;
        public float Pitch;
        public bool JumpPressed;
        public bool JumpHeld;
        public bool DashPressed;
        public bool SlideHeld;
    }

    /// <summary>
    /// Complete movement state. Together with the transform position it fully describes the motor,
    /// so it can be saved and restored for network reconciliation.
    /// </summary>
    public struct MotorState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public MoveMode Mode;

        public bool Grounded;
        public Vector3 GroundNormal;
        public float TimeSinceGrounded;
        public float TimeSinceLanded;

        public float JumpBufferTimer;
        public bool JumpCutAvailable;
        public bool JumpedSinceGrounded;
        public int AirJumpsLeft;

        public int DashCharges;
        public float DashRechargeTimer;
        public float DashCooldownTimer;
        public float DashTimer;
        public Vector3 DashDirection;
        public float DashEntrySpeed;

        public float SlideBoostCooldownTimer;
        public bool Crouched;

        public bool HasLastWall;
        public Vector3 LastWallNormal;
        public Vector3 LastWallPoint;
        public int WallJumpChain;
        public float WallSlideTimer;
        public Vector3 WallNormal;

        public float WallRunTimer;
        public bool HasWallRunLock;
        public Vector3 WallRunLockNormal;
    }

    /// <summary>
    /// One-shot events produced by a tick, consumed by camera/audio/VFX.
    /// </summary>
    [Flags]
    public enum MotorEvents : ushort
    {
        None = 0,
        Jumped = 1 << 0,
        AirJumped = 1 << 1,
        WallJumped = 1 << 2,
        Dashed = 1 << 3,
        Landed = 1 << 4,
        SlideStarted = 1 << 5,
        WallRunStarted = 1 << 6,
    }

    public struct MotorTickResult
    {
        public MotorEvents Events;
        public float LandingSpeed;
    }
}
