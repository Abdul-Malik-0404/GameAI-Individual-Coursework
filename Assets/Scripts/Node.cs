using UnityEngine;

// One cell of the pathfinding grid.
public class Node
{
    public Vector3 worldPosition;   // Centre of this cell in the 3D world
    public int gridX;               // Column index in the grid array (runs along world X)
    public int gridY;               // Row index in the grid array (runs along world Z)
    public bool isWalkable;         // False if a wall overlaps this cell
    public Node parentNode;         // The node we came from - used to retrace the final path

    public int gCost;               // Exact cost from the start node to this node
    public int hCost;               // Estimated cost from this node to the target node

    // F-Cost = G-Cost + H-Cost. A* always evaluates the node with the lowest F-Cost next.
    public int fCost
    {
        get { return gCost + hCost; }
    }

    public Node(bool isWalkable, Vector3 worldPosition, int gridX, int gridY)
    {
        this.isWalkable = isWalkable;
        this.worldPosition = worldPosition;
        this.gridX = gridX;
        this.gridY = gridY;
    }
}
