using UnityEngine;

namespace AwesomeBalls.Networking
{
    /// <summary>
    /// Minimal pause overlay in the gameplay scene: shown while the cursor is free (Esc).
    /// The game keeps running underneath (GDD 10.1).
    /// </summary>
    [RequireComponent(typeof(NetworkSession))]
    public sealed class InGameMenu : MonoBehaviour
    {
        NetworkSession session;
        GUIStyle buttonStyle;
        GUIStyle labelStyle;

        void Awake() => session = GetComponent<NetworkSession>();

        void OnGUI()
        {
            if (!session.InGame || NetworkSession.UiOwnsCursor || Cursor.lockState == CursorLockMode.Locked) return;

            buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 22 };
            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };

            const float width = 320f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.5f - 90f;

            GUI.Box(new Rect(x - 20f, y - 50f, width + 40f, 230f), GUIContent.none);
            string info = session.Mode == SessionMode.Steam ? "Steam" : "Локальный тест";
            if (session.IsHost) info += $" · хост · игроков: {session.ConnectedPlayers}";
            GUI.Label(new Rect(x, y - 40f, width, 30f), info, labelStyle);

            if (GUI.Button(new Rect(x, y, width, 54f), "Продолжить", buttonStyle))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (session.Mode == SessionMode.Steam && GUI.Button(new Rect(x, y + 64f, width, 54f), "Пригласить друга", buttonStyle))
                session.Lobby.OpenInviteOverlay();
            if (GUI.Button(new Rect(x, y + 128f, width, 54f), "Выйти в меню", buttonStyle))
                session.Leave();
        }
    }
}
