using UnityEngine;

// Shared IMGUI label styling and drop-shadow rendering helpers.
public static class HudUtility
{
    public static GUIStyle CreateLabelStyle(TextAnchor alignment)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = alignment;
        style.fontStyle = FontStyle.Bold;
        style.wordWrap = false;
        style.clipping = TextClipping.Overflow;
        style.normal.textColor = Color.white;
        return style;
    }

    public static void DrawShadowLabel(Rect rect, string text, Color color, GUIStyle style, float offset = 1.5f)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, color.a * 0.8f);
        GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), text, style);
        GUI.color = color;
        GUI.Label(rect, text, style);
        GUI.color = previous;
    }
}
