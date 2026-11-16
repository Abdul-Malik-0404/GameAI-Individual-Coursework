using System.Collections.Generic;
using UnityEngine;

// Builds the 2D grid of Nodes that A* searches.
// Put this on an empty GameObject placed at the CENTRE of your floor.
public class GridManager : MonoBehaviour
{
    [Header("Grid Settings")]
    [Tooltip("Size of the area the grid covers. X = width (world X), Y = depth (world Z).")]
    public Vector2 gridWorldSize = new Vector2(20f, 20f);

    [Tooltip("Half the width of one grid cell. Smaller = more accurate paths, but slower.")]
    public float nodeRadius = 0.5f;

    [Tooltip("The layer(s) your walls are on. Do NOT include the floor's layer.")]
    public LayerMask unwalkableMask;

    [Header("Debug")]
    public bool drawGridGizmos = true;

    // The sphere used to test for walls is slightly smaller than the cell,
    // so a wall that only touches the edge of a cell does not block it.
    private const float ObstacleCheckScale = 0.9f;

    private Node[,] grid;
    private Vector3 bottomLeft;     // World position of the grid's bottom-left corner
    private float nodeDiameter;
    private int gridSizeX;
    private int gridSizeY;

    public bool IsReady
    {
        get { return grid != null; }
    }

    void Start()
    {
        CreateGrid();
    }

    // Call this again at runtime if your walls move or change.
    public void CreateGrid()
    {
        nodeRadius = Mathf.Max(0.05f, nodeRadius);
        nodeDiameter = nodeRadius * 2f;
        gridSizeX = Mathf.Max(1, Mathf.RoundToInt(gridWorldSize.x / nodeDiameter));
        gridSizeY = Mathf.Max(1, Mathf.RoundToInt(gridWorldSize.y / nodeDiameter));
        grid = new Node[gridSizeX, gridSizeY];

        bottomLeft = transform.position
                     - Vector3.right * (gridWorldSize.x / 2f)
                     - Vector3.forward * (gridWorldSize.y / 2f);

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                // Centre of this cell in world space
                Vector3 worldPoint = bottomLeft
                                     + Vector3.right * (x * nodeDiameter + nodeRadius)
                                     + Vector3.forward * (y * nodeDiameter + nodeRadius);

                // If the sphere touches anything on the wall layer, the cell is blocked
                bool walkable = !Physics.CheckSphere(worldPoint, nodeRadius * ObstacleCheckScale, unwalkableMask);

                grid[x, y] = new Node(walkable, worldPoint, x, y);
            }
        }
    }

    public Node GetNode(int x, int y)
    {
        return grid[x, y];
    }

    // Converts a world position (e.g. the NPC's or the player's) into the node it stands on.
    public Node NodeFromWorldPoint(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt((worldPosition.x - bottomLeft.x) / nodeDiameter);
        int y = Mathf.FloorToInt((worldPosition.z - bottomLeft.z) / nodeDiameter);

        // Positions outside the grid snap to the nearest edge cell
        x = Mathf.Clamp(x, 0, gridSizeX - 1);
        y = Mathf.Clamp(y, 0, gridSizeY - 1);

        return grid[x, y];
    }

    // Returns the (up to) 8 nodes surrounding the given node.
    public List<Node> GetNeighbours(Node node)
    {
        List<Node> neighbours = new List<Node>(8);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue; // Skip the node itself

                int checkX = node.gridX + x;
                int checkY = node.gridY + y;

                if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY)
                    neighbours.Add(grid[checkX, checkY]);
            }
        }

        return neighbours;
    }

    // If the given node is blocked (e.g. the player is hugging a wall), finds the
    // walkable node closest to the given world position. Returns null if there is none.
    public Node GetClosestWalkableNode(Node node, Vector3 worldPosition)
    {
        if (node.isWalkable)
            return node;

        int maxRadius = Mathf.Max(gridSizeX, gridSizeY);

        // Search outwards in growing square rings around the node
        for (int radius = 1; radius < maxRadius; radius++)
        {
            Node best = null;
            float bestDistance = float.MaxValue;

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius)
                        continue; // Only the outer ring - inner cells were already checked

                    int checkX = node.gridX + x;
                    int checkY = node.gridY + y;

                    if (checkX < 0 || checkX >= gridSizeX || checkY < 0 || checkY >= gridSizeY)
                        continue;

                    Node candidate = grid[checkX, checkY];
                    if (!candidate.isWalkable)
                        continue;

                    float distance = (candidate.worldPosition - worldPosition).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }

            if (best != null)
                return best;
        }

        return null;
    }

    // Scene view helper: white outline = grid area, red cells = blocked (shown in Play mode).
    void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(transform.position, new Vector3(gridWorldSize.x, 1f, gridWorldSize.y));

        if (grid == null || !drawGridGizmos)
            return;

        Vector3 cellSize = new Vector3(nodeDiameter - 0.1f, 0.1f, nodeDiameter - 0.1f);
        foreach (Node node in grid)
        {
            Gizmos.color = node.isWalkable ? new Color(1f, 1f, 1f, 0.25f) : new Color(1f, 0f, 0f, 0.6f);
            Gizmos.DrawCube(node.worldPosition, cellSize);
        }
    }
}
