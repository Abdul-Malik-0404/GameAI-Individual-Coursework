using System.Collections.Generic;
using UnityEngine;

// 8-directional A* pathfinder with octile heuristic and optional corner-cutting prevention.
[RequireComponent(typeof(GridManager))]
public class Pathfinding : MonoBehaviour
{
    public static Pathfinding Instance { get; private set; }

    public bool preventCornerCutting = true;

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
        if (Instance == this) Instance = null;
    }

    // Returns ordered world waypoints from start to target, or null if unreachable.
    public List<Vector3> FindPath(Vector3 startPosition, Vector3 targetPosition)
    {
        if (!grid.IsReady) return null;

        Node startNode = grid.NodeFromWorldPoint(startPosition);
        Node targetNode = grid.GetClosestWalkableNode(grid.NodeFromWorldPoint(targetPosition), targetPosition);
        if (targetNode == null) return null;

        List<Node> openList = new List<Node>();
        HashSet<Node> closedList = new HashSet<Node>();

        startNode.gCost = 0;
        startNode.hCost = GetDistance(startNode, targetNode);
        startNode.parentNode = null;
        openList.Add(startNode);

        while (openList.Count > 0)
        {
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

            openList.Remove(currentNode);
            closedList.Add(currentNode);

            if (currentNode == targetNode)
                return RetracePath(startNode, targetNode);

            foreach (Node neighbour in grid.GetNeighbours(currentNode))
            {
                if (!neighbour.isWalkable || closedList.Contains(neighbour)) continue;
                if (preventCornerCutting && CutsCorner(currentNode, neighbour)) continue;

                int newGCost = currentNode.gCost + GetDistance(currentNode, neighbour);
                bool inOpenList = openList.Contains(neighbour);

                if (newGCost < neighbour.gCost || !inOpenList)
                {
                    neighbour.gCost = newGCost;
                    neighbour.hCost = GetDistance(neighbour, targetNode);
                    neighbour.parentNode = currentNode;

                    if (!inOpenList) openList.Add(neighbour);
                }
            }
        }

        return null;
    }

    public bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        return !Physics.Linecast(from, to, grid.unwalkableMask);
    }

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

    private int GetDistance(Node a, Node b)
    {
        int distanceX = Mathf.Abs(a.gridX - b.gridX);
        int distanceY = Mathf.Abs(a.gridY - b.gridY);

        return distanceX > distanceY
            ? DiagonalCost * distanceY + StraightCost * (distanceX - distanceY)
            : DiagonalCost * distanceX + StraightCost * (distanceY - distanceX);
    }

    // Blocks a diagonal step if either adjacent cardinal cell is unwalkable.
    private bool CutsCorner(Node from, Node to)
    {
        if (from.gridX == to.gridX || from.gridY == to.gridY) return false;
        return !grid.GetNode(from.gridX, to.gridY).isWalkable || !grid.GetNode(to.gridX, from.gridY).isWalkable;
    }
}
