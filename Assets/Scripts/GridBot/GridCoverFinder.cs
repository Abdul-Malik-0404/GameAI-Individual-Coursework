using System.Collections.Generic;
using UnityEngine;

// Samples walkable grid nodes for wandering, searching, and LOS-hidden retreat cover.
public static class GridCoverFinder
{
    public static Vector3 RandomWalkablePointNear(GridManager grid, Vector3 centre, float radius)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Node node = grid.NodeFromWorldPoint(centre + new Vector3(offset.x, 0f, offset.y));
            if (node.isWalkable) return node.worldPosition;
        }
        return centre;
    }

    // Scores nearby walkable nodes hidden from the threat; falls back to the furthest open spot.
    public static Vector3 FindCoverPoint(
        GridManager grid, NpcPerception perception, Vector3 myPosition, Vector3 forward,
        float coverSearchRadius, float minDistanceFromThreat)
    {
        Vector3 threat = perception.ThreatPosition;
        Vector3 awayFromThreat = myPosition - threat;
        awayFromThreat.y = 0f;
        awayFromThreat = awayFromThreat.sqrMagnitude > 0.01f ? awayFromThreat.normalized : -forward;

        bool foundCover = false;
        Vector3 bestCover = myPosition;
        float bestCoverScore = float.MinValue;
        Vector3 bestOpen = myPosition + awayFromThreat * coverSearchRadius;
        float bestOpenDistance = float.MinValue;

        float step = grid.nodeRadius * 2f;
        HashSet<Node> checkedNodes = new HashSet<Node>();

        for (float x = -coverSearchRadius; x <= coverSearchRadius; x += step)
        {
            for (float z = -coverSearchRadius; z <= coverSearchRadius; z += step)
            {
                if (x * x + z * z > coverSearchRadius * coverSearchRadius) continue;

                Node node = grid.NodeFromWorldPoint(myPosition + new Vector3(x, 0f, z));
                if (!node.isWalkable || !checkedNodes.Add(node)) continue;

                Vector3 spot = new Vector3(node.worldPosition.x, myPosition.y, node.worldPosition.z);
                Vector3 toSpot = spot - myPosition;
                toSpot.y = 0f;
                float runDistance = toSpot.magnitude;

                if (runDistance > 0.1f && Vector3.Dot(toSpot / runDistance, awayFromThreat) < -0.2f) continue;

                float threatDistance = SpatialUtility.FlatDistance(spot, threat);
                if (threatDistance < minDistanceFromThreat) continue;

                if (!perception.HasLineOfSight(threat, spot))
                {
                    float score = threatDistance * 0.5f - runDistance;
                    if (score > bestCoverScore)
                    {
                        bestCoverScore = score;
                        bestCover = spot;
                        foundCover = true;
                    }
                }
                else if (threatDistance > bestOpenDistance)
                {
                    bestOpenDistance = threatDistance;
                    bestOpen = spot;
                }
            }
        }

        return foundCover ? bestCover : bestOpen;
    }
}
