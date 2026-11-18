using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// ARTEFACT SPAWNER (sections 2.2 and 2.3)
// Scatters artefacts at random places in an area, and only keeps a place if it passes every check:
//   2.2 Procedural placement : random point inside the spawn area, not inside a wall, not too close to others
//   2.3 Reachability         : the point is on the NavMesh, AND a complete NavMesh path exists to it
//                              from the player's start, so every artefact can definitely be collected.
//
// Put this on an empty GameObject placed at the CENTRE of your floor, at floor height.
// The white box in the Scene view shows the spawn area. Press R in Play mode to generate a new layout.
public class ArtefactSpawner : MonoBehaviour
{
    [Header("What To Spawn")]
    [Tooltip("Your artefact prefab. If empty, a small yellow cube is created instead.")]
    public GameObject artefactPrefab;
    public int artefactCount = 8;
    [Tooltip("How high above the floor the artefact floats.")]
    public float spawnHeight = 0.5f;

    [Header("Spawn Area (2.2)")]
    [Tooltip("Size of the area, centred on this object. X = width (world X), Y = depth (world Z).")]
    public Vector2 areaSize = new Vector2(20f, 20f);

    [Header("Placement Checks")]
    [Tooltip("Your wall layer (e.g. Unwalkable). If left as Nothing, the A* grid's Unwalkable Mask is used.")]
    public LayerMask obstacleMask;
    [Tooltip("A spot is rejected if a wall is within this distance of it.")]
    public float clearanceRadius = 0.6f;
    [Tooltip("How far a random point may be from the NavMesh and still count as on it.")]
    public float navMeshSampleDistance = 1f;
    [Tooltip("Minimum distance between two artefacts, so they don't clump together.")]
    public float minSpacing = 2f;
    [Tooltip("No artefacts this close to the player's start.")]
    public float minDistanceFromStart = 3f;
    [Tooltip("Reachability is tested FROM here. If empty, the player's start position is used.")]
    public Transform reachabilityOrigin;
    [Tooltip("Gives up on an artefact after this many failed random spots (stops Unity freezing).")]
    public int maxAttemptsPerArtefact = 200;

    [Header("Randomness")]
    [Tooltip("0 = a different layout every time. Any other number = the same layout every time (useful for testing).")]
    public int seed = 0;

    [Header("Demo")]
    [Tooltip("Press R in Play mode to generate a new layout.")]
    public bool allowRegenerateKey = true;
    [Tooltip("Shows 'Artefacts: collected / total' in the top-right of the Game view.")]
    public bool showCounter = true;

    // One spawned artefact and the path that proved it was reachable
    private class SpawnRecord
    {
        public GameObject artefact;
        public Vector3[] pathCorners;
    }

    private readonly List<SpawnRecord> spawned = new List<SpawnRecord>();
    private NavMeshPath path;
    private System.Random rng;
    private Vector3 playerStart;
    private Vector3 originOnNavMesh;
    private int collectedCount;
    private int totalCount;
    private GUIStyle counterStyle;

    // Why spots were rejected - printed to the Console as evidence the checks work
    private int rejectedInsideWall;
    private int rejectedOffNavMesh;
    private int rejectedTooClose;
    private int rejectedUnreachable;

    void Start()
    {
        path = new NavMeshPath();

        // Use the same wall layer as the A* grid if none was set
        if (obstacleMask.value == 0 && Pathfinding.Instance != null)
        {
            GridManager grid = Pathfinding.Instance.GetComponent<GridManager>();
            if (grid != null) obstacleMask = grid.unwalkableMask;
        }
        if (obstacleMask.value == 0)
            obstacleMask = LayerMask.GetMask("Unwalkable", "Wall");

        PlayerController player = FindFirstObjectByType<PlayerController>();
        playerStart = player != null ? player.transform.position : transform.position;

        Generate();
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (allowRegenerateKey && kb != null && kb.rKey.wasPressedThisFrame)
            Generate();
    }

