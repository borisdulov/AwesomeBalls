using AwesomeBalls.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Steamworks;
using UnityEngine;
using UnityEngine.Rendering;

namespace AwesomeBalls.Networking
{
    /// <summary>
    /// Hooks a spawned player up for its owner (input, camera, HUD, first-person body), tints it
    /// per player and syncs its display name. Movement is owner-authoritative for now and synced by
    /// NetworkTransform; server reconciliation comes with M3 prediction (GDD 9.3).
    /// </summary>
    public sealed class PlayerNetwork : NetworkBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color[] PlayerColors =
        {
            new(0.95f, 0.3f, 0.3f),
            new(0.3f, 0.55f, 0.95f),
            new(0.35f, 0.85f, 0.4f),
            new(0.95f, 0.85f, 0.3f),
        };

        [SerializeField] PlayerController controller;
        [SerializeField] MovementDebugHud hud;
        [Tooltip("Tinted with the player color and hidden (shadow only) for the owner.")]
        [SerializeField] Renderer[] bodyRenderers;
        [Tooltip("Hidden (shadow only) for the owner, keeps its own color.")]
        [SerializeField] Renderer[] detailRenderers;

        readonly SyncVar<string> displayName = new(string.Empty);
        PlayerCamera boundCamera;

        /// <summary>The local player's object, if spawned.</summary>
        public static PlayerNetwork Local { get; private set; }

        public PlayerController Controller => controller;
        public string DisplayName => string.IsNullOrEmpty(displayName.Value) ? $"Игрок {OwnerId + 1}" : displayName.Value;
        public Color Color => ColorFor(OwnerId);

        public static Color ColorFor(int ownerId) => PlayerColors[Mathf.Abs(ownerId) % PlayerColors.Length];

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyColor();
            if (!IsOwner) return;

            Local = this;
            ServerSetName(LocalPlayerName());

            // First person: keep the body's shadow, hide the mesh itself.
            foreach (Renderer r in bodyRenderers) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            foreach (Renderer r in detailRenderers) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            boundCamera = FindAnyObjectByType<PlayerCamera>();
            boundCamera.SetTarget(controller);
            controller.Bind(boundCamera);
            controller.enabled = true;
            hud.enabled = true;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (boundCamera != null) boundCamera.SetTarget(null);
            if (Local == this) Local = null;
        }

        /// <summary>Steam persona in Steam sessions; empty otherwise (local tests often run two copies under one account).</summary>
        public static string LocalPlayerName()
        {
            bool steamSession = NetworkSession.Instance != null && NetworkSession.Instance.Mode == SessionMode.Steam;
            return steamSession && SteamBootstrap.Initialized ? SteamFriends.GetPersonaName() : string.Empty;
        }

        [ServerRpc]
        void ServerSetName(string playerName)
        {
            if (playerName.Length > 32) playerName = playerName.Substring(0, 32);
            displayName.Value = playerName;
        }

        void ApplyColor()
        {
            var block = new MaterialPropertyBlock();
            foreach (Renderer r in bodyRenderers)
            {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color);
                r.SetPropertyBlock(block);
            }
        }
    }
}
