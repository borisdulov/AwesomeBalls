using FishNet;
using UnityEngine;

namespace AwesomeBalls.Networking
{
    /// <summary>
    /// Pressing Play directly in a gameplay scene (no menu, no NetworkManager yet) starts a local
    /// host so movement can still be tested in one click.
    /// </summary>
    public sealed class DevSessionBootstrap : MonoBehaviour
    {
        [SerializeField] NetworkSession networkManagerPrefab;

        void Start()
        {
            if (InstanceFinder.NetworkManager != null) return;
            NetworkSession session = Instantiate(networkManagerPrefab);
            session.name = networkManagerPrefab.name;
            session.StartLocalHost();
        }
    }
}
