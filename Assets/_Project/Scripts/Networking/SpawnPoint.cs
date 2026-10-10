using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AwesomeBalls.Networking
{
    /// <summary>Marks a player spawn location in a gameplay scene. Forward (blue axis) is the facing.</summary>
    public sealed class SpawnPoint : MonoBehaviour
    {
        static readonly List<SpawnPoint> Buffer = new();

        /// <summary>Spawn point by index (wraps), ordered by name for stable results.</summary>
        public static Transform Pick(Scene scene, int index)
        {
            Collect(scene);
            if (Buffer.Count == 0) return null;
            return Buffer[Mathf.Abs(index) % Buffer.Count].transform;
        }

        /// <summary>The spawn point farthest from every given position (for respawns away from enemies).</summary>
        public static Transform PickFarthest(Scene scene, IReadOnlyList<Vector3> avoid)
        {
            Collect(scene);
            Transform best = null;
            float bestDistance = -1f;
            foreach (SpawnPoint point in Buffer)
            {
                float nearest = float.MaxValue;
                foreach (Vector3 position in avoid)
                    nearest = Mathf.Min(nearest, (point.transform.position - position).sqrMagnitude);
                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = point.transform;
                }
            }
            return best;
        }

        static void Collect(Scene scene)
        {
            Buffer.Clear();
            foreach (GameObject root in scene.GetRootGameObjects())
                Buffer.AddRange(root.GetComponentsInChildren<SpawnPoint>());
            Buffer.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.45f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward);
        }
    }
}
