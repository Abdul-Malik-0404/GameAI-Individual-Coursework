using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// GAME FLOW
//   Playing    -> the game runs normally.
//   PlayerDead -> the player died: after a short countdown the WHOLE LEVEL restarts
//                 (the scene is reloaded, so the player, every NPC and the artefacts all reset).
//   Victory    -> every NPC has been defeated: the game ends and shows the time taken.
//                 Press Enter to play again.
//
// Put this on ONE empty GameObject called "GameManager".
// The scene must be in the build's scene list (File > Build Profiles > Scene List > Add Open Scenes),
// otherwise Unity can't reload it.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Tooltip("Seconds between the player dying and the level restarting.")]
    public float restartDelay = 3f;

    private enum GameState { Playing, PlayerDead, Victory }
    private GameState state = GameState.Playing;

    private int startingNpcCount;
    private float npcCheckTimer;
    private float restartTimer;
    private float startTime;
    private float finishTime;

    private GUIStyle bigStyle;
    private GUIStyle smallStyle;

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f; // In case the last round ended frozen on the victory screen
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        startingNpcCount = CountNpcs();
        startTime = Time.time;

        if (startingNpcCount == 0)
            Debug.LogWarning("GameManager: no NPCs found, so the game can't be won.");
        if (SceneManager.GetActiveScene().buildIndex < 0)
            Debug.LogWarning("GameManager: this scene isn't in the build's scene list, so it can't restart. " +
                             "Add it in File > Build Profiles > Scene List > Add Open Scenes.");
    }

    void Update()
    {
        switch (state)
        {
            case GameState.Playing:
                // Check a few times a second whether every NPC is gone
                npcCheckTimer -= Time.deltaTime;
                if (npcCheckTimer <= 0f)
                {
                    npcCheckTimer = 0.25f;
                    if (startingNpcCount > 0 && CountNpcs() == 0)
                        Win();
                }
                break;

            case GameState.PlayerDead:
                restartTimer -= Time.unscaledDeltaTime;
                if (restartTimer <= 0f)
                    RestartLevel();
                break;

            case GameState.Victory:
                Keyboard kb = Keyboard.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                    RestartLevel();
                break;
        }
    }

    // Called by PlayerController when the player's health reaches 0
    public void OnPlayerDied()
    {
        if (state != GameState.Playing) return;

        state = GameState.PlayerDead;
        restartTimer = restartDelay;
        Debug.Log("GameManager: player died. Restarting the level in " + restartDelay + " seconds.");
    }

    void Win()
    {
        state = GameState.Victory;
        finishTime = Time.time - startTime;
        Time.timeScale = 0f; // Freeze the game behind the victory screen
        Debug.Log("GameManager: all NPCs defeated in " + FormatTime(finishTime) + "!");
    }

    void RestartLevel()
    {
        Time.timeScale = 1f;
        int sceneIndex = SceneManager.GetActiveScene().buildIndex;
        if (sceneIndex < 0)
        {
            Debug.LogError("GameManager: can't restart - add this scene in File > Build Profiles > Scene List.");
            return;
        }
        SceneManager.LoadScene(sceneIndex); // Reloads everything: player, NPCs, artefacts
    }

    int CountNpcs()
    {
        return FindObjectsByType<GridBot>(FindObjectsSortMode.None).Length
             + FindObjectsByType<TankerBot>(FindObjectsSortMode.None).Length;
    }

    string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int secs = Mathf.FloorToInt(seconds % 60f);
        return minutes + ":" + secs.ToString("00");
    }

    // ======================= Screens =======================
    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (state == GameState.Playing) return;

        float s = Screen.height / 1080f;
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label);
            bigStyle.alignment = TextAnchor.MiddleCenter;
            bigStyle.fontStyle = FontStyle.Bold;
            bigStyle.normal.textColor = Color.white;

            smallStyle = new GUIStyle(bigStyle);
        }
        bigStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(64f * s));
        smallStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(26f * s));

        Color oldColor = GUI.color;

        if (state == GameState.PlayerDead)
        {
            // The HUD already shows "YOU WERE DEFEATED"; add the countdown under it
            int secondsLeft = Mathf.CeilToInt(Mathf.Max(0f, restartTimer));
            ShadowLabel(new Rect(0f, Screen.height * 0.5f, Screen.width, 40f * s),
                        "Restarting the level in " + secondsLeft + "...", Color.white, smallStyle);
        }
        else if (state == GameState.Victory)
        {
            // Dark overlay behind the text
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

            ShadowLabel(new Rect(0f, Screen.height * 0.35f, Screen.width, 90f * s), "VICTORY!", Color.green, bigStyle);
            ShadowLabel(new Rect(0f, Screen.height * 0.35f + 100f * s, Screen.width, 40f * s),
                        "All " + startingNpcCount + " NPCs defeated in " + FormatTime(finishTime), Color.white, smallStyle);
            ShadowLabel(new Rect(0f, Screen.height * 0.35f + 150f * s, Screen.width, 40f * s),
                        "Press Enter to play again", Color.yellow, smallStyle);
        }

        GUI.color = oldColor;
    }

    void ShadowLabel(Rect rect, string text, Color color, GUIStyle style)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        GUI.color = color;
        GUI.Label(rect, text, style);
    }
}
