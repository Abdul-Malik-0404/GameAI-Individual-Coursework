using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Manages round lifecycle: win detection, defeat countdown, and full scene reload.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

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
        Time.timeScale = 1f;
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
            Debug.LogWarning("GameManager: scene isn't in Build Profiles > Scene List, so it can't restart.");
    }

    void Update()
    {
        switch (state)
        {
            case GameState.Playing:
                npcCheckTimer -= Time.deltaTime;
                if (npcCheckTimer <= 0f)
                {
                    npcCheckTimer = 0.25f;
                    if (startingNpcCount > 0 && CountNpcs() == 0) Win();
                }
                break;

            case GameState.PlayerDead:
                restartTimer -= Time.unscaledDeltaTime;
                if (restartTimer <= 0f) RestartLevel();
                break;

            case GameState.Victory:
                Keyboard kb = Keyboard.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                    RestartLevel();
                break;
        }
    }

    public void OnPlayerDied()
    {
        if (state != GameState.Playing) return;
        state = GameState.PlayerDead;
        restartTimer = restartDelay;
        Debug.Log("GameManager: player died. Restarting in " + restartDelay + " seconds.");
    }

    void Win()
    {
        state = GameState.Victory;
        finishTime = Time.time - startTime;
        Time.timeScale = 0f;
        Debug.Log("GameManager: all NPCs defeated in " + FormatTime(finishTime) + "!");
    }

    void RestartLevel()
    {
        Time.timeScale = 1f;
        int sceneIndex = SceneManager.GetActiveScene().buildIndex;
        if (sceneIndex < 0)
        {
            Debug.LogError("GameManager: add this scene in File > Build Profiles > Scene List to restart.");
            return;
        }
        SceneManager.LoadScene(sceneIndex);
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

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || state == GameState.Playing) return;

        float s = Screen.height / 1080f;
        if (bigStyle == null)
        {
            bigStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
            smallStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
        }
        bigStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(64f * s));
        smallStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(26f * s));

        if (state == GameState.PlayerDead)
        {
            int secondsLeft = Mathf.CeilToInt(Mathf.Max(0f, restartTimer));
            HudUtility.DrawShadowLabel(new Rect(0f, Screen.height * 0.5f, Screen.width, 40f * s),
                                       "Restarting the level in " + secondsLeft + "...", Color.white, smallStyle, 2f);
        }
        else if (state == GameState.Victory)
        {
            Color oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = oldColor;

            HudUtility.DrawShadowLabel(new Rect(0f, Screen.height * 0.35f, Screen.width, 90f * s), "VICTORY!", Color.green, bigStyle, 2f);
            HudUtility.DrawShadowLabel(new Rect(0f, Screen.height * 0.35f + 100f * s, Screen.width, 40f * s),
                                       "All " + startingNpcCount + " NPCs defeated in " + FormatTime(finishTime), Color.white, smallStyle, 2f);
            HudUtility.DrawShadowLabel(new Rect(0f, Screen.height * 0.35f + 150f * s, Screen.width, 40f * s),
                                       "Press Enter to play again", Color.yellow, smallStyle, 2f);
        }
    }
}
