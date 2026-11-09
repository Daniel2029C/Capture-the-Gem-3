using UnityEngine;
using UnityEngine.InputSystem;
using Blocks.Audio;
using Blocks.Character;
using Blocks.Extras;

namespace Blocks.Movement
{
    /// <summary>
    /// Player locomotion modelled on Terraria's defaults, converted to world units (1 block = 1 unit = 16 px,
    /// 60 ticks/s): acceleration-based running with fast braking, no sprint, and a jump that keeps its full
    /// launch speed for as long as Jump is held (up to a limit) before gravity takes over. No coyote time,
    /// jump buffering, apex hang or fall-gravity boost. Gravity and terminal velocity live on the
    /// BuildingBlocksCharacter. Replaces the separate Walk/Jump example abilities; don't run those alongside this one.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerMovementAbility : MovementAbility
    {
        [Header("Input")]
        [SerializeField] InputActionReference moveAction;
        [SerializeField] InputActionReference jumpAction;

        [Header("Run (Terraria: 3 px/tick max, +0.08 accel, 0.2 slowdown)")]
        [Tooltip("Top horizontal speed in units/second. Terraria: 3 px/tick = 11.25.")]
        [SerializeField] float maxRunSpeed = 11.25f;
        [Tooltip("Speed gained per second while holding a direction. Terraria: 0.08 px/tick² = 18.")]
        [SerializeField] float runAcceleration = 18f;
        [Tooltip("Speed lost per second when braking: releasing input on the ground, or pressing the opposite way. Terraria: 0.2 px/tick² = 45.")]
        [SerializeField] float runSlowdown = 45f;

        [Header("Jump (Terraria: 5.01 px/tick for up to 15 ticks)")]
        [Tooltip("Upward speed in units/second while the jump is held. Terraria: 5.01 px/tick = 18.79.")]
        [SerializeField] float jumpSpeed = 18.79f;
        [Tooltip("How long, in seconds, holding Jump sustains the full launch speed. Terraria: 15 ticks = 0.25.")]
        [SerializeField] float jumpHoldTime = 0.25f;
        [Tooltip("Grace period after walking off a ledge where a jump still counts, in seconds. Terraria has none.")]
        [SerializeField] float coyoteTime = 0f;
        [Tooltip("A jump pressed this early before landing is remembered and fires on touchdown. Terraria has none.")]
        [SerializeField] float jumpBufferTime = 0f;
        [SerializeField] float jumpCooldown = 0f;

        [Header("Stat Cost")]
        [SerializeField] StatType staminaStat = StatType.Stamina;
        [SerializeField] float jumpCost;

        [Header("Effects")]
        [Tooltip("Spawned at the feet when a jump starts — e.g. a dust puff.")]
        [SerializeField] OneShotVfx jumpVfxPrefab;
        [Tooltip("Spawned at the feet on landing.")]
        [SerializeField] OneShotVfx landVfxPrefab;
        [Tooltip("Spawned at the feet on an interval while running on the ground.")]
        [SerializeField] OneShotVfx runDustVfxPrefab;
        [SerializeField] float runDustInterval = 0.25f;

        [Header("Audio")]
        [Tooltip("Plays the movement sounds. Leave empty to use the AudioSource on this GameObject.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Footstep sounds — one is picked at random per step, so a handful keeps walking from sounding looped.")]
        [SerializeField] AudioClip[] footstepClips;
        [Tooltip("Ground covered between footsteps, in units. Measuring in distance rather than time keeps the steps in sync with the legs whether walking or sprinting.")]
        [SerializeField] float footstepStride = 3.5f;
        [Tooltip("1 is the clip's own level; above that boosts it. Footstep recordings are usually mastered far quieter than music, so they need the boost to be heard at all.")]
        [SerializeField][Range(0f, 5f)] float footstepVolume = 3f;
        [Tooltip("Played once when a jump starts.")]
        [SerializeField] AudioClip jumpClip;
        [SerializeField][Range(0f, 5f)] float jumpVolume = 2.5f;
        [Tooltip("Played once on touchdown.")]
        [SerializeField] AudioClip landClip;
        [SerializeField][Range(0f, 5f)] float landVolume = 2.5f;
        [Tooltip("Each sound is pitched up or down by up to this much, so repeats don't sound identical. 0 = no variation.")]
        [SerializeField][Range(0f, 0.5f)] float pitchVariation = 0.08f;

        InputHandle m_MoveHandle;
        InputHandle m_JumpHandle;
        float m_HorizontalSpeed;
        float m_JumpHoldLeft;
        float m_RunDustTimer;
        float m_DistanceSinceFootstep;
        AudioHelper m_Audio;

        protected override void OnInitialize()
        {
            m_MoveHandle = BindInput(moveAction);
            m_JumpHandle = BindInput(jumpAction, ConfigureJump);

            Character.OnLanded += HandleLanded;

            SetUpAudio();
            WarnAboutCompetingAbilities();
        }

        protected override void OnCleanup()
        {
            if (Character != null)
            {
                Character.OnLanded -= HandleLanded;
            }
        }

        protected override void OnRespawn()
        {
            // Momentum survives elimination, and TickLocomotion decelerates from wherever it left off
            // rather than starting at rest, so without this a sprint into a pit respawns mid-slide.
            // m_DistanceSinceFootstep is left alone: TickFootsteps re-primes it on the first tick at
            // rest, and to half a stride rather than to zero.
            m_HorizontalSpeed = 0f;
            m_JumpHoldLeft = 0f;
            m_RunDustTimer = 0f;
        }

        protected override void OnUpdate()
        {
            float deltaTime = Time.deltaTime;

            TickJumpHold(deltaTime);
            TickLocomotion(deltaTime);
        }

        void TickLocomotion(float deltaTime)
        {
            float inputX = m_MoveHandle.GetValue<Vector2>().x;
            float direction = Mathf.Abs(inputX) > 0.01f ? Mathf.Sign(inputX) : 0f;

            float speed = m_HorizontalSpeed;

            if (direction != 0f)
            {
                // Terraria: while moving against the pressed direction, brake with the (larger) slowdown
                // until reversed; otherwise accelerate up to the top speed. Both work in the air too.
                // Speed already above the cap (a knockback, say) is left to decay by friction, not clamped.
                float along = speed * direction;
                if (along < 0f)
                {
                    float brake = runSlowdown * deltaTime;
                    speed = along > -brake ? 0f : speed + direction * brake;
                }
                else if (along < maxRunSpeed)
                {
                    speed = direction * Mathf.Min(along + runAcceleration * deltaTime, maxRunSpeed);
                }
            }
            else if (Character.IsGrounded)
            {
                // Friction only acts on the ground; releasing input in mid-air keeps the momentum.
                speed = Mathf.MoveTowards(speed, 0f, runSlowdown * deltaTime);
            }

            m_HorizontalSpeed = speed;
            Character.AddMovement(new Vector2(m_HorizontalSpeed, 0f));

            TickRunDust(deltaTime);
            TickFootsteps(deltaTime);
        }

        void TickJumpHold(float deltaTime)
        {
            if (m_JumpHoldLeft <= 0f) return;

            if (!m_JumpHandle.IsPressed || Character.IsTouchingCeiling || Character.IsStunned)
            {
                // Released early or bonked a ceiling: gravity takes over from the current speed, no cut.
                m_JumpHoldLeft = 0f;
                if (Character.IsTouchingCeiling && Character.VerticalVelocity > 0f)
                {
                    Character.SetVerticalVelocity(0f);
                }
                return;
            }

            m_JumpHoldLeft -= deltaTime;

            // The character applies gravity after abilities tick; adding this frame's share back keeps the
            // speed pinned at jumpSpeed for the whole hold, as Terraria's per-tick reset does.
            Character.SetVerticalVelocity(jumpSpeed - Character.Gravity * deltaTime);
        }

        void ConfigureJump(InputHandle input)
        {
            input.Performed = PerformJump;
            input.CanExecute = CanJump;
            input.BufferTime = jumpBufferTime;
            input.CooldownTime = jumpCooldown;
        }

        bool CanJump()
        {
            // Character.JumpWithSpeed silently no-ops while stunned or attack-locked; gating here instead
            // lets the input buffer (if enabled) hold the press and fire it the moment the lock clears.
            if (Character.IsStunned || Character.IsAttackMovementLocked) return false;
            if (!Character.HasAtLeast(staminaStat, jumpCost)) return false;

            return Character.IsGroundedOrInCoyote(coyoteTime);
        }

        void PerformJump()
        {
            if (!Character.IsGroundedOrInCoyote(coyoteTime)) return;

            Character.Consume(staminaStat, jumpCost);

            Character.JumpWithSpeed(jumpSpeed);
            SpawnVfx(jumpVfxPrefab, FeetPosition);
            m_Audio.Play(jumpClip, jumpVolume);

            m_JumpHoldLeft = jumpHoldTime;
        }

        void TickRunDust(float deltaTime)
        {
            if (m_RunDustTimer > 0f)
            {
                m_RunDustTimer -= deltaTime;
            }

            bool isRunning = Character.IsGrounded && Mathf.Abs(m_HorizontalSpeed) > maxRunSpeed * 0.8f;
            if (!isRunning || m_RunDustTimer > 0f) return;

            m_RunDustTimer = runDustInterval;

            SpawnVfx(runDustVfxPrefab, FeetPosition);
        }

        void HandleLanded(float impactVelocity)
        {
            SpawnVfx(landVfxPrefab, FeetPosition);
            m_Audio.Play(landClip, landVolume);
        }

        void TickFootsteps(float deltaTime)
        {
            float speed = Mathf.Abs(m_HorizontalSpeed);
            if (!Character.IsGrounded || speed < 0.01f)
            {
                // Start the next stride half-spent so the first step lands shortly after the legs
                // start moving, instead of a full stride of silence every time the player sets off.
                m_DistanceSinceFootstep = footstepStride * 0.5f;
                return;
            }

            m_DistanceSinceFootstep += speed * deltaTime;
            if (m_DistanceSinceFootstep < footstepStride) return;

            m_DistanceSinceFootstep -= footstepStride;
            PlayFootstep();
        }

        void PlayFootstep()
        {
            m_Audio.PlayRandom(footstepClips, footstepVolume);
        }

        void SetUpAudio()
        {
            AudioHelper.WarmUp(jumpClip);
            AudioHelper.WarmUp(landClip);
            AudioHelper.WarmUp(footstepClips);

            bool hasClips = jumpClip != null || landClip != null ||
                            (footstepClips != null && footstepClips.Length > 0);

            m_Audio = new AudioHelper(
                this, audioSource, hasClips,
                clipFieldHint: "'Footstep Clips', 'Jump Clip', 'Land Clip'",
                ownerLabel: Character.name, pitchVariation: pitchVariation);
        }

        void WarnAboutCompetingAbilities()
        {
            MovementAbility[] abilities = Character.GetComponentsInChildren<MovementAbility>();
            foreach (MovementAbility ability in abilities)
            {
                if (ability == this || !ability.enabled) continue;

                Debug.LogWarning(
                    $"[PlayerMovementAbility] {Character.name}: '{ability.GetType().Name}' is also enabled — " +
                    "this ability already handles walk, run and jump, so both will fight for control. Disable one of them.",
                    this);
            }
        }

        void OnValidate()
        {
            if (maxRunSpeed < 0f) maxRunSpeed = 0f;
            if (runAcceleration < 0f) runAcceleration = 0f;
            if (runSlowdown < 0f) runSlowdown = 0f;
            if (jumpSpeed < 0f) jumpSpeed = 0f;
            if (jumpHoldTime < 0f) jumpHoldTime = 0f;
            if (coyoteTime < 0f) coyoteTime = 0f;
            if (jumpBufferTime < 0f) jumpBufferTime = 0f;
            if (jumpCooldown < 0f) jumpCooldown = 0f;
            if (jumpCost < 0f) jumpCost = 0f;
            if (runDustInterval < 0.05f) runDustInterval = 0.05f;
            if (footstepStride < 0.1f) footstepStride = 0.1f;
        }
    }
}
