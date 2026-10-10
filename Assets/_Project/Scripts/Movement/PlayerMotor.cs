using System.Collections.Generic;
using UnityEngine;

namespace AwesomeBalls.Movement
{
    /// <summary>
    /// First-person movement: run, jump, double jump, dash, slide, wall jump, wall slide.
    /// Advanced only through <see cref="Simulate"/> on a fixed tick, so it can later be driven
    /// by network prediction/reconciliation (GDD 13.3). The transform position is the feet.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        const float ProbeLift = 0.1f;
        const int WallProbeDirections = 8;
        const float GroundStickSpeed = 2f;

        [SerializeField] MovementSettings settings;

        CharacterController controller;
        MotorState state;

        readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        readonly Collider[] overlapBuffer = new Collider[16];
        readonly List<(Vector3 normal, Vector3 point)> moveHits = new();

        public MovementSettings Settings => settings;
        public ref readonly MotorState State => ref state;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            ConfigureController();
            ResetState();
        }

        void ConfigureController()
        {
            controller.radius = settings.radius;
            controller.slopeLimit = settings.maxSlopeAngle;
            controller.stepOffset = settings.stepOffset;
            controller.skinWidth = 0.05f;
            controller.minMoveDistance = 0f;
            SetCapsuleHeight(settings.standHeight);
        }

        public void ResetState()
        {
            state = new MotorState
            {
                Position = transform.position,
                Mode = MoveMode.Air,
                GroundNormal = Vector3.up,
                AirJumpsLeft = settings.airJumps,
                DashCharges = settings.dashCharges,
                TimeSinceGrounded = 1f,
                TimeSinceLanded = 1f,
            };
            SetCapsuleHeight(settings.standHeight);
        }

        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            ResetState();
        }

        /// <summary>Swaps in modified settings (card stats). Keeps the current state, clamping resources.</summary>
        public void SetSettings(MovementSettings newSettings)
        {
            settings = newSettings;
            if (controller == null) return; // Before Awake: Awake configures from the new settings.
            controller.radius = settings.radius;
            controller.slopeLimit = settings.maxSlopeAngle;
            controller.stepOffset = settings.stepOffset;
            state.AirJumpsLeft = Mathf.Min(state.AirJumpsLeft, settings.airJumps);
            if (state.Grounded) state.AirJumpsLeft = settings.airJumps;
            state.DashCharges = Mathf.Min(state.DashCharges, settings.dashCharges);
        }

        /// <summary>External push (hit knockback, explosions). Applied on the next tick.</summary>
        public void AddVelocity(Vector3 delta)
        {
            state.Velocity += delta;
            if (delta.y > 0f && state.Grounded)
            {
                state.Grounded = false;
                state.Mode = MoveMode.Air;
            }
        }

        public MotorState CaptureState() => state;

        public void RestoreState(in MotorState snapshot)
        {
            state = snapshot;
            SetCapsuleHeight(state.Crouched ? settings.crouchHeight : settings.standHeight);
            controller.enabled = false;
            transform.position = state.Position;
            controller.enabled = true;
        }

        public MotorTickResult Simulate(in MotorInput input, float dt)
        {
            var s = settings;
            var result = new MotorTickResult();

            Quaternion yawRotation = Quaternion.Euler(0f, input.Yaw, 0f);
            Vector3 wishDir = yawRotation * new Vector3(input.Move.x, 0f, input.Move.y);
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();
            bool hasInput = wishDir.sqrMagnitude > 0.01f;

            TickTimers(input, dt);

            Vector3 velocity = state.Velocity;
            bool jumpedThisTick = false;

            // Variable jump height: releasing jump while rising cuts the jump short.
            if (state.JumpCutAvailable)
            {
                if (velocity.y <= 0f) state.JumpCutAvailable = false;
                else if (!input.JumpHeld)
                {
                    velocity.y *= s.jumpCutMultiplier;
                    state.JumpCutAvailable = false;
                }
            }

            // Dash start.
            if (input.DashPressed && state.DashCharges > 0 && state.DashCooldownTimer <= 0f && state.Mode != MoveMode.Dash)
            {
                Vector3 dir = hasInput ? wishDir.normalized : yawRotation * Vector3.forward;
                state.DashEntrySpeed = Vector3.Dot(Horizontal(velocity), dir);
                state.DashDirection = dir;
                state.DashTimer = s.dashDuration;
                state.DashCooldownTimer = s.dashMinInterval;
                state.DashCharges--;
                state.Mode = MoveMode.Dash;
                state.JumpCutAvailable = false;
                if (state.Crouched) TryStand();
                result.Events |= MotorEvents.Dashed;
            }

            // Jump: ground (with coyote time) > wall > air.
            if (state.JumpBufferTimer > 0f)
            {
                bool canGroundJump = state.Grounded || (state.TimeSinceGrounded <= s.coyoteTime && !state.JumpedSinceGrounded);
                if (canGroundJump)
                {
                    if (state.Mode == MoveMode.Dash) EndDash(ref velocity);
                    if (state.Crouched) TryStand();
                    velocity.y = Mathf.Max(velocity.y, 0f) + s.JumpVelocity;
                    state.Mode = MoveMode.Air;
                    state.Grounded = false;
                    state.JumpedSinceGrounded = true;
                    state.JumpBufferTimer = 0f;
                    state.JumpCutAvailable = true;
                    jumpedThisTick = true;
                    result.Events |= MotorEvents.Jumped;
                }
                else if (state.TimeSinceGrounded >= s.wallJumpMinAirTime
                         && ProbeWall(out Vector3 wallNormal, out Vector3 wallPoint)
                         && (state.Mode == MoveMode.WallRun || !IsSameWall(wallNormal, wallPoint)))
                {
                    if (state.Mode == MoveMode.Dash) EndDash(ref velocity);
                    Vector3 tangent = Vector3.ProjectOnPlane(Horizontal(velocity), wallNormal) * s.wallJumpTangentKeep;
                    Vector3 horizontal = wallNormal * s.wallJumpPush + tangent + wishDir * (s.runSpeed * s.wallJumpInputBias);
                    float verticalScale = Mathf.Max(1f - s.wallJumpChainFalloff * state.WallJumpChain, s.wallJumpMinVertical);
                    velocity = horizontal + Vector3.up * (s.JumpVelocity * verticalScale);

                    state.WallJumpChain++;
                    state.HasLastWall = true;
                    state.LastWallNormal = wallNormal;
                    state.LastWallPoint = wallPoint;
                    state.AirJumpsLeft = Mathf.Min(state.AirJumpsLeft + 1, s.airJumps);
                    state.WallSlideTimer = 0f;
                    state.Mode = MoveMode.Air;
                    state.JumpBufferTimer = 0f;
                    state.JumpCutAvailable = false;
                    jumpedThisTick = true;
                    result.Events |= MotorEvents.WallJumped;
                }
                else if (input.JumpPressed && state.AirJumpsLeft > 0 && !WillLandWithinBuffer(velocity))
                {
                    if (state.Mode == MoveMode.Dash) EndDash(ref velocity);
                    Vector3 horizontal = Horizontal(velocity);
                    if (hasInput && horizontal.sqrMagnitude > 0.01f)
                    {
                        Vector3 dir = Vector3.RotateTowards(horizontal.normalized, wishDir.normalized, s.airJumpRedirectAngle * Mathf.Deg2Rad, 0f);
                        horizontal = dir * (horizontal.magnitude * s.airJumpSpeedKeep);
                    }
                    velocity = horizontal + Vector3.up * s.AirJumpVelocity;

                    state.AirJumpsLeft--;
                    state.Mode = MoveMode.Air;
                    state.JumpBufferTimer = 0f;
                    state.JumpCutAvailable = true;
                    jumpedThisTick = true;
                    result.Events |= MotorEvents.AirJumped;
                }
            }

            // Mode-specific velocity update.
            if (state.Mode == MoveMode.Dash)
            {
                state.DashTimer -= dt;
                velocity = state.DashDirection * s.dashSpeed;
                if (state.DashTimer <= 0f) EndDash(ref velocity);
            }
            else if (state.Grounded)
            {
                UpdateGroundMode(input, ref velocity, ref result);
                if (state.Mode == MoveMode.Slide) ApplySlide(wishDir, hasInput, ref velocity, dt);
                else ApplyGroundMove(wishDir, hasInput, ref velocity, dt);
            }
            else
            {
                if (state.Crouched) TryStand();
                if (!UpdateWallRun(input, wishDir, ref velocity, dt, ref result))
                {
                    state.Mode = MoveMode.Air;
                    ApplyAirMove(wishDir, ref velocity, dt);
                    ApplyWallSlide(wishDir, hasInput, ref velocity, dt);
                }
            }

            Vector3 flat = Horizontal(velocity);
            if (flat.magnitude > s.maxHorizontalSpeed)
            {
                flat = flat.normalized * s.maxHorizontalSpeed;
                velocity = new Vector3(flat.x, velocity.y, flat.z);
            }

            MoveAndCollide(ref velocity, input, jumpedThisTick, dt, ref result);

            state.Velocity = velocity;
            state.Position = transform.position;
            return result;
        }

        void TickTimers(in MotorInput input, float dt)
        {
            var s = settings;
            state.TimeSinceGrounded += dt;
            state.TimeSinceLanded += dt;
            state.DashCooldownTimer -= dt;
            state.SlideBoostCooldownTimer -= dt;
            state.JumpBufferTimer = input.JumpPressed ? s.jumpBufferTime : Mathf.Max(0f, state.JumpBufferTimer - dt);

            if (state.DashCharges < s.dashCharges)
            {
                state.DashRechargeTimer += dt;
                if (state.DashRechargeTimer >= s.dashRechargeTime)
                {
                    state.DashRechargeTimer -= s.dashRechargeTime;
                    state.DashCharges++;
                }
            }
            else
            {
                state.DashRechargeTimer = 0f;
            }
        }

        void EndDash(ref Vector3 velocity)
        {
            float speed = Mathf.Max(state.DashEntrySpeed, settings.dashExitSpeed);
            velocity = state.DashDirection * speed;
            state.DashTimer = 0f;
            state.Mode = state.Grounded ? MoveMode.Ground : MoveMode.Air;
        }

        void UpdateGroundMode(in MotorInput input, ref Vector3 velocity, ref MotorTickResult result)
        {
            var s = settings;
            float speed = Vector3.ProjectOnPlane(velocity, state.GroundNormal).magnitude;

            if (input.SlideHeld)
            {
                if (state.Mode == MoveMode.Slide) return;
                if (speed >= s.slideMinStartSpeed)
                {
                    if (state.SlideBoostCooldownTimer <= 0f && speed < s.slideBoostCap)
                    {
                        float boosted = Mathf.Min(speed + s.slideBoost, s.slideBoostCap);
                        velocity *= boosted / Mathf.Max(speed, 0.001f);
                        state.SlideBoostCooldownTimer = s.slideBoostCooldown;
                    }
                    SetCrouched(true);
                    state.Mode = MoveMode.Slide;
                    result.Events |= MotorEvents.SlideStarted;
                }
                else
                {
                    SetCrouched(true);
                    state.Mode = MoveMode.Crouch;
                }
            }
            else if (state.Crouched)
            {
                state.Mode = TryStand() ? MoveMode.Ground : MoveMode.Crouch;
            }
            else
            {
                state.Mode = MoveMode.Ground;
            }
        }

        void ApplyGroundMove(Vector3 wishDir, bool hasInput, ref Vector3 velocity, float dt)
        {
            var s = settings;
            Vector3 normal = state.GroundNormal;
            Vector3 planar = Vector3.ProjectOnPlane(velocity, normal);
            Vector3 wishPlanar = Vector3.ProjectOnPlane(wishDir, normal).normalized * wishDir.magnitude;
            float wishSpeed = state.Crouched ? s.crouchSpeed : s.runSpeed;
            float speed = planar.magnitude;

            if (speed > wishSpeed + 0.01f)
            {
                // Above run speed: keep momentum, bleed only the excess, allow steering.
                Vector3 dir = planar / speed;
                float newSpeed = speed;
                if (state.TimeSinceLanded > s.bhopWindow)
                    newSpeed = Mathf.Max(speed - s.overspeedFriction * dt, wishSpeed);

                if (hasInput)
                {
                    Vector3 wishNorm = wishPlanar.normalized;
                    if (Vector3.Dot(dir, wishNorm) < -0.5f)
                        newSpeed = Mathf.Max(newSpeed - s.groundDeceleration * dt, wishSpeed);
                    else
                        dir = Vector3.RotateTowards(dir, wishNorm, s.groundAcceleration / speed * dt, 0f);
                }
                velocity = dir * newSpeed;
            }
            else
            {
                float rate = hasInput ? s.groundAcceleration : s.groundDeceleration;
                velocity = Vector3.MoveTowards(planar, wishPlanar * wishSpeed, rate * dt);
            }
        }

        void ApplySlide(Vector3 wishDir, bool hasInput, ref Vector3 velocity, float dt)
        {
            var s = settings;
            Vector3 normal = state.GroundNormal;
            Vector3 planar = Vector3.ProjectOnPlane(velocity, normal);
            planar += Vector3.ProjectOnPlane(Vector3.down * s.gravity, normal) * dt;

            float speed = Mathf.Max(0f, planar.magnitude - s.slideFriction * dt);
            Vector3 dir = planar.sqrMagnitude > 0.0001f ? planar.normalized : Vector3.zero;
            if (hasInput && dir != Vector3.zero)
            {
                Vector3 wishPlanar = Vector3.ProjectOnPlane(wishDir, normal).normalized;
                dir = Vector3.RotateTowards(dir, wishPlanar, s.slideTurnRate * Mathf.Deg2Rad * dt, 0f);
            }
            velocity = dir * speed;

            if (speed < s.slideExitSpeed) state.Mode = MoveMode.Crouch;
        }

        void ApplyAirMove(Vector3 wishDir, ref Vector3 velocity, float dt)
        {
            var s = settings;
            if (wishDir.sqrMagnitude > 0.0001f)
            {
                // Quake-style: accelerate toward wishDir up to airMaxSpeed, never cut existing momentum.
                Vector3 wishNorm = wishDir.normalized;
                float current = Vector3.Dot(Horizontal(velocity), wishNorm);
                float add = Mathf.Clamp(s.airMaxSpeed * wishDir.magnitude - current, 0f, s.airAcceleration * dt);
                velocity += wishNorm * add;
            }

            float gravity = s.gravity * (velocity.y < 0f ? s.fallGravityMultiplier : 1f);
            velocity.y = Mathf.Max(velocity.y - gravity * dt, -s.maxFallSpeed);
        }

        /// <summary>
        /// Starts or continues a wall run. Returns false when the player should fall normally.
        /// A wall with the same facing can't be run again until grounded or after running another
        /// wall, so corridor zigzags chain but a single long wall doesn't loop forever.
        /// </summary>
        bool UpdateWallRun(in MotorInput input, Vector3 wishDir, ref Vector3 velocity, float dt, ref MotorTickResult result)
        {
            var s = settings;
            if (!s.wallRunEnabled || input.Move.y < 0.1f) return false;
            if (!ProbeWall(out Vector3 wallNormal, out _)) return false;
            if (Vector3.Dot(wishDir, wallNormal) > 0.5f) return false;

            Vector3 flat = Horizontal(velocity);
            Vector3 tangent = Vector3.ProjectOnPlane(flat, wallNormal);
            float along = tangent.magnitude;

            if (state.Mode == MoveMode.WallRun)
            {
                state.WallRunTimer += dt;
                if (state.WallRunTimer >= s.wallRunMaxDuration || along < s.wallRunMinSpeed * 0.7f) return false;
            }
            else
            {
                if (state.TimeSinceGrounded < s.wallJumpMinAirTime || along < s.wallRunMinSpeed) return false;
                if (Vector3.Dot(flat / Mathf.Max(flat.magnitude, 0.001f), -wallNormal) > 0.8f) return false;
                if (state.HasWallRunLock && Vector3.Angle(wallNormal, state.WallRunLockNormal) < s.sameWallAngle) return false;
                if (ProbeGround(s.wallRunMinHeight, out _)) return false;

                state.Mode = MoveMode.WallRun;
                state.WallRunTimer = 0f;
                state.HasWallRunLock = true;
                state.WallRunLockNormal = wallNormal;
                state.AirJumpsLeft = s.airJumps;
                state.WallJumpChain = 0;
                state.JumpCutAvailable = false;
                velocity.y = Mathf.Clamp(velocity.y, s.wallRunEntryLift, s.wallRunMaxEntryUpSpeed);
                result.Events |= MotorEvents.WallRunStarted;
            }

            state.WallNormal = wallNormal;
            float speed = along < s.wallRunSpeed ? Mathf.Min(along + s.wallRunAcceleration * dt, s.wallRunSpeed) : along;
            float vertical = velocity.y - s.wallRunGravity * dt;
            velocity = tangent / along * speed - wallNormal * s.wallRunStickSpeed + Vector3.up * vertical;
            return true;
        }

        void ApplyWallSlide(Vector3 wishDir, bool hasInput, ref Vector3 velocity, float dt)
        {
            var s = settings;
            if (!hasInput || velocity.y >= 0f || state.WallSlideTimer >= s.wallSlideDuration) return;
            if (state.TimeSinceGrounded < s.wallJumpMinAirTime) return;
            if (!ProbeWall(out Vector3 wallNormal, out _)) return;
            if (Vector3.Dot(wishDir.normalized, -wallNormal) < 0.5f) return;

            state.Mode = MoveMode.WallSlide;
            state.WallNormal = wallNormal;
            state.WallSlideTimer += dt;
            velocity.y = Mathf.Max(velocity.y, -s.wallSlideMaxFallSpeed);
        }

        void MoveAndCollide(ref Vector3 velocity, in MotorInput input, bool jumpedThisTick, float dt, ref MotorTickResult result)
        {
            var s = settings;
            bool wasGrounded = state.Grounded;
            float fallSpeed = Mathf.Max(0f, -velocity.y);

            float feetY = transform.position.y;
            moveHits.Clear();
            // A small downward push keeps the controller in contact with the ground so it can climb steps.
            Vector3 stick = wasGrounded && !jumpedThisTick ? Vector3.down * (GroundStickSpeed * dt) : Vector3.zero;
            controller.Move(velocity * dt + stick);

            float groundMinY = Mathf.Cos(s.maxSlopeAngle * Mathf.Deg2Rad);
            float maxVerticalAfterClip = Mathf.Max(velocity.y, 0f);
            foreach ((Vector3 n, Vector3 point) in moveHits)
            {
                if (n.y >= groundMinY) continue;
                // Low obstacles while grounded are steps the controller climbs, not walls.
                if (wasGrounded && point.y - feetY <= controller.stepOffset + 0.01f) continue;
                // Ceilings only stop upward motion. The controller also touches them while probing
                // for steps under low roofs, which must not eat a slide's horizontal speed.
                if (n.y < -0.1f)
                {
                    if (velocity.y > 0f) velocity.y = 0f;
                    state.JumpCutAvailable = false;
                    continue;
                }
                float into = Vector3.Dot(velocity, n);
                if (into < 0f) velocity -= n * into;
            }
            // Clipping against step edges or steep slopes must never launch the player upward.
            velocity.y = Mathf.Min(velocity.y, maxVerticalAfterClip);

            // Stick to the ground when walking down slopes or over small drops.
            if (wasGrounded && !jumpedThisTick && velocity.y <= 0.1f
                && ProbeGround(s.groundSnapDistance, out RaycastHit snapHit) && IsWalkable(snapHit.normal))
            {
                float gap = snapHit.distance - ProbeLift;
                if (gap > 0.001f) controller.Move(Vector3.down * gap);
            }

            RaycastHit groundHit = default;
            bool grounded = !jumpedThisTick
                            && ProbeGround(0.05f, out groundHit)
                            && IsWalkable(groundHit.normal)
                            && Vector3.Dot(velocity, groundHit.normal) <= 0.5f;

            if (grounded)
            {
                Vector3 groundNormal = SurfaceNormalBelow();
                if (!wasGrounded)
                {
                    result.Events |= MotorEvents.Landed;
                    result.LandingSpeed = fallSpeed;
                    state.TimeSinceLanded = 0f;

                    if (input.SlideHeld && fallSpeed > s.slideLandingMinFallSpeed)
                    {
                        Vector3 flat = Horizontal(velocity);
                        Vector3 dir = flat.sqrMagnitude > 0.01f ? flat.normalized : Quaternion.Euler(0f, input.Yaw, 0f) * Vector3.forward;
                        velocity += dir * (fallSpeed * s.slideLandingConversion);
                    }

                    if (state.Mode is MoveMode.Air or MoveMode.WallSlide or MoveMode.WallRun) state.Mode = MoveMode.Ground;
                }

                velocity = Vector3.ProjectOnPlane(velocity, groundNormal);
                state.GroundNormal = groundNormal;
                state.Grounded = true;
                state.TimeSinceGrounded = 0f;
                state.JumpedSinceGrounded = false;
                state.AirJumpsLeft = s.airJumps;
                state.HasLastWall = false;
                state.WallJumpChain = 0;
                state.WallSlideTimer = 0f;
                state.WallRunTimer = 0f;
                state.HasWallRunLock = false;
            }
            else
            {
                state.Grounded = false;
                state.GroundNormal = Vector3.up;
                if (state.Mode is MoveMode.Ground or MoveMode.Crouch or MoveMode.Slide) state.Mode = MoveMode.Air;
            }
        }

        void OnControllerColliderHit(ControllerColliderHit hit) => moveHits.Add((hit.normal, hit.point));

        // ---------- Queries ----------

        bool ProbeGround(float extraDistance, out RaycastHit closest)
        {
            Vector3 origin = transform.position + Vector3.up * (controller.radius + ProbeLift);
            float radius = controller.radius * 0.95f;
            float distance = ProbeLift + controller.skinWidth + extraDistance;
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, hitBuffer, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return Closest(count, out closest);
        }

        /// <summary>
        /// Face normal straight below the capsule center. Sphere casts return rounded normals on
        /// step edges, which would tilt the velocity upward; standing on an edge counts as flat.
        /// </summary>
        Vector3 SurfaceNormalBelow()
        {
            float distance = ProbeLift + controller.skinWidth + settings.groundSnapDistance + 0.1f;
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * ProbeLift, Vector3.down, hitBuffer, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return Closest(count, out RaycastHit hit) && IsWalkable(hit.normal) ? hit.normal : Vector3.up;
        }

        bool ProbeWall(out Vector3 normal, out Vector3 point)
        {
            var s = settings;
            normal = Vector3.zero;
            point = Vector3.zero;

            float radius = controller.radius * 0.9f;
            Vector3 bottom = transform.position + Vector3.up * (controller.radius + 0.2f);
            Vector3 top = transform.position + Vector3.up * Mathf.Max(controller.height - controller.radius, controller.radius + 0.2f);
            float reach = s.wallCheckDistance + controller.radius - radius + controller.skinWidth;

            int nearby = Physics.OverlapCapsuleNonAlloc(bottom, top, radius + reach, overlapBuffer,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool anyOther = false;
            for (int i = 0; i < nearby; i++)
                if (overlapBuffer[i] != controller) { anyOther = true; break; }
            if (!anyOther) return false;

            float maxNormalY = Mathf.Sin(s.wallMaxNormalTilt * Mathf.Deg2Rad);
            float best = float.MaxValue;
            for (int d = 0; d < WallProbeDirections; d++)
            {
                Vector3 dir = Quaternion.Euler(0f, d * (360f / WallProbeDirections), 0f) * Vector3.forward;
                int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, dir, hitBuffer, reach,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = hitBuffer[i];
                    if (hit.collider == controller || hit.distance <= 0f || hit.distance >= best) continue;
                    if (Mathf.Abs(hit.normal.y) > maxNormalY) continue;
                    best = hit.distance;
                    normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                    point = hit.point;
                }
            }
            return best < float.MaxValue;
        }

        bool IsSameWall(Vector3 normal, Vector3 point)
        {
            if (!state.HasLastWall) return false;
            var s = settings;
            bool sameFacing = Vector3.Angle(normal, state.LastWallNormal) < s.sameWallAngle;
            bool close = Horizontal(point - state.LastWallPoint).magnitude < s.sameWallDistance;
            return sameFacing && close;
        }

        bool WillLandWithinBuffer(Vector3 velocity)
        {
            if (velocity.y >= 0f) return false;
            float drop = -velocity.y * settings.jumpBufferTime;
            return ProbeGround(drop, out RaycastHit hit) && IsWalkable(hit.normal);
        }

        bool TryStand()
        {
            if (!state.Crouched) return true;
            float radius = controller.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * (controller.radius + 0.05f);
            Vector3 top = transform.position + Vector3.up * (settings.standHeight - controller.radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapBuffer,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlapBuffer[i] != controller) return false;

            SetCrouched(false);
            return true;
        }

        void SetCrouched(bool crouched)
        {
            if (state.Crouched == crouched) return;
            state.Crouched = crouched;
            SetCapsuleHeight(crouched ? settings.crouchHeight : settings.standHeight);
        }

        void SetCapsuleHeight(float height)
        {
            controller.height = height;
            controller.center = Vector3.up * (height * 0.5f);
        }

        bool Closest(int count, out RaycastHit closest)
        {
            closest = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == controller || hit.distance <= 0f || hit.distance >= best) continue;
                best = hit.distance;
                closest = hit;
            }
            return best < float.MaxValue;
        }

        bool IsWalkable(Vector3 normal) => Vector3.Angle(normal, Vector3.up) <= settings.maxSlopeAngle;

        static Vector3 Horizontal(Vector3 v) => new(v.x, 0f, v.z);
    }
}