    // Removes the old artefacts and scatters a new set
    [ContextMenu("Regenerate Artefacts")]
    public void Generate()
    {
        ClearSpawned();
        rng = seed != 0 ? new System.Random(seed) : new System.Random();
        rejectedInsideWall = 0;
        rejectedOffNavMesh = 0;
        rejectedTooClose = 0;
        rejectedUnreachable = 0;
        collectedCount = 0;

        // The path start must itself be ON the NavMesh, or every path test fails.
        // (The player's position is the middle of its body, about 1 m above the NavMesh.)
        Vector3 origin = reachabilityOrigin != null ? reachabilityOrigin.position : playerStart;
        NavMeshHit originHit;
        if (!NavMesh.SamplePosition(origin, out originHit, 3f, NavMesh.AllAreas))
        {
            Debug.LogError("ArtefactSpawner: the start point isn't near the NavMesh. Is the NavMesh baked?");
            return;
        }
        originOnNavMesh = originHit.position;

        for (int i = 0; i < artefactCount; i++)
        {
            Vector3 spot;
            Vector3[] corners;
            if (TryFindValidSpot(out spot, out corners))
            {
                SpawnArtefact(spot, corners);
            }
            else
            {
                Debug.LogWarning("ArtefactSpawner: only found room for " + i + " of " + artefactCount +
                                 " artefacts. Make the area bigger or lower Min Spacing.");
                break;
            }
        }

        totalCount = spawned.Count;
        Debug.Log("ArtefactSpawner: placed " + totalCount + " artefacts. Rejected spots: " +
                  rejectedInsideWall + " inside walls, " +
                  rejectedOffNavMesh + " off the NavMesh, " +
                  rejectedTooClose + " too close to others or the start, " +
                  rejectedUnreachable + " unreachable (no complete path).");
    }

    // Tries random spots until one passes every check, or gives up after maxAttemptsPerArtefact
    bool TryFindValidSpot(out Vector3 spot, out Vector3[] corners)
    {
        for (int attempt = 0; attempt < maxAttemptsPerArtefact; attempt++)
        {
            // ---- 2.2 Step 1: random point inside the spawn area ----
            float x = Mathf.Lerp(-areaSize.x / 2f, areaSize.x / 2f, (float)rng.NextDouble());
            float z = Mathf.Lerp(-areaSize.y / 2f, areaSize.y / 2f, (float)rng.NextDouble());
            Vector3 candidate = transform.position + new Vector3(x, 0f, z);

            // ---- 2.2 Step 2: not inside or touching a wall ----
            if (Physics.CheckSphere(candidate + Vector3.up * clearanceRadius, clearanceRadius, obstacleMask))
            {
                rejectedInsideWall++;
                continue;
            }

            // ---- 2.3 Step 1: the point must be on the walkable NavMesh ----
            NavMeshHit navHit;
            if (!NavMesh.SamplePosition(candidate, out navHit, navMeshSampleDistance, NavMesh.AllAreas))
            {
                rejectedOffNavMesh++;
                continue;
            }
            Vector3 point = navHit.position; // Snapped exactly onto the NavMesh

            // ---- 2.2 Step 3: spread out - not on top of another artefact or the player's start ----
            if (TooCloseToOthers(point))
            {
                rejectedTooClose++;
                continue;
            }

            // ---- 2.3 Steps 2 and 3: a COMPLETE path must exist from the start to this point ----
            // PathPartial = only gets part of the way (e.g. a closed room). PathInvalid = no path at all.
            bool foundPath = NavMesh.CalculatePath(originOnNavMesh, point, NavMesh.AllAreas, path);
            if (!foundPath || path.status != NavMeshPathStatus.PathComplete)
            {
                rejectedUnreachable++;
                continue;
            }

            // Passed every check
            spot = point;
            corners = path.corners;
            return true;
        }

        spot = Vector3.zero;
        corners = null;
        return false;
    }

