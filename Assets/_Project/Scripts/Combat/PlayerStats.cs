using System;
using System.Collections.Generic;
using AwesomeBalls.Cards;
using AwesomeBalls.Movement;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace AwesomeBalls.Combat
{
    /// <summary>
    /// The player's cards and the stats they produce (GDD 7.2). The server sets the card list on spawn;
    /// every peer rebuilds the same stats from it, so visuals (projectile size, bounces) match everywhere.
    /// </summary>
    public sealed class PlayerStats : NetworkBehaviour
    {
        [SerializeField] CardDatabase database;
        [SerializeField] MovementSettings baseMovement;
        [SerializeField] WeaponSettings baseWeapon;
        [SerializeField] int baseMaxHealth = 100;
        [SerializeField] PlayerMotor motor;

        readonly SyncList<int> cards = new();

        public IReadOnlyList<int> Cards => cards;
        public MovementSettings Movement { get; private set; }
        public WeaponSettings Weapon { get; private set; }
        public int MaxHealth { get; private set; }
        /// <summary>HP per second after a few seconds without taking damage.</summary>
        public float Regeneration { get; private set; }
        /// <summary>Fraction of dealt damage returned as health.</summary>
        public float Lifesteal { get; private set; }
        public int ProjectileCount { get; private set; }
        public int Bounces { get; private set; }
        public float ExplosionRadius { get; private set; }

        public event Action Rebuilt;

        void Awake()
        {
            cards.OnChange += (_, _, _, _, _) => Rebuild();
            Rebuild();
        }

        void OnDestroy()
        {
            if (Movement != null) Destroy(Movement);
            if (Weapon != null) Destroy(Weapon);
        }

        /// <summary>Server: assign the cards this player owns for the round.</summary>
        public void ServerSetCards(IEnumerable<int> cardIds)
        {
            cards.Clear();
            cards.AddRange(cardIds);
            Rebuild();
        }

        void Rebuild()
        {
            StatBlock s = database.BuildStats(cards);

            if (Movement != null) Destroy(Movement);
            Movement = Instantiate(baseMovement);
            float speedScale = s.Evaluate(StatId.RunSpeed, baseMovement.runSpeed) / baseMovement.runSpeed;
            Movement.runSpeed = baseMovement.runSpeed * speedScale;
            Movement.airMaxSpeed = baseMovement.airMaxSpeed * speedScale;
            Movement.airJumps = s.EvaluateInt(StatId.AirJumps, baseMovement.airJumps);
            Movement.dashCharges = s.EvaluateInt(StatId.DashCharges, baseMovement.dashCharges);

            if (Weapon != null) Destroy(Weapon);
            Weapon = Instantiate(baseWeapon);
            Weapon.damage = s.EvaluateInt(StatId.Damage, baseWeapon.damage);
            Weapon.fireRate = s.Evaluate(StatId.FireRate, baseWeapon.fireRate);
            Weapon.magazineSize = s.EvaluateInt(StatId.MagazineSize, baseWeapon.magazineSize);
            Weapon.reloadTime = s.Evaluate(StatId.ReloadTime, baseWeapon.reloadTime);
            Weapon.spread = s.Evaluate(StatId.Spread, baseWeapon.spread);
            Weapon.knockback = s.Evaluate(StatId.Knockback, baseWeapon.knockback);
            Weapon.projectileSpeed = s.Evaluate(StatId.ProjectileSpeed, baseWeapon.projectileSpeed);
            Weapon.projectileRadius = s.Evaluate(StatId.ProjectileSize, baseWeapon.projectileRadius);

            ProjectileCount = s.EvaluateInt(StatId.ProjectileCount, 1);
            Bounces = s.EvaluateInt(StatId.Bounces, 0);
            ExplosionRadius = s.Evaluate(StatId.ExplosionRadius, 0f);
            MaxHealth = s.EvaluateInt(StatId.MaxHealth, baseMaxHealth);
            Regeneration = s.Evaluate(StatId.Regeneration, 0f);
            Lifesteal = s.Evaluate(StatId.Lifesteal, 0f);

            motor.SetSettings(Movement);
            Rebuilt?.Invoke();
        }
    }
}
