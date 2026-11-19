using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(GridManager))]
public class Pathfinding : MonoBehaviour
{
    public static Pathfinding Instance { get; private set; }

    [Tooltip("Stops diagonal steps from squeezing past the corner of a wall.")]
    public bool preventCornerCutting = true;

    // Integer step costs: 10 for a straight step, 14 for a diagonal one (10 * sqrt(2) ~ 14)
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;

    private GridManager grid;

    void Awake()
    {
        Instance = this;
        grid = GetComponent<GridManager>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // Returns the world positions to walk through, in order, to get from start to target.
    // Returns null if the grid is not built yet or no path exists.
    public List<Vector3> FindPath(Vector3 startPosition, Vector3 targetPosition)
    {
        if (!grid.IsReady)
            return null;

        Node startNode = grid.NodeFromWorldPoint(startPosition);
        Node targetNode = grid.NodeFromWorldPoint(targetPosition);

        // If the target stands on a blocked cell (e.g. pressed against a wall), aim for the closest walkable cell instead.
        targetNode = grid.GetClosestWalkableNode(targetNode, targetPosition);
        if (targetNode == null)
            return null;

        List<Node> openList = new List<Node>();         // Nodes waiting to be evaluated
        HashSet<Node> closedList = new HashSet<Node>(); // Nodes already evaluated

        // Add the starting node to the Open List
        startNode.gCost = 0;
        startNode.hCost = GetDistance(startNode, targetNode);
        startNode.parentNode = null;
        openList.Add(startNode);

        while (openList.Count > 0)
        {
            // Find the node with the lowest F-Cost (ties go to the lowest H-Cost)
            Node currentNode = openList[0];
            for (int i = 1; i < openList.Count; i++)
            {
                Node candidate = openList[i];
                if (candidate.fCost < currentNode.fCost ||
                    (candidate.fCost == currentNode.fCost && candidate.hCost < currentNode.hCost))
                {
                    currentNode = candidate;
                }
            }

            // Move it from the Open List to the Closed List
            openList.Remove(currentNode);
            closedList.Add(currentNode);

            // Target check - if we reached the target, retrace the path and finish
            if (currentNode == targetNode)
                return RetracePath(startNode, targetNode);

            // Neighbour check - look at all 8 surrounding nodes
            foreach (Node neighbour in grid.GetNeighbours(currentNode))
            {
                // Ignore neighbours that are blocked or already evaluated
                if (!neighbour.isWalkable || closedList.Contains(neighbour))
                    continue;

                if (preventCornerCutting && CutsCorner(currentNode, neighbour))
                    continue;

                int newGCost = currentNode.gCost + GetDistance(currentNode, neighbour);
                bool inOpenList = openList.Contains(neighbour);

                // If this route to the neighbour is shorter, or we have not seen it yet
                if (newGCost < neighbour.gCost || !inOpenList)
                {
                    neighbour.gCost = newGCost;
                    neighbour.hCost = GetDistance(neighbour, targetNode);
                    neighbour.parentNode = currentNode;

                    if (!inOpenList)
                        openList.Add(neighbour);
                }
            }
        }

        // Open List is empty and the target was never reached: no path exists
        return null;
    }

    // True if nothing on the wall layer lies on the straight line between two points.
    public bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        return !Physics.Linecast(from, to, grid.unwalkableMask);
    }

    // Follows the parentNode chain backwards from the target to the start, then reverses it so the path runs start -> target.
    private List<Vector3> RetracePath(Node startNode, Node endNode)
    {
        List<Vector3> path = new List<Vector3>();
        Node currentNode = endNode;

        while (currentNode != startNode)
        {
            path.Add(currentNode.worldPosition);
            currentNode = currentNode.parentNode;
        }

        path.Reverse();
        return path;
    }

    // Distance between two nodes counted in grid steps:
    // take as many diagonal steps as possible, then straight steps for the rest.
    // Used for both the G-Cost (between neighbours) and the H-Cost (to the target).
    private int GetDistance(Node a, Node b)
    {
        int distanceX = Mathf.Abs(a.gridX - b.gridX);
        int distanceY = Mathf.Abs(a.gridY - b.gridY);

        if (distanceX > distanceY)
            return DiagonalCost * distanceY + StraightCost * (distanceX - distanceY);

        return DiagonalCost * distanceX + StraightCost * (distanceY - distanceX);
    }

    // A diagonal step cuts a corner if either of the two cells it squeezes between is a wall.
    private bool CutsCorner(Node from, Node to)
    {
        if (from.gridX == to.gridX || from.gridY == to.gridY)
            return false; // Straight step, no corner involved

        return !grid.GetNode(from.gridX, to.gridY).isWalkable || !grid.GetNode(to.gridX, from.gridY).isWalkable;
    }
}
