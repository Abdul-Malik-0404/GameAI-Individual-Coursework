using UnityEngine;

// Shared XZ-plane geometry, layer resolution, ID naming, and Gizmo helpers.
public static class SpatialUtility
{
    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public static bool IsWithinAngle(Vector3 origin, Vector3 forward, Vector3 point, float maxHalfAngle)
    {
        Vector3 toPoint = point - origin;
        toPoint.y = 0f;
        return toPoint.sqrMagnitude < 0.01f || Vector3.Angle(forward, toPoint) <= maxHalfAngle;
    }

    // Falls back to the A* grid's wall mask, then standard wall layers, if unassigned.
    public static LayerMask ResolveObstacleMask(LayerMask current, string warnOwner = null)
    {
        if (current.value == 0 && Pathfinding.Instance != null)
        {
            GridManager grid = Pathfinding.Instance.GetComponent<GridManager>();
            if (grid != null) current = grid.unwalkableMask;
        }
        if (current.value == 0)
            current = LayerMask.GetMask("Unwalkable", "Wall");
        if (current.value == 0 && !string.IsNullOrEmpty(warnOwner))
            Debug.LogWarning(warnOwner + ": no Obstacle Mask set and no wall layer found.");
        return current;
    }

    // Assigns a deterministic scene-order ID when none is set in the Inspector.
    public static string ResolveNpcId<T>(T self, string currentId, string prefix) where T : MonoBehaviour
    {
        if (!string.IsNullOrEmpty(currentId)) return currentId;
        T[] all = Object.FindObjectsByType<T>(FindObjectsSortMode.InstanceID);
        return prefix + " " + (System.Array.IndexOf(all, self) + 1);
    }

    public static void DrawConeGizmo(Vector3 origin, Vector3 forward, float angleDegrees, float radius, Color color)
    {
        Gizmos.color = color;
        Vector3 left = Quaternion.Euler(0f, -angleDegrees * 0.5f, 0f) * forward;
        Vector3 right = Quaternion.Euler(0f, angleDegrees * 0.5f, 0f) * forward;
        Gizmos.DrawLine(origin, origin + left * radius);
        Gizmos.DrawLine(origin, origin + right * radius);
    }
}
