using System;
using System.Collections.Generic;
using UnityEngine;

namespace AwesomeBalls.Cards
{
    /// <summary>Every stat a card can modify (GDD 7.2).</summary>
    public enum StatId
    {
        // Weapon
        Damage,
        FireRate,
        MagazineSize,
        ReloadTime,
        Spread,
        Knockback,
        ProjectileSpeed,
        ProjectileSize,
        ProjectileCount,
        Bounces,
        ExplosionRadius,
        // Defense
        MaxHealth,
        Regeneration,
        Lifesteal,
        // Movement
        RunSpeed,
        AirJumps,
        DashCharges,
    }

    public enum ModifierType
    {
        /// <summary>Flat amount added to the base.</summary>
        Add,
        /// <summary>Percentages sum together: two +50% cards are +100%.</summary>
        Percent,
        /// <summary>Rare strong multipliers, multiplied together.</summary>
        Multiply,
    }

    [Serializable]
    public struct StatModifier
    {
        public StatId stat;
        public ModifierType type;
        [Tooltip("Add: flat value. Percent: 0.5 = +50%, -0.2 = -20%. Multiply: 2 = x2.")]
        public float value;

        public StatModifier(StatId stat, ModifierType type, float value)
        {
            this.stat = stat;
            this.type = type;
            this.value = value;
        }
    }

    /// <summary>
    /// Accumulates modifiers and evaluates final = (base + Σadd) × (1 + Σpercent) × Πmultiply,
    /// then clamps to keep broken synergies playable (GDD 7.2).
    /// </summary>
    public sealed class StatBlock
    {
        static readonly int Count = Enum.GetValues(typeof(StatId)).Length;

        readonly float[] add = new float[Count];
        readonly float[] percent = new float[Count];
        readonly float[] multiply = new float[Count];

        public StatBlock()
        {
            for (int i = 0; i < Count; i++) multiply[i] = 1f;
        }

        public static StatBlock From(IEnumerable<StatModifier> modifiers)
        {
            var block = new StatBlock();
            foreach (StatModifier m in modifiers) block.Apply(m);
            return block;
        }

        public void Apply(StatModifier m)
        {
            int i = (int)m.stat;
            switch (m.type)
            {
                case ModifierType.Add: add[i] += m.value; break;
                case ModifierType.Percent: percent[i] += m.value; break;
                case ModifierType.Multiply: multiply[i] *= m.value; break;
            }
        }

        public float Evaluate(StatId stat, float baseValue)
        {
            int i = (int)stat;
            float value = (baseValue + add[i]) * Mathf.Max(0f, 1f + percent[i]) * multiply[i];
            return Clamp(stat, value);
        }

        public int EvaluateInt(StatId stat, int baseValue) => Mathf.RoundToInt(Evaluate(stat, baseValue));

        static float Clamp(StatId stat, float value) => stat switch
        {
            StatId.Damage => Mathf.Max(1f, value),
            StatId.FireRate => Mathf.Clamp(value, 0.5f, 20f),
            StatId.MagazineSize => Mathf.Clamp(value, 1f, 60f),
            StatId.ReloadTime => Mathf.Clamp(value, 0.2f, 5f),
            StatId.Spread => Mathf.Clamp(value, 0f, 30f),
            StatId.ProjectileSpeed => Mathf.Clamp(value, 20f, 300f),
            StatId.ProjectileSize => Mathf.Clamp(value, 0.03f, 1.5f),
            StatId.ProjectileCount => Mathf.Clamp(value, 1f, 12f),
            StatId.Bounces => Mathf.Clamp(value, 0f, 20f),
            StatId.ExplosionRadius => Mathf.Clamp(value, 0f, 10f),
            StatId.MaxHealth => Mathf.Clamp(value, 10f, 500f),
            StatId.Regeneration => Mathf.Clamp(value, 0f, 100f),
            StatId.Lifesteal => Mathf.Clamp(value, 0f, 2f),
            StatId.RunSpeed => Mathf.Clamp(value, 5f, 18f),
            StatId.AirJumps => Mathf.Clamp(value, 0f, 10f),
            StatId.DashCharges => Mathf.Clamp(value, 0f, 6f),
            _ => Mathf.Max(0f, value),
        };
    }
}
