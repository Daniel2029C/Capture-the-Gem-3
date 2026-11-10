using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>Archer tuning. Durations are in ticks, speeds in px per update.</summary>
    [System.Serializable]
    public sealed class ArcherStats
    {
        public SprintStats Sprint = new();

        [Header("Light arrows")]
        public int MaxLightCharges = 2;
        /// <summary>Ticks to regain one light charge.</summary>
        public int LightRegenTicks = 240;
        /// <summary>Minimum ticks between two light arrows.</summary>
        public int LightIntervalTicks = 30;
        /// <summary>
        /// Small delay when the kind of shot changes: after an Ember/Gleam arrow, the first Light arrow waits this many
        /// ticks, and after a Light arrow, the first Ember/Gleam draw does. Repeating the same kind is not delayed by it.
        /// </summary>
        public int SwitchDelayTicks = 20;
        public int LightDamage = 20;
        public float LightSpeed = 22f;
        public float LightSpawnOffsetPx = 2f * 1.41421356f;

        [Header("Luminite arrow")]
        /// <summary>Ticks before the Luminite arrow can be armed again, counted from the shot.</summary>
        public int LuminiteCooldownTicks = 23 * 60;
        public float LuminiteChargePerTick = 0.333f;
        /// <summary>Added to the bow's speed multiplier.</summary>
        public float LuminiteSpeedBonus = 1.5f;
        /// <summary>Added to the bow's damage before the power multiplier.</summary>
        public int LuminiteDamageBonus = 10;
        /// <summary>Running speed multiplier while drawing it.</summary>
        public float LuminiteDrawRunScale = 0.5f;
        public int LuminiteExtraUpdates = 1;
    }

    /// <summary>
    /// State the Archer's three abilities and its bow share: the arrow mode (Normal = Ember on left click and
    /// Gleam on right click; Light = Light arrows on left click only), the light arrow charges, the delay that
    /// separates the two kinds of shot, and the armed Luminite arrow.
    /// </summary>
    public sealed class ArcherKit
    {
        public readonly ArcherStats Stats = new();

        public bool LightMode { get; private set; }
        public float LightCharges { get; private set; }
        public bool LuminiteArmed { get; private set; }
        public int LuminiteCooldown { get; private set; }

        enum ShotKind { None, Light, Heavy }

        int m_NextLightTick;
        ShotKind m_LastKind;
        int m_LastShotTick;

        public ArcherKit() => Reset();

        public void Reset()
        {
            LightMode = false;
            LightCharges = Stats.MaxLightCharges;
            LuminiteArmed = false;
            LuminiteCooldown = 0;
            m_NextLightTick = 0;
            m_LastKind = ShotKind.None;
            m_LastShotTick = 0;
        }

        /// <summary>Regeneration and cooldowns; once per tick while alive.</summary>
        public void Tick()
        {
            if (LightCharges < Stats.MaxLightCharges)
            {
                LightCharges = Mathf.Min(Stats.MaxLightCharges, LightCharges + 1f / Stats.LightRegenTicks);
            }

            // The Luminite cooldown only runs once the arrow has been used.
            if (!LuminiteArmed && LuminiteCooldown > 0) LuminiteCooldown--;
        }

        public void ToggleMode() => LightMode = !LightMode;

        public bool CanDrawHeavy(int tick) => m_LastKind != ShotKind.Light || tick >= m_LastShotTick + Stats.SwitchDelayTicks;

        public bool CanFireLight(int tick) =>
            LightMode && LightCharges >= 1f && tick >= m_NextLightTick
            && (m_LastKind != ShotKind.Heavy || tick >= m_LastShotTick + Stats.SwitchDelayTicks);

        public void OnLightFired(int tick)
        {
            LightCharges -= 1f;
            m_NextLightTick = tick + Stats.LightIntervalTicks;
            m_LastKind = ShotKind.Light;
            m_LastShotTick = tick;
        }

        /// <summary>An Ember or Gleam arrow was just loosed: the first Light arrow after it waits out the switch delay.</summary>
        public void OnHeavyFired(int tick)
        {
            m_LastKind = ShotKind.Heavy;
            m_LastShotTick = tick;
        }

        public bool CanArmLuminite => !LuminiteArmed && LuminiteCooldown <= 0;

        public void ArmLuminite() => LuminiteArmed = true;

        public void OnLuminiteFired()
        {
            LuminiteArmed = false;
            LuminiteCooldown = Stats.LuminiteCooldownTicks;
        }
    }

    /// <summary>Ability 2: toggles between Normal (Ember/Gleam) and Light arrow mode.</summary>
    public sealed class ArrowModeAbility : Ability
    {
        readonly ArcherKit m_Kit;

        public ArrowModeAbility(ArcherKit kit) => m_Kit = kit;

        public override string Name => "Arrows";
        public override float Fill => m_Kit.LightMode ? m_Kit.LightCharges / m_Kit.Stats.MaxLightCharges : 1f;
        public override bool IsActive => m_Kit.LightMode;
        public override string Status => m_Kit.LightMode ? $"Light {Mathf.FloorToInt(m_Kit.LightCharges)}/{m_Kit.Stats.MaxLightCharges}" : "Ember / Gleam";

        public override void Tick(in AbilityContext context, bool pressed, bool held)
        {
            // Switching mid-draw would leave a half-drawn arrow, so it waits until the bow is let go.
            if (pressed && !context.Player.ItemUser.Bow.IsCharging) m_Kit.ToggleMode();
        }
    }

    /// <summary>Ability 3: arms the Luminite arrow, which the next draw (either button) shoots.</summary>
    public sealed class LuminiteAbility : Ability
    {
        readonly ArcherKit m_Kit;

        public LuminiteAbility(ArcherKit kit) => m_Kit = kit;

        public override string Name => "Luminite";
        public override bool IsActive => m_Kit.LuminiteArmed;

        public override float Fill => m_Kit.LuminiteArmed
            ? 1f
            : 1f - (float)m_Kit.LuminiteCooldown / m_Kit.Stats.LuminiteCooldownTicks;

        public override string Status => m_Kit.LuminiteArmed
            ? "Armed"
            : m_Kit.LuminiteCooldown > 0 ? $"{Mathf.CeilToInt(m_Kit.LuminiteCooldown / 60f)}s" : "Ready";

        public override void Tick(in AbilityContext context, bool pressed, bool held)
        {
            if (pressed && m_Kit.CanArmLuminite) m_Kit.ArmLuminite();
        }
    }
}
