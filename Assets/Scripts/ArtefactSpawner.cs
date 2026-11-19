using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Where an artefact type is allowed to appear (a "location suitability" rule)
public enum ArtefactPlacement
{
    Anywhere,    // No preference
    NearCover,   // A wall must be close by (defensive items: somewhere safe to use them)
    OpenGround   // No wall close by (risky to grab, room to use them)
}

// One kind of artefact. Edit these in the Inspector.
[System.Serializable]
public class ArtefactType
{
    public string name = "Artefact";
    [Tooltip("Optional prefab. If empty, a coloured primitive shape is made.")]
    public GameObject prefab;
    public PrimitiveType shape = PrimitiveType.Cube;
    public Color color = Color.yellow;
    public float size = 0.5f;
    [Tooltip("Rotation of the shape, e.g. (45, 0, 45) turns a cube into a diamond.")]
    public Vector3 tilt = Vector3.zero;
    [Tooltip("How many of this type to place (the brief needs at least 3).")]
    [Min(1)] public int count = 3;
    public ArtefactPlacement placement = ArtefactPlacement.Anywhere;
    [Tooltip("Health given if the player picks it up (0 = none). Pick-up is optional for the brief.")]
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

// ARTEFACT SPAWNER (section 2.2: procedural artefact generation)
// Places 6 distinct artefact types, 3 of each (18 in total), using these rules:
//
// VALIDITY (never broken - an artefact must always be placeable and collectable):
//   1. Not inside or touching a wall                    (Physics.CheckSphere)
//   2. On the walkable NavMesh                           (NavMesh.SamplePosition)
//   3. Reachable: a COMPLETE path from the player's start (NavMesh.CalculatePath + PathComplete)
//
// PLACEMENT CONSTRAINTS (the design rules):
//   4. Minimum spacing    : at least 2 m from any artefact, and 5 m from one of the SAME type
//   5. Exclusion zones    : nothing within 4 m of the player's start or of any NPC's start
//   6. Balanced regions   : the arena is split into regions (2 x 2) and each artefact goes to the
//                           region with the fewest so far, so they spread evenly over the arena
//   7. Location suitability: each type has a preferred kind of spot -
//                           Health Pack + Shield near cover, Speed Boost + Extra Health in the open
//
// Rules 6 and 7 are "soft": if a spot can't be found with them after many tries, they are relaxed
// (rules 1-5 never are), so generation always finishes. The Console reports how often that happened.
//
// Everything resets with the level: when the player dies, the GameManager reloads the scene and
// this runs again. Seed = 0 gives a new layout each time; any other seed gives the same layout.
//
// Put this on an empty GameObject at the CENTRE of your floor, at floor height.
public class ArtefactSpawner : MonoBehaviour
{
    [Header("Artefact Types (six types, at least 3 of each)")]
    public List<ArtefactType> artefactTypes = new List<ArtefactType>
    {
        new ArtefactType("Health Pack",  PrimitiveType.Cube, new Color(0.2f, 0.9f, 0.3f), 0.5f, Vector3.zero, ArtefactPlacement.NearCover, 20),
        new ArtefactType("Speed Boost", PrimitiveType.Capsule, Color.cyan, 0.4f, Vector3 zero,    ArtefactPlacement.OpenGround, 0),
        new ArtefactType("Hit Boost", PrimitiveType.Cylinder, new Color(1f, 0.5f, 0f), 0.45f, Vector3.zero, ArtefactPlacement.Anywhere, 0),
        new ArtefactType("Shield", PrimitiveType.Sphere,   new Color(0.2f, 0.4f, 1f), 0.6f,  Vector3.zero, ArtefactPlacement.NearCover, 0),
        new ArtefactType("Collectible", PrimitiveType.Cube, Color.yellow, 0.35f, new Vector3(45f, 0f, 45f), ArtefactPlacement.Anywhere, 0),
        new ArtefactType("Extra Health", PrimitiveType.Sphere, new Color(1f, 0.25f, 0.5f), 0.4f, Vector3.zero, ArtefactPlacement.OpenGround, 40)
    };
    [Tooltip("How high above the floor artefacts float.")]
    public float spawnHeight = 0.5f;

    [Header("Spawn Area")]
    [Tooltip("Size of the area, centred on this object. X = width (world X), Y = depth (world Z).")]
    public Vector2 areaSize = new Vector2(20f, 20f);
    [Tooltip("The area is split into this many regions (X by Z); artefacts are shared out evenly between them.")]
    public Vector2Int regions = new Vector2Int(2, 2);

