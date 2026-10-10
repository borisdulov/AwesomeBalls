using UnityEngine;

namespace AwesomeBalls.Combat
{
    /// <summary>Visual-only expanding sphere for explosive projectiles. Damage is applied by the weapon on the server.</summary>
    public sealed class Explosion : MonoBehaviour
    {
        const float Duration = 0.25f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        float radius;
        float age;
        Renderer sphere;
        MaterialPropertyBlock block;
        Color color;

        public static void Spawn(Vector3 position, float radius, Color color, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Explosion";
            go.transform.position = position;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var explosion = go.AddComponent<Explosion>();
            explosion.radius = radius;
            explosion.sphere = renderer;
            explosion.color = color;
            explosion.block = new MaterialPropertyBlock();
            explosion.Update();
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Duration);
            transform.localScale = Vector3.one * (radius * 2f * Mathf.Lerp(0.3f, 1f, 1f - (1f - t) * (1f - t)));
            block.SetColor(BaseColorId, Color.Lerp(Color.white, color, t) * (1f - t * 0.6f));
            sphere.SetPropertyBlock(block);
            if (age >= Duration) Destroy(gameObject);
        }
    }
}
