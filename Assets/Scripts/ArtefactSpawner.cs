using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum ArtefactPlacement
{
    Anywhere,
    NearCover,
    OpenGround
}

[System.Serializable]
public class ArtefactType
{
    public string name = "Artefact";
    [Tooltip("Optional prefab; spawns a coloured primitive if empty.")]
    public GameObject prefab;
    public PrimitiveType shape = PrimitiveType.Cube;
    public Color color = Color.yellow;
    public float size = 0.5f;
    public Vector3 tilt = Vector3.zero;
    [Min(1)] public int count = 3;
    public ArtefactPlacement placement = ArtefactPlacement.Anywhere;
    public int healAmount = 0;

    public ArtefactType() { }

    public ArtefactType(string name, PrimitiveType shape, Color color, float size, Vector3 tilt,
                        ArtefactPlacement placement, int healAmount)
    {
        this.name = name;
        this.shape = shape;
        this.color = color;
        this.size = size;
        this.tilt = tilt;
        this.placement = placement;
        this.healAmount = healAmount;
        count = 3;
    }
}

// Procedurally places artefacts using hard validity rules and soft region/suitability preferences.
public class ArtefactSpawner : MonoBehaviour
{
    [Header("Artefact Types")]
    public List<ArtefactType> artefactTypes = new List<ArtefactType>
    {
        new ArtefactType("Health Pack", PrimitiveType.Cube, new Color(0.2f, 0.9f, 0.3f), 0.5f, Vector3.zero, ArtefactPlacement.NearCover, 20),
        new ArtefactType("Speed Boost", PrimitiveType.Capsule, Color.cyan, 0.4f, Vector3.zero, ArtefactPlacement.OpenGround, 0),
        new ArtefactType("Hit Boost", PrimitiveType.Cylinder, new Color(1f, 0.5f, 0f), 0.45f, Vector3.zero, ArtefactPlacement.Anywhere, 0),
        new ArtefactType("Shield", PrimitiveType.Sphere, new Color(0.2f, 0.4f, 1f), 0.6f, Vector3.zero, ArtefactPlacement.NearCover, 0),
        new ArtefactType("Collectible", PrimitiveType.Cube, Color.yellow, 0.35f, new Vector3(45f, 0f, 45f), ArtefactPlacement.Anywhere, 0),
        new ArtefactType("Extra Health", PrimitiveType.Sphere, new Color(1f, 0.25f, 0.5f), 0.4f, Vector3.zero, ArtefactPlacement.OpenGround, 40)
    };
    public float spawnHeight = 0.5f;

    [Header("Spawn Area")]
    public Vector2 areaSize = new Vector2(20f, 20f);
    public Vector2Int regions = new Vector2Int(2, 2);

    [Header("Validity Checks")]
    [Tooltip("Wall layer(s). Falls back to the A* grid's Unwalkable Mask if empty.")]
    public LayerMask obstacleMask;
    public float clearanceRadius = 0.6f;
    public float navMeshSampleDistance = 1f;
    [Tooltip("Origin for NavMesh reachability checks; defaults to player start.")]
    public Transform reachabilityOrigin;

    [Header("Placement Rules")]
    public float minSpacing = 2f;
    public float sameTypeSpacing = 5f;
    public float playerExclusionRadius = 4f;
    public float npcExclusionRadius = 4f;
    public float coverDistance = 2.5f;

    [Header("Reliability")]
    public int maxAttemptsPerArtefact = 300;
    public int attemptsPerRegion = 40;

    [Header("Randomness")]
    [Tooltip("0 = random layout each run; non-zero = deterministic seed.")]
    public int seed = 0;

    [Header("Display")]
    public bool showCounter = true;
    public bool showLabels = true;

    private class SpawnRecord
    {
        public GameObject artefact;
        public int typeIndex;
        public Vector3 spot;
        public Vector3[] pathCorners;
    }

    private readonly List<SpawnRecord> spawned = new List<SpawnRecord>();
    private readonly List<Vector3> npcStartPositions = new List<Vector3>();
    private NavMeshPath path;
    private System.Random rng;
    private Vector3 playerStart;
    private Vector3 originOnNavMesh;
    private int[] regionCounts;
    private int[] placedPerType;
    private int collectedCount;
    private int totalCount;
    private GUIStyle counterStyle;
    private GUIStyle labelStyle;

