using UnityEngine;

namespace AwesomeBalls.Combat
{
    /// <summary>Everything a single projectile needs, resolved from the shooter's stats at fire time.</summary>
    public struct ShotData
    {
        public int Damage;
        public float Speed;
        public float Radius;
        public float Lifetime;
        public int Bounces;
        public float ExplosionRadius;
    }

    /// <summary>
    /// Physical projectile (GDD 6.2), not a networked object: every peer simulates its own copy.
    /// Hits are decided by the shooter's own copy ("favor the shooter": what you see is what you hit);
    /// the copies on other peers are visuals. Supports ricochets (+10% damage per bounce) and
    /// explosions on impact (cards, GDD 7.4).
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        internal const float BounceDamageBonus = 1.1f;
        const int MaxCastsPerStep = 4;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        [SerializeField] Renderer body;
        [SerializeField] TrailRenderer trail;

        Weapon owner;
        Collider ownerCollider;
        Vector3 direction;
        ShotData shot;
        bool reportsHits;
        Color color;

        public void Launch(Weapon shooter, Collider shooterCollider, Vector3 dir, ShotData data,
            bool isShooterCopy, float catchUp, Color tint)
        {
            owner = shooter;
            ownerCollider = shooterCollider;
            direction = dir.normalized;
            shot = data;
            reportsHits = isShooterCopy;
            color = tint;

            transform.localScale = Vector3.one * (shot.Radius * 2f);
            trail.widthMultiplier = Mathf.Max(0.16f, shot.Radius * 1.3f);
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            body.SetPropertyBlock(block);
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);

            Advance(catchUp);
        }

        void Update() => Advance(Time.deltaTime);

        void Advance(float dt)
        {
            if (dt <= 0f || !this) return;

            Vector3 position = transform.position;
            float remaining = shot.Speed * dt;

            for (int cast = 0; cast < MaxCastsPerStep && remaining > 0f; cast++)
            {
                if (!CastNearest(position, remaining, out RaycastHit hit))
                {
                    position += direction * remaining;
                    remaining = 0f;
                    break;
                }

                position += direction * hit.distance;
                Health target = hit.collider.GetComponentInParent<Health>();
                bool canBounce = target == null && shot.Bounces > 0 && hit.normal != Vector3.zero;
                if (!canBounce)
                {
                    Impact(position, target);
                    return;
                }

                direction = Vector3.Reflect(direction, hit.normal);
                shot.Bounces--;
                shot.Damage = Mathf.RoundToInt(shot.Damage * BounceDamageBonus);
                remaining -= hit.distance;
                position += hit.normal * 0.02f;
            }

            transform.position = position;
            shot.Lifetime -= dt;
            if (shot.Lifetime <= 0f) Destroy(gameObject);
        }

        bool CastNearest(Vector3 from, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.SphereCastNonAlloc(from, shot.Radius, direction, Hits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider == ownerCollider || Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                nearest = Hits[i];
            }
            return best < float.MaxValue;
        }

        void Impact(Vector3 point, Health target)
        {
            transform.position = point;
            if (reportsHits && owner != null) owner.OnOwnProjectileImpact(target, point, direction, shot);
            if (shot.ExplosionRadius > 0f) Explosion.Spawn(point, shot.ExplosionRadius, color, body.sharedMaterial);
            Destroy(gameObject);
        }
    }
}
