using System.Collections.Generic;
using UnityEngine;

namespace AwesomeBalls.Combat
{
    /// <summary>
    /// Prototype combat HUD (GDD 10.2, 6.6): HP bar, ammo pips, hit marker, damage flash,
    /// death screen and kill feed. Enabled for the local player only.
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        const float HitMarkerTime = 0.15f;
        const float FlashTime = 0.35f;
        const float FeedTime = 5f;
        const int FeedSize = 4;

        [SerializeField] Health health;
        [SerializeField] Weapon weapon;

        readonly List<(string text, float time)> feed = new();
        float hitMarkerTimer;
        bool hitMarkerKill;
        float flashTimer;
        float deathTime;
        GUIStyle bigStyle, smallStyle, centeredStyle, feedStyle;

        /// <summary>Hide the HUD while a full-screen match UI (card pick, results) is up.</summary>
        public static bool Suppressed { get; set; }

        void OnEnable()
        {
            health.Damaged += OnDamaged;
            weapon.HitConfirmed += OnHitConfirmed;
            Health.Killed += OnKilled;
        }

        void OnDisable()
        {
            health.Damaged -= OnDamaged;
            weapon.HitConfirmed -= OnHitConfirmed;
            Health.Killed -= OnKilled;
        }

        void OnDamaged(int amount) => flashTimer = FlashTime;

        void OnHitConfirmed(int damage, bool killed)
        {
            hitMarkerTimer = killed ? HitMarkerTime * 2f : HitMarkerTime;
            hitMarkerKill = killed;
        }

        void OnKilled(string killer, string victim, bool victimIsLocal)
        {
            if (victimIsLocal) deathTime = Time.time;
            feed.Add((killer == null ? $"{victim} разбился" : $"{killer}  ✖  {victim}", Time.time));
            if (feed.Count > FeedSize) feed.RemoveAt(0);
        }

        void Update()
        {
            hitMarkerTimer -= Time.deltaTime;
            flashTimer -= Time.deltaTime;
            feed.RemoveAll(e => Time.time - e.time > FeedTime);
        }

        void OnGUI()
        {
            if (Suppressed) return;
            bigStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            smallStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            centeredStyle ??= new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter };
            feedStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleRight };

            float w = Screen.width, h = Screen.height;
            var center = new Vector2(w * 0.5f, h * 0.5f);

            if (flashTimer > 0f) Fill(new Rect(0, 0, w, h), new Color(0.8f, 0f, 0f, 0.35f * flashTimer / FlashTime));

            if (health.IsDead)
            {
                Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.45f));
                float left = Mathf.Max(0f, health.RespawnDelay - (Time.time - deathTime));
                string by = health.LastKiller != null ? $"Вас убил {health.LastKiller}" : "Вы разбились";
                GUI.Label(new Rect(0, h * 0.4f, w, 50f), by, bigStyle);
                string next = Health.AutoRespawn ? $"Возрождение через {Mathf.CeilToInt(left)}…" : "Ждём конца раунда…";
                GUI.Label(new Rect(0, h * 0.4f + 50f, w, 40f), next, centeredStyle);
            }
            else
            {
                DrawHitMarker(center);
                DrawAmmo(center);
                DrawHealth(w, h);
            }

            for (int i = 0; i < feed.Count; i++)
                Shadowed(new Rect(w - 520f, 16f + i * 26f, 500f, 26f), feed[i].text, feedStyle);
        }

        void DrawHitMarker(Vector2 c)
        {
            if (hitMarkerTimer <= 0f) return;
            Color color = hitMarkerKill ? new Color(1f, 0.25f, 0.2f) : Color.white;
            const float gap = 6f, len = 9f, t = 2f;
            Fill(new Rect(c.x - gap - len, c.y - t * 0.5f, len, t), color);
            Fill(new Rect(c.x + gap, c.y - t * 0.5f, len, t), color);
            Fill(new Rect(c.x - t * 0.5f, c.y - gap - len, t, len), color);
            Fill(new Rect(c.x - t * 0.5f, c.y + gap, t, len), color);
        }

        void DrawAmmo(Vector2 c)
        {
            const float size = 7f, spacing = 11f;
            int mag = weapon.MagazineSize;
            float startX = c.x - (mag - 1) * spacing * 0.5f - size * 0.5f;
            float y = c.y + 26f;
            if (weapon.IsReloading)
            {
                Fill(new Rect(c.x - 40f, y, 80f, 4f), new Color(1f, 1f, 1f, 0.25f));
                Fill(new Rect(c.x - 40f, y, 80f * weapon.ReloadProgress, 4f), Color.white);
                return;
            }
            for (int i = 0; i < mag; i++)
                Fill(new Rect(startX + i * spacing, y, size, size), i < weapon.Ammo ? Color.white : new Color(1f, 1f, 1f, 0.2f));
        }

        void DrawHealth(float w, float h)
        {
            var bar = new Rect(24f, h - 52f, 320f, 22f);
            float fraction = Mathf.Clamp01((float)health.Current / health.Max);
            Fill(bar, new Color(0f, 0f, 0f, 0.5f));
            Color color = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.9f, 0.4f), fraction);
            Fill(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * fraction, bar.height - 4f), color);
            Shadowed(new Rect(bar.xMax + 12f, bar.y - 4f, 120f, 30f), health.Current.ToString(), smallStyle);
        }

        static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void Shadowed(Rect rect, string text, GUIStyle style)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            GUI.color = previous;
            GUI.Label(rect, text, style);
        }
    }
}