    // Rejection telemetry logged after generation for PCG validation evidence.
    private int rejectedInsideWall;
    private int rejectedOffNavMesh;
    private int rejectedExclusion;
    private int rejectedSpacing;
    private int rejectedSameType;
    private int rejectedUnsuitable;
    private int rejectedUnreachable;
    private int relaxedPlacements;

    void Start()
    {
        path = new NavMeshPath();
        obstacleMask = SpatialUtility.ResolveObstacleMask(obstacleMask);

        PlayerController player = FindFirstObjectByType<PlayerController>();
        playerStart = player != null ? player.transform.position : transform.position;

        foreach (GridBot bot in FindObjectsByType<GridBot>(FindObjectsSortMode.None))
            npcStartPositions.Add(bot.transform.position);
        foreach (TankerBot tanker in FindObjectsByType<TankerBot>(FindObjectsSortMode.None))
            npcStartPositions.Add(tanker.transform.position);

        Generate();
    }

    void Generate()
    {
        rng = seed != 0 ? new System.Random(seed) : new System.Random();
        regions.x = Mathf.Max(1, regions.x);
        regions.y = Mathf.Max(1, regions.y);
        regionCounts = new int[regions.x * regions.y];
        placedPerType = new int[artefactTypes.Count];

        Vector3 origin = reachabilityOrigin != null ? reachabilityOrigin.position : playerStart;
        NavMeshHit originHit;
        if (!NavMesh.SamplePosition(origin, out originHit, 3f, NavMesh.AllAreas))
        {
            Debug.LogError("ArtefactSpawner: start point isn't near the NavMesh. Is the NavMesh baked?");
            return;
        }
        originOnNavMesh = originHit.position;

        // Interleave types round-robin so every type gets fair region distribution.
        List<int> order = new List<int>();
        int maxCount = 0;
        foreach (ArtefactType type in artefactTypes) maxCount = Mathf.Max(maxCount, type.count);
        for (int round = 0; round < maxCount; round++)
        {
            for (int t = 0; t < artefactTypes.Count; t++)
            {
                if (round < artefactTypes[t].count) order.Add(t);
            }
        }

        int failed = 0;
        foreach (int typeIndex in order)
        {
            Vector3 spot;
            Vector3[] corners;
            bool relaxed;
            if (TryFindSpot(typeIndex, out spot, out corners, out relaxed))
            {
                SpawnArtefact(typeIndex, spot, corners);
                if (relaxed) relaxedPlacements++;
            }
            else
            {
                failed++;
                Debug.LogWarning("ArtefactSpawner: no valid spot found for " + artefactTypes[typeIndex].name + ".");
            }
        }

        totalCount = spawned.Count;
        PrintSummary(order.Count, failed);
    }

    // Attempts strict placement in the emptiest region first, then relaxes soft rules if needed.
    bool TryFindSpot(int typeIndex, out Vector3 spot, out Vector3[] corners, out bool relaxed)
    {
        ArtefactType type = artefactTypes[typeIndex];
        bool[] regionSkipped = new bool[regionCounts.Length];
        int[] regionFailures = new int[regionCounts.Length];

        for (int attempt = 0; attempt < maxAttemptsPerArtefact; attempt++)
        {
            int targetRegion = LeastFilledRegion(regionSkipped);
            bool strict = targetRegion >= 0;
            Vector3 candidate = RandomPointInRegion(targetRegion);

            if (CheckSpot(type, typeIndex, candidate, strict, out spot))
            {
                corners = path.corners;
                relaxed = !strict;
                return true;
            }

            if (strict)
            {
                regionFailures[targetRegion]++;
                if (regionFailures[targetRegion] >= attemptsPerRegion)
                    regionSkipped[targetRegion] = true;
            }
        }

        spot = Vector3.zero;
        corners = null;
        relaxed = false;
        return false;
    }

