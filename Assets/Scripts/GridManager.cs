using System.Collections.Generic;
using UnityEngine;

// Builds the 2D walkability grid searched by A* pathfinding.
public class GridManager : MonoBehaviour
{
    [Header("Grid Settings")]
    public Vector2 gridWorldSize = new Vector2(20f, 20f);
    public float nodeRadius = 0.5f;
    [Tooltip("Wall layer(s) only; exclude the floor layer.")]
    public LayerMask unwalkableMask;

    [Header("Debug")]
    public bool drawGridGizmos = true;

    // Slightly shrunk sphere avoids blocking a cell when a wall only grazes its edge.
    private const float ObstacleCheckScale = 0.9f;

    private Node[,] grid;
    private Vector3 bottomLeft;
    private float nodeDiameter;
    private int gridSizeX;
    private int gridSizeY;

    public bool IsReady { get { return grid != null; } }

    void Start()
    {
        CreateGrid();
    }

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
                Vector3 worldPoint = bottomLeft
                    + Vector3.right * (x * nodeDiameter + nodeRadius)
                    + Vector3.forward * (y * nodeDiameter + nodeRadius);
                bool walkable = !Physics.CheckSphere(worldPoint, nodeRadius * ObstacleCheckScale, unwalkableMask);
                grid[x, y] = new Node(walkable, worldPoint, x, y);
            }
        }
    }

    public Node GetNode(int x, int y)
    {
        return grid[x, y];
    }

    public Node NodeFromWorldPoint(Vector3 worldPosition)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt((worldPosition.x - bottomLeft.x) / nodeDiameter), 0, gridSizeX - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt((worldPosition.z - bottomLeft.z) / nodeDiameter), 0, gridSizeY - 1);
        return grid[x, y];
    }

    public List<Node> GetNeighbours(Node node)
    {
        List<Node> neighbours = new List<Node>(8);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;

                int checkX = node.gridX + x;
                int checkY = node.gridY + y;
                if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY)
                    neighbours.Add(grid[checkX, checkY]);
            }
        }

        return neighbours;
    }

    // Expands outward in square rings to find the nearest walkable node when the target cell is blocked.
    public Node GetClosestWalkableNode(Node node, Vector3 worldPosition)
    {
        if (node.isWalkable) return node;

        int maxRadius = Mathf.Max(gridSizeX, gridSizeY);
        for (int radius = 1; radius < maxRadius; radius++)
        {
            Node best = null;
            float bestDistance = float.MaxValue;

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius) continue;

                    int checkX = node.gridX + x;
                    int checkY = node.gridY + y;
                    if (checkX < 0 || checkX >= gridSizeX || checkY < 0 || checkY >= gridSizeY) continue;

                    Node candidate = grid[checkX, checkY];
                    if (!candidate.isWalkable) continue;

                    float distance = (candidate.worldPosition - worldPosition).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }

            if (best != null) return best;
        }

        return null;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(transform.position, new Vector3(gridWorldSize.x, 1f, gridWorldSize.y));

        if (grid == null || !drawGridGizmos) return;

        Vector3 cellSize = new Vector3(nodeDiameter - 0.1f, 0.1f, nodeDiameter - 0.1f);
        foreach (Node node in grid)
        {
            Gizmos.color = node.isWalkable ? new Color(1f, 1f, 1f, 0.25f) : new Color(1f, 0f, 0f, 0.6f);
            Gizmos.DrawCube(node.worldPosition, cellSize);
        }
    }
}
