using UnityEngine;

// Single cell of the A* navigation grid storing walkability and search costs.
public class Node
{
    public Vector3 worldPosition;
    public int gridX;
    public int gridY;
    public bool isWalkable;
    public Node parentNode;

    public int gCost;
    public int hCost;
    public int fCost { get { return gCost + hCost; } }

    public Node(bool isWalkable, Vector3 worldPosition, int gridX, int gridY)
    {
        this.isWalkable = isWalkable;
        this.worldPosition = worldPosition;
        this.gridX = gridX;
        this.gridY = gridY;
    }
}