    [Header("Validity Checks (never relaxed)")]
    [Tooltip("Your wall layer (e.g. Unwalkable). If left as Nothing, the A* grid's Unwalkable Mask is used.")]
    public LayerMask obstacleMask;
    [Tooltip("A spot is rejected if a wall is within this distance of it.")]
    public float clearanceRadius = 0.6f;
    [Tooltip("How far a random point may be from the NavMesh and still count as on it.")]
    public float navMeshSampleDistance = 1f;
    [Tooltip("Reachability is tested FROM here. If empty, the player's start position is used.")]
    public Transform reachabilityOrigin;

    [Header("Placement Rules")]
    [Tooltip("Minimum distance between any two artefacts.")]
    public float minSpacing = 2f;
    [Tooltip("Minimum distance between two artefacts of the SAME type, so each type is spread out.")]
    public float sameTypeSpacing = 5f;
    [Tooltip("No artefacts this close to the player's start.")]
    public float playerExclusionRadius = 4f;
    [Tooltip("No artefacts this close to any NPC's start position.")]
    public float npcExclusionRadius = 4f;
    [Tooltip("'Near cover' = a wall within this distance. 'Open ground' = no wall within it.")]
    public float coverDistance = 2.5f;

    [Header("Reliability")]
    [Tooltip("Most random spots tried per artefact before giving up (stops Unity freezing).")]
    public int maxAttemptsPerArtefact = 300;
    [Tooltip("Failed tries in one region before it is skipped for the current artefact.")]
    public int attemptsPerRegion = 40;

    [Header("Randomness")]
    [Tooltip("0 = a new layout every time. Any other number = the same layout every time.")]
    public int seed = 0;

    [Header("Display")]
    [Tooltip("Shows 'Artefacts: collected / total' in the top-right of the Game view.")]
    public bool showCounter = true;
    [Tooltip("Shows each artefact's type name above it in the Game view.")]
    public bool showLabels = true;

    // One placed artefact and the path that proved it was reachable
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

    // Why spots were rejected - printed to the Console as evidence the rules are working
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

        // Remember where every NPC starts, for the exclusion zones
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

        // The path start must itself be ON the NavMesh, or every reachability test fails
        Vector3 origin = reachabilityOrigin != null ? reachabilityOrigin.position : playerStart;
        NavMeshHit originHit;
        if (!NavMesh.SamplePosition(origin, out originHit, 3f, NavMesh.AllAreas))
        {
            Debug.LogError("ArtefactSpawner: the start point isn't near the NavMesh. Is the NavMesh baked?");
            return;
        }
        originOnNavMesh = originHit.position;