    // Runs cheap spatial checks before the more expensive NavMesh reachability test.
    bool CheckSpot(ArtefactType type, int typeIndex, Vector3 candidate, bool strict, out Vector3 spot)
    {
        spot = candidate;

        if (Physics.CheckSphere(candidate + Vector3.up * clearanceRadius, clearanceRadius, obstacleMask))
        {
            rejectedInsideWall++;
            return false;
        }

        NavMeshHit navHit;
        if (!NavMesh.SamplePosition(candidate, out navHit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            rejectedOffNavMesh++;
            return false;
        }
        Vector3 point = navHit.position;

        if (InExclusionZone(point))
        {
            rejectedExclusion++;
            return false;
        }

        int spacingResult = SpacingCheck(point, typeIndex);
        if (spacingResult == 1) { rejectedSpacing++; return false; }
        if (spacingResult == 2) { rejectedSameType++; return false; }

        if (strict && !SuitableFor(type, point))
        {
            rejectedUnsuitable++;
            return false;
        }

        if (!NavMesh.CalculatePath(originOnNavMesh, point, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            rejectedUnreachable++;
            return false;
        }

        spot = point;
        return true;
    }

    bool InExclusionZone(Vector3 point)
    {
        if (SpatialUtility.FlatDistance(point, playerStart) < playerExclusionRadius) return true;
        foreach (Vector3 npcStart in npcStartPositions)
        {
            if (SpatialUtility.FlatDistance(point, npcStart) < npcExclusionRadius) return true;
        }
        return false;
    }

    // Returns 0 if clear, 1 if violating minSpacing, or 2 if violating sameTypeSpacing.
    int SpacingCheck(Vector3 point, int typeIndex)
    {
        foreach (SpawnRecord record in spawned)
        {
            float distance = SpatialUtility.FlatDistance(point, record.spot);
            if (distance < minSpacing) return 1;
            if (record.typeIndex == typeIndex && distance < sameTypeSpacing) return 2;
        }
        return 0;
    }

    bool SuitableFor(ArtefactType type, Vector3 point)
    {
        if (type.placement == ArtefactPlacement.Anywhere) return true;
        bool wallNearby = Physics.CheckSphere(point + Vector3.up * 0.5f, coverDistance, obstacleMask);
        return type.placement == ArtefactPlacement.NearCover ? wallNearby : !wallNearby;
    }

    int LeastFilledRegion(bool[] skipped)
    {
        int fewest = int.MaxValue;
        for (int r = 0; r < regionCounts.Length; r++)
        {
            if (!skipped[r] && regionCounts[r] < fewest) fewest = regionCounts[r];
        }
        if (fewest == int.MaxValue) return -1;

        List<int> candidates = new List<int>();
        for (int r = 0; r < regionCounts.Length; r++)
        {
            if (!skipped[r] && regionCounts[r] == fewest) candidates.Add(r);
        }
        return candidates[rng.Next(candidates.Count)];
    }

    Vector3 RandomPointInRegion(int region)
    {
        float minX = -areaSize.x / 2f, maxX = areaSize.x / 2f;
        float minZ = -areaSize.y / 2f, maxZ = areaSize.y / 2f;

        if (region >= 0)
        {
            float regionWidth = areaSize.x / regions.x;
            float regionDepth = areaSize.y / regions.y;
            minX = -areaSize.x / 2f + (region % regions.x) * regionWidth;
            maxX = minX + regionWidth;
            minZ = -areaSize.y / 2f + (region / regions.x) * regionDepth;
            maxZ = minZ + regionDepth;
        }

        float x = Mathf.Lerp(minX, maxX, (float)rng.NextDouble());
        float z = Mathf.Lerp(minZ, maxZ, (float)rng.NextDouble());
        return transform.position + new Vector3(x, 0f, z);
    }

    int RegionOf(Vector3 point)
    {
        Vector3 local = point - transform.position;
        int col = Mathf.Clamp(Mathf.FloorToInt((local.x + areaSize.x / 2f) / (areaSize.x / regions.x)), 0, regions.x - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt((local.z + areaSize.y / 2f) / (areaSize.y / regions.y)), 0, regions.y - 1);
        return row * regions.x + col;
    }

    void SpawnArtefact(int typeIndex, Vector3 spot, Vector3[] corners)
    {
        ArtefactType type = artefactTypes[typeIndex];
        Vector3 position = spot + Vector3.up * spawnHeight;
        Quaternion rotation = Quaternion.Euler(type.tilt);
        GameObject artefact;

        if (type.prefab != null)
        {
            artefact = Instantiate(type.prefab, position, rotation, transform);
        }
        else
        {
            artefact = GameObject.CreatePrimitive(type.shape);
            artefact.transform.SetParent(transform);
            artefact.transform.SetPositionAndRotation(position, rotation);
            artefact.transform.localScale = Vector3.one * type.size;

            Renderer artefactRenderer = artefact.GetComponent<Renderer>();
            artefactRenderer.material.color = type.color;
            artefactRenderer.material.SetColor("_BaseColor", type.color);
        }

        placedPerType[typeIndex]++;
        artefact.name = type.name + " " + placedPerType[typeIndex];

        Collider[] colliders = artefact.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0) artefact.AddComponent<SphereCollider>().isTrigger = true;
        foreach (Collider c in colliders) c.isTrigger = true;

        Artefact pickup = artefact.GetComponent<Artefact>();
        if (pickup == null) pickup = artefact.AddComponent<Artefact>();
        pickup.spawner = this;
        pickup.typeName = type.name;
        pickup.healAmount = type.healAmount;

        regionCounts[RegionOf(spot)]++;
        spawned.Add(new SpawnRecord { artefact = artefact, typeIndex = typeIndex, spot = spot, pathCorners = corners });
    }

    void PrintSummary(int requested, int failed)
    {
        string perType = "";
        for (int t = 0; t < artefactTypes.Count; t++)
        {
            if (t > 0) perType += ", ";
            perType += artefactTypes[t].name + " " + placedPerType[t];
        }

        string perRegion = string.Join(", ", regionCounts);
        Debug.Log("ArtefactSpawner: placed " + totalCount + " / " + requested + " artefacts (" + perType + ").\n" +
                  "Per region: " + perRegion + ".  Relaxed: " + relaxedPlacements + ".\n" +
                  "Rejected: " + rejectedInsideWall + " wall, " + rejectedOffNavMesh + " off-NavMesh, " +
                  rejectedExclusion + " exclusion, " + rejectedSpacing + " spacing, " +
                  rejectedSameType + " same-type, " + rejectedUnsuitable + " unsuitable, " +
                  rejectedUnreachable + " unreachable.");

        if (failed > 0)
            Debug.LogWarning("ArtefactSpawner: " + failed + " artefacts could not be placed.");
    }

    public void OnArtefactCollected(Artefact artefact)
    {
        collectedCount++;
        Debug.Log("Collected " + artefact.typeName + " (" + collectedCount + " / " + totalCount + ")");
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        float s = Screen.height / 1080f;
        if (counterStyle == null)
        {
            counterStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleRight);
            labelStyle = HudUtility.CreateLabelStyle(TextAnchor.MiddleCenter);
        }
        counterStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(18f * s));
        labelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(11f * s));

        if (showCounter)
        {
            Rect rect = new Rect(Screen.width - 520f * s, 16f * s, 500f * s, 30f * s);
            HudUtility.DrawShadowLabel(rect, "Artefacts: " + collectedCount + " / " + totalCount, Color.yellow, counterStyle);
        }

        Camera cam = Camera.main;
        if (!showLabels || cam == null) return;

        foreach (SpawnRecord record in spawned)
        {
            if (record.artefact == null) continue;
            Vector3 screen = cam.WorldToScreenPoint(record.spot + Vector3.up * (spawnHeight + 0.7f));
            if (screen.z <= 0f) continue;

            Rect rect = new Rect(screen.x - 80f * s, Screen.height - screen.y - 10f * s, 160f * s, 20f * s);
            HudUtility.DrawShadowLabel(rect, artefactTypes[record.typeIndex].name, artefactTypes[record.typeIndex].color, labelStyle, 1f);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 0.2f, areaSize.y));

        Gizmos.color = Color.gray;
        int columns = Mathf.Max(1, regions.x);
        int rows = Mathf.Max(1, regions.y);
        for (int c = 1; c < columns; c++)
        {
            float x = -areaSize.x / 2f + c * areaSize.x / columns;
            Gizmos.DrawLine(transform.position + new Vector3(x, 0f, -areaSize.y / 2f), transform.position + new Vector3(x, 0f, areaSize.y / 2f));
        }
        for (int r = 1; r < rows; r++)
        {
            float z = -areaSize.y / 2f + r * areaSize.y / rows;
            Gizmos.DrawLine(transform.position + new Vector3(-areaSize.x / 2f, 0f, z), transform.position + new Vector3(areaSize.x / 2f, 0f, z));
        }

        if (!Application.isPlaying) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(originOnNavMesh, 0.4f);

        foreach (SpawnRecord record in spawned)
        {
            if (record.artefact == null || record.pathCorners == null) continue;
            Gizmos.color = artefactTypes[record.typeIndex].color;
            for (int i = 1; i < record.pathCorners.Length; i++)
                Gizmos.DrawLine(record.pathCorners[i - 1] + Vector3.up * 0.1f, record.pathCorners[i] + Vector3.up * 0.1f);
        }
    }
}
