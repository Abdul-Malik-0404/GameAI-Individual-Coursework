using System.Collections.Generic;
using UnityEngine;

// Exposes NPC identity, FSM state, and health to GameHUD.
public interface IHUDTarget
{
    string Id { get; }
    string StateName { get; }
    Color StateColor { get; }
    float CurrentHealth { get; }
    float MaxHealth { get; }
    Transform transform { get; }
}

// Draws player health, overhead NPC state/health bars, and floating combat popups.
public class GameHUD : MonoBehaviour
{
    [Header("Layout")]
    public float uiScale = 1f;
    public float npcLabelHeight = 2.2f;

    [Header("Damage Numbers")]
    public float popupDuration = 1f;
    public float popupRiseSpeed = 1.5f;

    private class Popup
    {
        public Vector3 worldPosition;
        public string text;
        public Color color;
        public float age;
        public bool big;
    }

    private class TrackedNPC
    {
        public MonoBehaviour component;
        public IHUDTarget target;
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
        if (player != null) player.Damaged += OnPlayerDamaged;
        RefreshNPCList();
    }

    void OnDestroy()
    {
        if (player != null) player.Damaged -= OnPlayerDamaged;
    }

    void OnPlayerDamaged(int damage, string hitType)
    {
        string text = !string.IsNullOrEmpty(hitType) && hitType != "HIT"
            ? hitType + " -" + damage
            : "-" + damage;

        Color color = new Color(1f, 0.35f, 0.35f);
        if (hitType.Contains("CRIT")) color = new Color(1f, 0.1f, 0.6f);
        else if (hitType.Contains("GRAZE")) color = new Color(0.8f, 0.8f, 0.8f);

        AddPopup(player.transform.position + Vector3.up * 1.5f, text, color, hitType.Contains("CRIT"));
    }

    void Update()
    {
        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.5f;
            RefreshNPCList();
        }

        // Poll tracked NPC health drops and removals to spawn damage/defeat popups.
        for (int i = trackedNPCs.Count - 1; i >= 0; i--)
        {
            TrackedNPC tracked = trackedNPCs[i];
            if (tracked.component == null)
            {
                if (!tracked.deathShown)
                    AddPopup(tracked.lastPosition + Vector3.up * npcLabelHeight, tracked.id + " DEFEATED", Color.red);
                trackedNPCs.RemoveAt(i);
                continue;
            }

            float health = tracked.target.CurrentHealth;
            float lost = tracked.lastHealth - health;
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

        for (int i = popups.Count - 1; i >= 0; i--)
        {
            popups[i].age += Time.deltaTime;
            if (popups[i].age >= popupDuration) popups.RemoveAt(i);
        }
    }

    void RefreshNPCList()
    {
        foreach (MonoBehaviour component in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            IHUDTarget target = component as IHUDTarget;
            if (target == null) continue;

            bool alreadyTracked = false;
            foreach (TrackedNPC tracked in trackedNPCs)
            {
                if (tracked.component == component) { alreadyTracked = true; break; }
            }

            if (!alreadyTracked)
            {
                trackedNPCs.Add(new TrackedNPC
                {
                    component = component,
                    target = target,
                    id = target.Id,
                    lastHealth = target.CurrentHealth,
                    lastPosition = component.transform.position
                });
            }
        }
    }

    void AddPopup(Vector3 worldPosition, string text, Color color, bool big = false)
    {
        popups.Add(new Popup
        {
            big = big,
            worldPosition = worldPosition + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f)),
            text = text,
            color = color
        });
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        float s = uiScale * Screen.height / 1080f;
        PrepareStyles(s);

        foreach (TrackedNPC tracked in trackedNPCs)
        {
            if (tracked.component == null) continue;
            IHUDTarget npc = tracked.target;

            Vector3 screen = cam.WorldToScreenPoint(npc.transform.position + Vector3.up * npcLabelHeight);
            if (screen.z <= 0f) continue;

            float x = screen.x;
            float y = Screen.height - screen.y;
            float barWidth = 130f * s;
            float barHeight = 18f * s;

            HudUtility.DrawShadowLabel(new Rect(x - 100f * s, y - 66f * s, 200f * s, 20f * s), npc.Id, Color.white, idStyle);
            HudUtility.DrawShadowLabel(new Rect(x - 100f * s, y - 46f * s, 200f * s, 24f * s), npc.StateName.ToUpper(), npc.StateColor, stateStyle);

            float health = Mathf.Max(0f, npc.CurrentHealth);
            float maxHealth = Mathf.Max(1f, npc.MaxHealth);
            DrawBar(new Rect(x - barWidth / 2f, y - 20f * s, barWidth, barHeight), health / maxHealth,
                    Mathf.CeilToInt(health) + " / " + Mathf.RoundToInt(maxHealth));
        }

        if (player != null)
        {
            int health = Mathf.Max(0, player.healthPoints);
            int maxHealth = Mathf.Max(1, player.MaxHealth);

            HudUtility.DrawShadowLabel(new Rect(20f * s, 14f * s, 300f * s, 26f * s), "PLAYER", Color.white, titleStyle);
            DrawBar(new Rect(20f * s, 42f * s, 280f * s, 26f * s), health / (float)maxHealth, health + " / " + maxHealth);

            if (player.IsDead)
            {
                HudUtility.DrawShadowLabel(new Rect(0f, Screen.height * 0.4f, Screen.width, 80f * s),
                                           "YOU WERE DEFEATED", Color.red, bigStyle);
            }
        }

        foreach (Popup popup in popups)
        {
            Vector3 world = popup.worldPosition + Vector3.up * popupRiseSpeed * popup.age;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) continue;

            Color color = popup.color;
            color.a = 1f - popup.age / popupDuration;
            HudUtility.DrawShadowLabel(new Rect(screen.x - 150f * s, Screen.height - screen.y - 20f * s, 300f * s, 40f * s),
                                       popup.text, color, popup.big ? bigPopupStyle : popupStyle);
        }
    }

    void DrawBar(Rect rect, float fraction, string text)
    {
        fraction = Mathf.Clamp01(fraction);
        Color oldColor = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        GUI.color = HealthColor(fraction);
        const float border = 2f;
        GUI.DrawTexture(new Rect(rect.x + border, rect.y + border, (rect.width - border * 2f) * fraction, rect.height - border * 2f),
                        Texture2D.whiteTexture);

        GUI.color = oldColor;
        HudUtility.DrawShadowLabel(rect, text, Color.white, barTextStyle);
    }

    Color HealthColor(float fraction)
    {
        return fraction > 0.5f
            ? Color.Lerp(Color.yellow, Color.green, (fraction - 0.5f) * 2f)
            : Color.Lerp(Color.red, Color.yellow, fraction * 2f);
    }

    void PrepareStyles(float s)
    {
        if (stateStyle == null)
        {
            idStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
            stateStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
            barTextStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
            popupStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
            bigPopupStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
            titleStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleLeft);
            bigStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
        }

        idStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(13f * s));
        stateStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(15f * s));
        barTextStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(12f * s));
        popupStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(22f * s));
        bigPopupStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(32f * s));
        titleStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(16f * s));
        bigStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(48f * s));
    }
}
