using System.Collections.Generic;
using UnityEngine;

// Any NPC that should get a label above its head implements this (GridBot and TankerBot do).
public interface IHUDTarget
{
    string Id { get; }              // e.g. "GridBot 1"
    string StateName { get; }       // e.g. "Patrol"
    Color StateColor { get; }       // Colour for the state text
    float CurrentHealth { get; }
    float MaxHealth { get; }
    Transform transform { get; }    // Already provided by every MonoBehaviour
}

// In-game HUD. Shows, in the Game view and in builds:
//   - above every NPC (anything implementing IHUDTarget): its ID, current FSM state and a health bar
//   - top-left: the player's health bar
//   - floating damage numbers whenever the player or an NPC loses health, and "DEFEATED" when one dies
//
// It uses Unity's built-in immediate-mode GUI (OnGUI), so it needs no Canvas, prefabs or fonts.
// It only READS values, so it never changes how the NPCs or the player behave.
//
// Put this on any ONE GameObject in the scene (e.g. an empty object called "HUD").
public class GameHUD : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Scales all text and bars. 1 = normal size; it also adjusts to the Game view's height.")]
    public float uiScale = 1f;
    [Tooltip("How far above the NPC's centre its label sits, in metres.")]
    public float npcLabelHeight = 2.2f;

    [Header("Damage Numbers")]
    [Tooltip("Seconds a damage number stays on screen.")]
    public float popupDuration = 1f;
    [Tooltip("How fast damage numbers float upwards, in metres per second.")]
    public float popupRiseSpeed = 1.5f;

    // One floating damage number
    private class Popup
    {
        public Vector3 worldPosition;
        public string text;
        public Color color;
        public float age;
        public bool big; // Critical hits are drawn larger
    }

    // What we remember about each NPC between frames, to spot health going down
    private class TrackedNPC
    {
        public MonoBehaviour component;  // Used to check whether the NPC still exists
        public IHUDTarget target;        // Used to read its ID, state and health
        public string id;
        public float lastHealth;
        public Vector3 lastPosition;
        public bool deathShown;
    }

    private PlayerController player;

    private readonly List<TrackedNPC> trackedNPCs = new List<TrackedNPC>();
    private readonly List<Popup> popups = new List<Popup>();
    private float refreshTimer;

    private GUIStyle idStyle;
    private GUIStyle stateStyle;
    private GUIStyle barTextStyle;
    private GUIStyle popupStyle;
    private GUIStyle bigPopupStyle;
    private GUIStyle titleStyle;
    private GUIStyle bigStyle;

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        if (player != null)
        {
            // The player tells us about every hit, including its type from the damage roll (Option A)
            player.Damaged += OnPlayerDamaged;
        }

        RefreshNPCList();
    }

    void OnDestroy()
    {
        if (player != null) player.Damaged -= OnPlayerDamaged;
    }

    // Shows e.g. "-10", "CRIT -25", "GRAZE -5" or "RAM -25" over the player
    void OnPlayerDamaged(int damage, string hitType)
    {
        string text = "-" + damage;
        if (!string.IsNullOrEmpty(hitType) && hitType != "HIT")
            text = hitType + " " + text;

        Color color = new Color(1f, 0.35f, 0.35f);          // Normal hit: red
        if (hitType.Contains("CRIT")) color = new Color(1f, 0.1f, 0.6f);   // Critical: hot pink
        else if (hitType.Contains("GRAZE")) color = new Color(0.8f, 0.8f, 0.8f); // Graze: grey

        AddPopup(player.transform.position + Vector3.up * 1.5f, text, color, hitType.Contains("CRIT"));
    }

    void Update()
    {
        // Look for NPCs added to the scene (twice a second is plenty)
        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.5f;
            RefreshNPCList();
        }

        // ---- NPCs: health went down since last frame? Show the damage. Also spot deaths ----
        // (The player's damage numbers come from its Damaged event instead - see OnPlayerDamaged.)
        for (int i = trackedNPCs.Count - 1; i >= 0; i--)
        {
            TrackedNPC tracked = trackedNPCs[i];

            // The NPC was destroyed since last frame
            if (tracked.component == null)
            {
                if (!tracked.deathShown)
                    AddPopup(tracked.lastPosition + Vector3.up * npcLabelHeight, tracked.id + " DEFEATED", Color.red);
                trackedNPCs.RemoveAt(i);
                continue;
            }

            float health = tracked.target.CurrentHealth;
            float lost = tracked.lastHealth - health;

            // Only count real drops (healing makes health go UP, which we ignore)
            if (lost >= 0.5f)
                AddPopup(tracked.target.transform.position + Vector3.up * 1.5f, "-" + Mathf.RoundToInt(lost), new Color(1f, 0.85f, 0.2f));

            if (health <= 0f && !tracked.deathShown)
            {
                AddPopup(tracked.target.transform.position + Vector3.up * npcLabelHeight, tracked.id + " DEFEATED", Color.red);
                tracked.deathShown = true;
            }

            tracked.id = tracked.target.Id;
            tracked.lastHealth = health;
            tracked.lastPosition = tracked.target.transform.position;
        }

        // ---- Age the damage numbers and remove old ones ----
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            popups[i].age += Time.deltaTime;
            if (popups[i].age >= popupDuration)
                popups.RemoveAt(i);
        }
    }

    // Finds every script in the scene that implements IHUDTarget (GridBot and TankerBot)
    void RefreshNPCList()
    {
        MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

        foreach (MonoBehaviour component in all)
        {
            IHUDTarget target = component as IHUDTarget;
            if (target == null) continue;

            bool alreadyTracked = false;
            foreach (TrackedNPC tracked in trackedNPCs)
            {
                if (tracked.component == component)
                {
                    alreadyTracked = true;
                    break;
                }
            }

            if (!alreadyTracked)
            {
                TrackedNPC newNPC = new TrackedNPC();
                newNPC.component = component;
                newNPC.target = target;
                newNPC.id = target.Id;
                newNPC.lastHealth = target.CurrentHealth;
                newNPC.lastPosition = component.transform.position;
                trackedNPCs.Add(newNPC);
            }
        }
    }

    void AddPopup(Vector3 worldPosition, string text, Color color)
    {
        AddPopup(worldPosition, text, color, false);
    }

    void AddPopup(Vector3 worldPosition, string text, Color color, bool big)
    {
        Popup popup = new Popup();
        popup.big = big;
        // Small random sideways offset so several numbers in a row don't sit on top of each other
        popup.worldPosition = worldPosition + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
        popup.text = text;
        popup.color = color;
        popup.age = 0f;
        popups.Add(popup);
    }

    // ======================= Drawing =======================
    void OnGUI()
    {
        // OnGUI runs several times a frame; only the Repaint pass actually draws
        if (Event.current.type != EventType.Repaint) return;

        Camera cam = Camera.main;
        if (cam == null) return; // Needs a camera tagged "MainCamera"

        float s = uiScale * Screen.height / 1080f; // Scale everything with the Game view height
        PrepareStyles(s);

        // ---- Above each NPC: ID, state, health bar ----
        foreach (TrackedNPC tracked in trackedNPCs)
        {
            if (tracked.component == null) continue;
            IHUDTarget npc = tracked.target;

            Vector3 screen = cam.WorldToScreenPoint(npc.transform.position + Vector3.up * npcLabelHeight);
            if (screen.z <= 0f) continue; // Behind the camera

            // Screen positions start at the bottom-left; GUI positions start at the top-left
            float x = screen.x;
            float y = Screen.height - screen.y;

            float barWidth = 130f * s;
            float barHeight = 18f * s;

            DrawShadowLabel(new Rect(x - 100f * s, y - 66f * s, 200f * s, 20f * s), npc.Id, Color.white, idStyle);
            DrawShadowLabel(new Rect(x - 100f * s, y - 46f * s, 200f * s, 24f * s), npc.StateName.ToUpper(), npc.StateColor, stateStyle);

            float health = Mathf.Max(0f, npc.CurrentHealth);
            float maxHealth = Mathf.Max(1f, npc.MaxHealth);
            string healthText = Mathf.CeilToInt(health) + " / " + Mathf.RoundToInt(maxHealth);
            DrawBar(new Rect(x - barWidth / 2f, y - 20f * s, barWidth, barHeight), health / maxHealth, healthText);
        }

        // ---- Top-left: player health ----
        if (player != null)
        {
            int health = Mathf.Max(0, player.healthPoints);
            int maxHealth = Mathf.Max(1, player.MaxHealth);

            DrawShadowLabel(new Rect(20f * s, 14f * s, 300f * s, 26f * s), "PLAYER", Color.white, titleStyle);
            DrawBar(new Rect(20f * s, 42f * s, 280f * s, 26f * s), health / (float)maxHealth,
                    health + " / " + maxHealth);

            if (player.IsDead)
            {
                DrawShadowLabel(new Rect(0f, Screen.height * 0.4f, Screen.width, 80f * s),
                                "YOU WERE DEFEATED", Color.red, bigStyle);
            }
        }

        // ---- Floating damage numbers ----
        foreach (Popup popup in popups)
        {
            Vector3 world = popup.worldPosition + Vector3.up * popupRiseSpeed * popup.age;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) continue;

            Color color = popup.color;
            color.a = 1f - popup.age / popupDuration; // Fade out

            DrawShadowLabel(new Rect(screen.x - 150f * s, Screen.height - screen.y - 20f * s, 300f * s, 40f * s),
                            popup.text, color, popup.big ? bigPopupStyle : popupStyle);
        }
    }

    // A health bar: dark background, coloured fill (green -> yellow -> red), text on top
    void DrawBar(Rect rect, float fraction, string text)
    {
        fraction = Mathf.Clamp01(fraction);
        Color oldColor = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        GUI.color = HealthColor(fraction);
        float border = 2f;
        GUI.DrawTexture(new Rect(rect.x + border, rect.y + border, (rect.width - border * 2f) * fraction, rect.height - border * 2f),
                        Texture2D.whiteTexture);

        GUI.color = oldColor;
        DrawShadowLabel(rect, text, Color.white, barTextStyle);
    }

    // Text with a dark drop shadow, so it stays readable over any background
    void DrawShadowLabel(Rect rect, string text, Color color, GUIStyle style)
    {
        Color oldColor = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, color.a * 0.8f);
        GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);

        GUI.color = color;
        GUI.Label(rect, text, style);

        GUI.color = oldColor;
    }

    Color HealthColor(float fraction)
    {
        if (fraction > 0.5f)
            return Color.Lerp(Color.yellow, Color.green, (fraction - 0.5f) * 2f);
        return Color.Lerp(Color.red, Color.yellow, fraction * 2f);
    }

    void PrepareStyles(float s)
    {
        if (stateStyle == null)
        {
            idStyle = MakeStyle(TextAnchor.MiddleCenter);
            stateStyle = MakeStyle(TextAnchor.MiddleCenter);
            barTextStyle = MakeStyle(TextAnchor.MiddleCenter);
            popupStyle = MakeStyle(TextAnchor.MiddleCenter);
            bigPopupStyle = MakeStyle(TextAnchor.MiddleCenter);
            titleStyle = MakeStyle(TextAnchor.MiddleLeft);
            bigStyle = MakeStyle(TextAnchor.MiddleCenter);
        }

        // Font sizes follow the scale, so the HUD looks the same at any resolution
        idStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(13f * s));
        stateStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(15f * s));
        barTextStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(12f * s));
        popupStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(22f * s));
        bigPopupStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(32f * s));
        titleStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(16f * s));
        bigStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(48f * s));
    }

    GUIStyle MakeStyle(TextAnchor alignment)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = alignment;
        style.fontStyle = FontStyle.Bold;
        style.wordWrap = false;
        style.clipping = TextClipping.Overflow;
        style.normal.textColor = Color.white; // GUI.color tints this to the colour we want
        return style;
    }
}
