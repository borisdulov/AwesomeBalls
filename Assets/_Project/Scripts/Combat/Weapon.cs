using System;
using System.Collections.Generic;
using AwesomeBalls.Networking;
using AwesomeBalls.Player;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace AwesomeBalls.Combat
{
    /// <summary>
    /// The base Blaster (GDD 6.2), modified by card stats. The owner fires instantly and its own
    /// projectiles decide hits ("favor the shooter": remote players are seen with latency, so only
    /// the shooter's view matches what they aimed at). The owner reports hits; the server validates
    /// and applies damage. Every other peer spawns visual copies advanced by the latency (GDD 9.3).
    /// </summary>
    public sealed class Weapon : NetworkBehaviour
    {
        const float ExplosionDamageFraction = 0.4f;
        const float SelfExplosionFraction = 0.5f;
        const float ExplosionKnockback = 9f;

        static readonly Collider[] ExplosionOverlap = new Collider[32];
        static readonly HashSet<Health> ExplosionTargets = new();

        [SerializeField] Projectile projectilePrefab;
        [SerializeField] PlayerController controller;
        [SerializeField] PlayerStats stats;
        [SerializeField] Health health;
        [SerializeField] Collider ownCollider;

        float cooldown;
        float reloadTimer;

        WeaponSettings Settings => stats.Weapon;

        public int Ammo { get; private set; }
        public int MagazineSize => Settings.magazineSize;
        public bool IsReloading => reloadTimer > 0f;
        public float ReloadProgress => IsReloading ? 1f - reloadTimer / Settings.reloadTime : 1f;

        /// <summary>Owner only: the server confirmed one of our shots hit. Args: damage, killed.</summary>
        public event Action<int, bool> HitConfirmed;

        Color ShotColor => PlayerNetwork.ColorFor(OwnerId);

        void Awake() => stats.Rebuilt += Refill;

        public override void OnStartClient()
        {
            base.OnStartClient();
            Refill();
        }

        void Refill()
        {
            Ammo = Settings.magazineSize;
            reloadTimer = 0f;
        }

        void Update()
        {
            if (!IsOwner || health.IsDead) return;

            float dt = Time.deltaTime;
            cooldown = Mathf.Max(0f, cooldown - dt);
            if (IsReloading)
            {
                reloadTimer -= dt;
                if (reloadTimer <= 0f)
                {
                    reloadTimer = 0f;
                    Ammo = Settings.magazineSize;
                }
            }

            if (!controller.HasControl) return;
            if (controller.Input.ReloadPressed && Ammo < Settings.magazineSize) StartReload();
            if (!controller.Input.FireHeld || IsReloading || cooldown > 0f) return;

            if (Ammo > 0) Fire();
            else StartReload();
        }

        void StartReload()
        {
            if (!IsReloading) reloadTimer = Settings.reloadTime;
        }

        void Fire()
        {
            cooldown += 1f / Settings.fireRate;
            Ammo--;
            if (Ammo == 0) StartReload();

            Transform eye = controller.Camera.transform;
            Vector3[] directions = Aim(eye.rotation);
            Vector3 origin = eye.position;
            uint tick = TimeManager.Tick;

            SpawnVolley(origin, directions, 0f, shooterCopy: true);
            if (IsServerStarted) ObserversFire(origin, directions, tick);
            else ServerFire(origin, directions, tick);
        }

        /// <summary>One direction per projectile: random cone for one, an even horizontal fan for several.</summary>
        Vector3[] Aim(Quaternion look)
        {
            int count = stats.ProjectileCount;
            float spread = Settings.spread;
            var directions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 jitter = UnityEngine.Random.insideUnitCircle * (count == 1 ? spread : spread * 0.25f);
                float fan = count == 1 ? 0f : Mathf.Lerp(-spread, spread, i / (float)(count - 1));
                directions[i] = look * Quaternion.Euler(jitter.y, fan + jitter.x, 0f) * Vector3.forward;
            }
            return directions;
        }

        [ServerRpc]
        void ServerFire(Vector3 origin, Vector3[] directions, uint tick)
        {
            if (health.IsDead || directions.Length > 12) return;
            SpawnVolley(origin, directions, CatchUp(tick), shooterCopy: false);
            ObserversFire(origin, directions, tick);
        }

        [ObserversRpc(ExcludeOwner = true, ExcludeServer = true)]
        void ObserversFire(Vector3 origin, Vector3[] directions, uint tick)
        {
            SpawnVolley(origin, directions, CatchUp(tick), shooterCopy: false);
        }

        float CatchUp(uint tick) => Mathf.Clamp((float)TimeManager.TimePassed(tick), 0f, Settings.maxCatchUp);

        void SpawnVolley(Vector3 origin, Vector3[] directions, float catchUp, bool shooterCopy)
        {
            var shot = new ShotData
            {
                Damage = Settings.damage,
                Speed = Settings.projectileSpeed,
                Radius = Settings.projectileRadius,
                Lifetime = Settings.projectileLifetime,
                Bounces = stats.Bounces,
                ExplosionRadius = stats.ExplosionRadius,
            };
            foreach (Vector3 direction in directions)
            {
                Projectile projectile = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(direction));
                projectile.Launch(this, ownCollider, direction, shot, shooterCopy, catchUp, ShotColor);
            }
        }

        /// <summary>Shooter: one of our own projectiles hit something (or nothing, for explosions).</summary>
        internal void OnOwnProjectileImpact(Health target, Vector3 point, Vector3 direction, ShotData shot)
        {
            if (IsServerStarted) ServerProjectileImpact(target, point, direction, shot.Damage, shot.ExplosionRadius);
            else if (target != null || shot.ExplosionRadius > 0f)
                ServerReportImpact(target != null ? target.NetworkObject : null, point, direction, shot.Damage);
        }

        [ServerRpc]
        void ServerReportImpact(NetworkObject target, Vector3 point, Vector3 direction, int damage)
        {
            // Friends-only game (GDD 9.6): trust the shooter's hit, but never beyond what its cards allow.
            int maxDamage = Mathf.CeilToInt(Settings.damage * Mathf.Pow(Projectile.BounceDamageBonus, stats.Bounces));
            damage = Mathf.Clamp(damage, 1, maxDamage);
            Health victim = target != null ? target.GetComponent<Health>() : null;
            ServerProjectileImpact(victim, point, direction.normalized, damage, stats.ExplosionRadius);
        }

        /// <summary>Server: apply a hit on <paramref name="directTarget"/> and the explosion around <paramref name="point"/>.</summary>
        void ServerProjectileImpact(Health directTarget, Vector3 point, Vector3 direction, int shotDamage, float explosionRadius)
        {
            if (directTarget != null && directTarget != health)
                ApplyHit(directTarget, shotDamage, direction * Settings.knockback);

            if (explosionRadius <= 0f) return;

            ExplosionTargets.Clear();
            int count = Physics.OverlapSphereNonAlloc(point, explosionRadius, ExplosionOverlap,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Health target = ExplosionOverlap[i].GetComponentInParent<Health>();
                if (target != null && target != directTarget) ExplosionTargets.Add(target);
            }

            foreach (Health target in ExplosionTargets)
            {
                float fraction = ExplosionDamageFraction * (target == health ? SelfExplosionFraction : 1f);
                int damage = Mathf.Max(1, Mathf.RoundToInt(shotDamage * fraction));
                Vector3 away = (target.transform.position + Vector3.up * 0.9f - point).normalized;
                ApplyHit(target, damage, (away + Vector3.up * 0.4f).normalized * ExplosionKnockback);
            }
        }

        void ApplyHit(Health target, int damage, Vector3 knockback)
        {
            if (target.IsDead) return;
            bool killed = target.Current <= damage;
            target.ServerDamage(damage, this, knockback);
            if (target == health) return;

            if (stats.Lifesteal > 0f) health.ServerHeal(Mathf.RoundToInt(damage * stats.Lifesteal));
            TargetHitConfirmed(Owner, damage, killed);
        }

        [TargetRpc]
        void TargetHitConfirmed(NetworkConnection connection, int damage, bool killed) => HitConfirmed?.Invoke(damage, killed);
    }
}
