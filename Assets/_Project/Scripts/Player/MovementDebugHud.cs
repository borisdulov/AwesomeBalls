using AwesomeBalls.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AwesomeBalls.Player
{
    /// <summary>Prototype overlay: crosshair, speed, mode and resources. F1 toggles the stats.</summary>
    public sealed class MovementDebugHud : MonoBehaviour
    {
        [SerializeField] PlayerController target;
        [SerializeField] bool showStats = true;

        GUIStyle style;

        /// <summary>Hide the overlay while a full-screen match UI (card pick, results) is up.</summary>
        public static bool Suppressed { get; set; }
        float smoothedFps;
        float topSpeed;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) showStats = !showStats;
            if (Time.unscaledDeltaTime > 0f) smoothedFps = Mathf.Lerp(smoothedFps, 1f / Time.unscaledDeltaTime, 0.1f);
        }

        void OnGUI()
        {
            if (Suppressed) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            GUI.DrawTexture(new Rect(center.x - 2f, center.y - 2f, 4f, 4f), Texture2D.whiteTexture);

            if (!showStats) return;

            ref readonly MotorState s = ref target.Motor.State;
            MovementSettings cfg = target.Motor.Settings;
            float speed = new Vector2(s.Velocity.x, s.Velocity.z).magnitude;
            topSpeed = Mathf.Max(topSpeed, speed);

            string dashes = new string('◆', s.DashCharges) + new string('◇', cfg.dashCharges - s.DashCharges);
            string text =
                $"<b>Speed</b> {speed,5:0.0} m/s   (top {topSpeed:0.0})\n" +
                $"<b>Vertical</b> {s.Velocity.y,5:0.0} m/s\n" +
                $"<b>Mode</b> {s.Mode}{(s.Grounded ? "  [grounded]" : "")}\n" +
                $"<b>Air jumps</b> {s.AirJumpsLeft}/{cfg.airJumps}   <b>Wall chain</b> {s.WallJumpChain}\n" +
                $"<b>Dash</b> {dashes}" +
                (s.DashCharges < cfg.dashCharges ? $"  {s.DashRechargeTimer / cfg.dashRechargeTime:P0}" : "") + "\n" +
                $"<b>FPS</b> {smoothedFps:0}\n\n" +
                "<size=14>WASD move · Space jump · Shift dash · Ctrl/C slide\nEsc free cursor · F1 hide stats</size>";

            var shadow = new GUIContent(text);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(17f, 17f, 600f, 300f), shadow, style);
            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 16f, 600f, 300f), shadow, style);
        }
    }
}
