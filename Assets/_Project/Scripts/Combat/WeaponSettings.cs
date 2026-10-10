using UnityEngine;

namespace AwesomeBalls.Combat
{
    /// <summary>Base "Blaster" stats (GDD 6.2). Cards will modify these later via a stats container.</summary>
    [CreateAssetMenu(menuName = "AwesomeBalls/Weapon Settings", fileName = "WeaponSettings")]
    public sealed class WeaponSettings : ScriptableObject
    {
        [Header("Damage")]
        public int damage = 20;
        [Tooltip("Speed added to the target along the shot direction, m/s.")]
        public float knockback = 3f;

        [Header("Fire")]
        [Tooltip("Shots per second while the trigger is held.")]
        public float fireRate = 4f;
        public int magazineSize = 6;
        public float reloadTime = 1.5f;
        [Tooltip("Random cone half-angle, degrees. Does not grow while moving.")]
        public float spread = 0.6f;

        [Header("Projectile")]
        public float projectileSpeed = 90f;
        public float projectileRadius = 0.12f;
        public float projectileLifetime = 2f;
        [Tooltip("Max latency compensation when spawning a remote shot, seconds.")]
        public float maxCatchUp = 0.3f;
    }
}
