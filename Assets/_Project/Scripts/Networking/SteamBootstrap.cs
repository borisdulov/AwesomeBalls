using Steamworks;
using UnityEngine;

namespace AwesomeBalls.Networking
{
    /// <summary>
    /// Initializes the Steam API once per process and pumps its callbacks. Lives on the persistent
    /// NetworkManager object; duplicates created by reloading the menu scene stay passive.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SteamBootstrap : MonoBehaviour
    {
        static SteamBootstrap owner;

        public static bool Initialized { get; private set; }

        void Awake()
        {
            if (Initialized) return;

            if (!Packsize.Test())
            {
                Debug.LogError("[Steam] Packsize test failed: wrong Steamworks.NET build for this platform.");
                return;
            }
            if (!DllCheck.Test())
            {
                Debug.LogError("[Steam] steam_api DLL check failed.");
                return;
            }

            Initialized = SteamAPI.Init();
            if (Initialized)
            {
                owner = this;
                Debug.Log($"[Steam] Initialized as {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID()}).");
            }
            else
            {
                Debug.LogWarning("[Steam] SteamAPI.Init failed. Is the Steam client running and steam_appid.txt present?");
            }
        }

        void Update()
        {
            if (owner == this) SteamAPI.RunCallbacks();
        }

        void OnDestroy()
        {
            if (owner != this) return;
            SteamAPI.Shutdown();
            Initialized = false;
            owner = null;
        }
    }
}
