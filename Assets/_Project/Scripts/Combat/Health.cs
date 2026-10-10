using System;
using System.Collections;
using System.Collections.Generic;
using AwesomeBalls.Networking;
using AwesomeBalls.Player;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace AwesomeBalls.Combat
{
    /// <summary>
    /// Server-authoritative HP (GDD 6.1). Max HP and regeneration come from card stats. In a match a
    /// death is final for the round (the match manager listens to <see cref="ServerDied"/>); in free
    /// play the player respawns after a delay. Falling off the map counts as a death.
    /// </summary>
    public sealed class Health : NetworkBehaviour
    {
        const float RegenerationDelay = 3f;

        [SerializeField] float respawnDelay = 3f;
        [SerializeField] PlayerController controller;
        [SerializeField] PlayerStats stats;
        [SerializeField] CharacterController body;
        [SerializeField] Renderer[] hideWhenDead;
        [SerializeField] CombatHud hud;

        readonly SyncVar<int> current = new(100);
        readonly SyncVar<bool> dead = new(false);

        static readonly List<Vector3> OtherPlayers = new();

        float lastDamageTime;
        float regenerationBuffer;

        /// <summary>Server: respawn players automatically after death (free play). A match turns this off.</summary>
        public static bool AutoRespawn = true;

        public int Current => current.Value;
        public int Max => stats.MaxHealth;
        public bool IsDead => dead.Value;
        public float RespawnDelay => respawnDelay;

        /// <summary>Owner only: we took damage. Arg: amount.</summary>
        public event Action<int> Damaged;
        /// <summary>Server: a player died. Args: victim, killer (null for falls).</summary>
        public static event Action<Health, Health> ServerDied;
        /// <summary>Every client: someone died. Args: killer name (null for falls), victim name, victim is local player.</summary>
        public static event Action<string, string, bool> Killed;
        /// <summary>Owner only: who killed us (null for falls).</summary>
        public string LastKiller { get; private set; }

        void Awake()
        {
            dead.OnChange += (_, isDead, _) => ApplyDeathState(isDead);
            stats.Rebuilt += () =>
            {
                // Card stats are assigned on spawn, before the fight: start at full (new) max HP.
                if (IsServerStarted && !dead.Value) current.Value = stats.MaxHealth;
            };
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            current.Value = stats.MaxHealth;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyDeathState(dead.Value);
            if (!IsOwner) return;
            controller.FellOutOfWorld += OnFellOutOfWorld;
            hud.enabled = true;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner) controller.FellOutOfWorld -= OnFellOutOfWorld;
        }

        void Update()
        {
            if (!IsServerStarted || dead.Value || stats.Regeneration <= 0f) return;
            if (current.Value >= stats.MaxHealth || Time.time - lastDamageTime < RegenerationDelay) return;

            regenerationBuffer += stats.Regeneration * Time.deltaTime;
            int whole = Mathf.FloorToInt(regenerationBuffer);
            if (whole <= 0) return;
            regenerationBuffer -= whole;
            ServerHeal(whole);
        }

        /// <summary>Server: apply damage from a weapon (null attacker for the environment).</summary>
        public void ServerDamage(int amount, Weapon attacker, Vector3 knockback)
        {
            if (!IsServerStarted || dead.Value) return;
            lastDamageTime = Time.time;
            regenerationBuffer = 0f;
            current.Value = Mathf.Max(0, current.Value - amount);
            TargetDamaged(Owner, amount, knockback);
            if (current.Value == 0) ServerDie(attacker != null ? attacker.GetComponent<Health>() : null);
        }

        public void ServerHeal(int amount)
        {
            if (!IsServerStarted || dead.Value || amount <= 0) return;
            current.Value = Mathf.Min(stats.MaxHealth, current.Value + amount);
        }

        void OnFellOutOfWorld()
        {
            if (!dead.Value) ServerFellOutOfWorld();
        }

        [ServerRpc]
        void ServerFellOutOfWorld() => ServerDie(null);

        void ServerDie(Health killer)
        {
            if (dead.Value) return;
            current.Value = 0;
            dead.Value = true;
            string victimName = GetComponent<PlayerNetwork>().DisplayName;
            string killerName = killer != null ? killer.GetComponent<PlayerNetwork>().DisplayName : null;
            ObserversKilled(killerName, victimName);
            ServerDied?.Invoke(this, killer);
            if (AutoRespawn) StartCoroutine(RespawnAfterDelay());
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);

            OtherPlayers.Clear();
            foreach (Health other in FindObjectsByType<Health>(FindObjectsSortMode.None))
                if (other != this && !other.IsDead) OtherPlayers.Add(other.transform.position);
            Transform point = SpawnPoint.PickFarthest(gameObject.scene, OtherPlayers);
            Vector3 position = point != null ? point.position : Vector3.zero;
            float yaw = point != null ? point.eulerAngles.y : 0f;

            current.Value = stats.MaxHealth;
            dead.Value = false;
            TargetRespawn(Owner, position, yaw);
        }

        [TargetRpc]
        void TargetDamaged(NetworkConnection connection, int amount, Vector3 knockback)
        {
            controller.Motor.AddVelocity(knockback);
            Damaged?.Invoke(amount);
        }

        [TargetRpc]
        void TargetRespawn(NetworkConnection connection, Vector3 position, float yaw)
        {
            controller.TeleportTo(position, yaw);
            controller.enabled = true;
        }

        [ObserversRpc]
        void ObserversKilled(string killerName, string victimName)
        {
            if (IsOwner) LastKiller = killerName;
            Killed?.Invoke(killerName, victimName, IsOwner);
        }

        void ApplyDeathState(bool isDead)
        {
            foreach (Renderer r in hideWhenDead) r.enabled = !isDead;
            // Dead players don't block movement or projectiles.
            body.enabled = !isDead;
            if (isDead && IsOwner) controller.enabled = false;
        }
    }
}