        // Spawn order: one of each type in turn (round-robin), so no type is left until last
        // when space is running out. e.g. Health, Speed, Hit, Shield, Collectible, Extra, Health, ...
        List<int> order = new List<int>();
        int mostOfOneType = 0;
        foreach (ArtefactType type in artefactTypes) mostOfOneType = Mathf.Max(mostOfOneType, type.count);
        for (int round = 0; round < mostOfOneType; round++)
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
                Debug.LogWarning("ArtefactSpawner: no valid spot found for a " + artefactTypes[typeIndex].name + ". Make the area bigger or lower the spacing.");
            }
        }

        totalCount = spawned.Count;
        PrintSummary(order.Count, failed);
    }

    // Tries random spots until one passes every rule, or gives up after maxAttemptsPerArtefact.
    // First pass ("strict"): all rules, aimed at the emptiest region.
    // Second pass ("relaxed"): anywhere in the area, without the location-suitability rule.
    bool TryFindSpot(int typeIndex, out Vector3 spot, out Vector3[] corners, out bool relaxed)
    {
        ArtefactType type = artefactTypes[typeIndex];
        bool[] regionSkipped = new bool[regionCounts.Length];
        int[] regionFailures = new int[regionCounts.Length];

        for (int attempt = 0; attempt < maxAttemptsPerArtefact; attempt++)
        {
            // ---- Rule 6: balanced regions - aim for the region with the fewest artefacts ----
            int targetRegion = LeastFilledRegion(regionSkipped);
            bool strict = targetRegion >= 0; // -1 = every region has been skipped: relax the soft rules
            Vector3 candidate = RandomPointInRegion(targetRegion);

            bool passed = CheckSpot(type, typeIndex, candidate, strict, out spot);
            if (passed)
            {
                corners = path.corners;
                relaxed = !strict;
                return true;
            }

            // Too many failures in this region: skip it for this artefact and try the next emptiest
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

    // Runs every rule on one candidate point. Cheap checks first, the path calculation last.
    bool CheckSpot(ArtefactType type, int typeIndex, Vector3 candidate, bool strict, out Vector3 spot)
    {
        spot = candidate;

        // ---- Validity 1: not inside or touching a wall ----
        if (Physics.CheckSphere(candidate + Vector3.up * clearanceRadius, clearanceRadius, obstacleMask))
        {
            rejectedInsideWall++;
            return false;
        }

        // ---- Validity 2: on the walkable NavMesh ----
        NavMeshHit navHit;
        if (!NavMesh.SamplePosition(candidate, out navHit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            rejectedOffNavMesh++;
            return false;
        }
        Vector3 point = navHit.position; // Snapped exactly onto the NavMesh

        // ---- Rule 5: exclusion zones around the player's start and the NPCs ----
        if (InExclusionZone(point))
        {
            rejectedExclusion++;
            return false;
        }

        // ---- Rule 4: minimum spacing (any type) and same-type spacing ----
        int spacingResult = SpacingCheck(point, typeIndex);
        if (spacingResult == 1)
        {
            rejectedSpacing++;
            return false;
        }
        if (spacingResult == 2)
        {
            rejectedSameType++;
            return false;
        }

        // ---- Rule 7: location suitability (soft - only in the strict pass) ----
        if (strict && !SuitableFor(type, point))
        {
            rejectedUnsuitable++;
            return false;
        }

        // ---- Validity 3: reachable - a COMPLETE path must exist from the player's start ----
        bool foundPath = NavMesh.CalculatePath(originOnNavMesh, point, NavMesh.AllAreas, path);
        if (!foundPath || path.status != NavMeshPathStatus.PathComplete)
        {
            rejectedUnreachable++;
            return false;
        }

        spot = point;
        return true;
    }

    bool InExclusionZone(Vector3 point)
    {
        if (FlatDistance(point, playerStart) < playerExclusionRadius) return true;

        foreach (Vector3 npcStart in npcStartPositions)
        {
            if (FlatDistance(point, npcStart) < npcExclusionRadius) return true;
        }
        return false;
    }

    // 0 = fine, 1 = too close to any artefact, 2 = too close to one of the same type
    int SpacingCheck(Vector3 point, int typeIndex)
    {
        foreach (SpawnRecord record in spawned)
        {
            float distance = FlatDistance(point, record.spot);
            if (distance < minSpacing) return 1;
            if (record.typeIndex == typeIndex && distance < sameTypeSpacing) return 2;
        }
        return 0;
    }

    bool SuitableFor(ArtefactType type, Vector3 point)
    {
        if (type.placement == ArtefactPlacement.Anywhere) return true;

        bool wallNearby = Physics.CheckSphere(point + Vector3.up * 0.5f, coverDistance, obstacleMask);
        if (type.placement == ArtefactPlacement.NearCover) return wallNearby;
        return !wallNearby; // OpenGround
    }

    // ======================= Regions =======================
    // Returns the region (not skipped) with the fewest artefacts; ties are broken at random. -1 if none left.
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

    // A random point inside one region, or anywhere in the area if region is -1
    Vector3 RandomPointInRegion(int region)
    {
        float minX = -areaSize.x / 2f, maxX = areaSize.x / 2f;
        float minZ = -areaSize.y / 2f, maxZ = areaSize.y / 2f;

        if (region >= 0)
        {
            float regionWidth = areaSize.x / regions.x;
            float regionDepth = areaSize.y / regions.y;
            int column = region % regions.x;
            int row = region / regions.x;
            minX = -areaSize.x / 2f + column * regionWidth;
            maxX = minX + regionWidth;
            minZ = -areaSize.y / 2f + row * regionDepth;
            maxZ = minZ + regionDepth;
        }

        float x = Mathf.Lerp(minX, maxX, (float)rng.NextDouble());
        float z = Mathf.Lerp(minZ, maxZ, (float)rng.NextDouble());
        return transform.position + new Vector3(x, 0f, z);
    }

    int RegionOf(Vector3 point)
    {
        Vector3 local = point - transform.position;
        int column = Mathf.Clamp(Mathf.FloorToInt((local.x + areaSize.x / 2f) / (areaSize.x / regions.x)), 0, regions.x - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt((local.z + areaSize.y / 2f) / (areaSize.y / regions.y)), 0, regions.y - 1);
        return row * regions.x + column;
    }

    // ======================= Spawning =======================
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
            artefactRenderer.material.color = type.color;               // Built-in render pipeline
            artefactRenderer.material.SetColor("_BaseColor", type.color); // URP
        }

        placedPerType[typeIndex]++;
        artefact.name = type.name + " " + placedPerType[typeIndex];

        // Make sure it can be picked up: trigger collider + Artefact script
        Collider[] colliders = artefact.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0) artefact.AddComponent<SphereCollider>().isTrigger = true;
        foreach (Collider c in colliders) c.isTrigger = true;

        Artefact pickup = artefact.GetComponent<Artefact>();
        if (pickup == null) pickup = artefact.AddComponent<Artefact>();
        pickup.spawner = this;
        pickup.typeName = type.name;
        pickup.healAmount = type.healAmount;

        regionCounts[RegionOf(spot)]++;

        SpawnRecord record = new SpawnRecord();
        record.artefact = artefact;
        record.typeIndex = typeIndex;
        record.spot = spot;
        record.pathCorners = corners;
        spawned.Add(record);
    }

    void PrintSummary(int requested, int failed)
    {
        string perType = "";
        for (int t = 0; t < artefactTypes.Count; t++)
        {
            if (t > 0) perType += ", ";
            perType += artefactTypes[t].name + " " + placedPerType[t];
        }

        string perRegion = "";
        for (int r = 0; r < regionCounts.Length; r++)
        {
            if (r > 0) perRegion += ", ";
            perRegion += regionCounts[r];
        }

        Debug.Log("ArtefactSpawner: placed " + totalCount + " / " + requested + " artefacts (" + perType + ").\n" +
                  "Per region: " + perRegion + ".  Placed with soft rules relaxed: " + relaxedPlacements + ".\n" +
                  "Rejected spots: " + rejectedInsideWall + " inside walls, " +
                  rejectedOffNavMesh + " off the NavMesh, " +
                  rejectedExclusion + " in exclusion zones, " +
                  rejectedSpacing + " too close to another artefact, " +
                  rejectedSameType + " too close to the same type, " +
                  rejectedUnsuitable + " unsuitable location, " +
                  rejectedUnreachable + " unreachable.");

        if (failed > 0)
            Debug.LogWarning("ArtefactSpawner: " + failed + " artefacts could not be placed.");
    }

    // Called by an Artefact when the player picks it up (pick-up is optional for the brief)
    public void OnArtefactCollected(Artefact artefact)
    {
        collectedCount++;
        Debug.Log("Collected " + artefact.typeName + " (" + collectedCount + " / " + totalCount + ")");
    }

    float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ======================= Game view: counter and labels =======================
    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        float s = Screen.height / 1080f;
        if (counterStyle == null)
        {
            counterStyle = new GUIStyle(GUI.skin.label);
            counterStyle.alignment = TextAnchor.MiddleRight;
            counterStyle.fontStyle = FontStyle.Bold;
            counterStyle.normal.textColor = Color.white;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.wordWrap = false;
            labelStyle.clipping = TextClipping.Overflow;
            labelStyle.normal.textColor = Color.white;
        }
        counterStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(18f * s));
        labelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(11f * s));

        Color oldColor = GUI.color;

        if (showCounter)
        {
            string text = "Artefacts: " + collectedCount + " / " + totalCount;
            Rect rect = new Rect(Screen.width - 520f * s, 16f * s, 500f * s, 30f * s);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, counterStyle);
            GUI.color = Color.yellow;
            GUI.Label(rect, text, counterStyle);
        }

        Camera cam = Camera.main;
        if (showLabels && cam != null)
        {
            foreach (SpawnRecord record in spawned)
            {
                if (record.artefact == null) continue;
                Vector3 screen = cam.WorldToScreenPoint(record.spot + Vector3.up * (spawnHeight + 0.7f));
                if (screen.z <= 0f) continue;

                Rect rect = new Rect(screen.x - 80f * s, Screen.height - screen.y - 10f * s, 160f * s, 20f * s);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), artefactTypes[record.typeIndex].name, labelStyle);
                GUI.color = artefactTypes[record.typeIndex].color;
                GUI.Label(rect, artefactTypes[record.typeIndex].name, labelStyle);
            }
        }

        GUI.color = oldColor;
    }

    // ======================= Scene view gizmos =======================
    // White box = spawn area, grey lines = region borders,
    // coloured lines = the verified NavMesh path to each artefact (in its type's colour).
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