    bool TooCloseToOthers(Vector3 point)
    {
        if (FlatDistance(point, playerStart) < minDistanceFromStart) return true;

        foreach (SpawnRecord record in spawned)
        {
            if (record.artefact != null && FlatDistance(point, record.artefact.transform.position) < minSpacing)
                return true;
        }
        return false;
    }

    // ---- 2.3 Step 4: only now create the artefact ----
    void SpawnArtefact(Vector3 spot, Vector3[] corners)
    {
        Vector3 position = spot + Vector3.up * spawnHeight;
        GameObject artefact;

        if (artefactPrefab != null)
        {
            artefact = Instantiate(artefactPrefab, position, Quaternion.identity, transform);
        }
        else
        {
            // No prefab given: make a small yellow cube
            artefact = GameObject.CreatePrimitive(PrimitiveType.Cube);
            artefact.transform.SetParent(transform);
            artefact.transform.position = position;
            artefact.transform.localScale = Vector3.one * 0.5f;
            Renderer artefactRenderer = artefact.GetComponent<Renderer>();
            artefactRenderer.material.color = Color.yellow;               // Built-in pipeline
            artefactRenderer.material.SetColor("_BaseColor", Color.yellow); // URP
        }
        artefact.name = "Artefact " + (spawned.Count + 1);

        // Make sure it can be picked up: trigger collider + Artefact script
        Collider[] colliders = artefact.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0) artefact.AddComponent<SphereCollider>().isTrigger = true;
        foreach (Collider c in colliders) c.isTrigger = true;

        Artefact pickup = artefact.GetComponent<Artefact>();
        if (pickup == null) pickup = artefact.AddComponent<Artefact>();
        pickup.spawner = this;

        SpawnRecord record = new SpawnRecord();
        record.artefact = artefact;
        record.pathCorners = corners;
        spawned.Add(record);
    }

    // Called by an Artefact when the player picks it up
    public void OnArtefactCollected(Artefact artefact)
    {
        collectedCount++;
        Debug.Log("Artefact collected: " + collectedCount + " / " + totalCount);
        if (collectedCount >= totalCount && totalCount > 0)
            Debug.Log("All artefacts collected!" + (allowRegenerateKey ? " Press R for a new layout." : ""));
    }

    void ClearSpawned()
    {
        foreach (SpawnRecord record in spawned)
        {
            if (record.artefact != null) Destroy(record.artefact);
        }
        spawned.Clear();
    }

    float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ======================= Counter (Game view) =======================
    void OnGUI()
    {
        if (!showCounter || Event.current.type != EventType.Repaint) return;

        float s = Screen.height / 1080f;
        if (counterStyle == null)
        {
            counterStyle = new GUIStyle(GUI.skin.label);
            counterStyle.alignment = TextAnchor.MiddleRight;
            counterStyle.fontStyle = FontStyle.Bold;
            counterStyle.normal.textColor = Color.white;
        }
        counterStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(18f * s));

        string text = "Artefacts: " + collectedCount + " / " + totalCount;
        if (collectedCount >= totalCount && totalCount > 0)
            text = "All artefacts collected!" + (allowRegenerateKey ? "  (R = new layout)" : "");

        Rect rect = new Rect(Screen.width - 520f * s, 16f * s, 500f * s, 30f * s);
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, counterStyle);
        GUI.color = Color.yellow;
        GUI.Label(rect, text, counterStyle);
        GUI.color = old;
    }

    // ======================= Gizmos (Scene view) =======================
    // White box = spawn area. Cyan lines = the verified NavMesh path to each remaining artefact (proof of 2.3).
    void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 0.2f, areaSize.y));

        if (!Application.isPlaying) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(originOnNavMesh, 0.4f);

        Gizmos.color = Color.cyan;
        foreach (SpawnRecord record in spawned)
        {
            if (record.artefact == null || record.pathCorners == null) continue;
            for (int i = 1; i < record.pathCorners.Length; i++)
                Gizmos.DrawLine(record.pathCorners[i - 1] + Vector3.up * 0.1f, record.pathCorners[i] + Vector3.up * 0.1f);
        }
    }
}
