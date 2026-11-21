using System.Collections.Generic;
using UnityEngine;

// Handles A* path following, throttled repathing, and Rigidbody locomotion for GridBot.
public class GridMotor
{
    private const float RepathDistance = 0.5f;

    private readonly Transform transform;
    private readonly Rigidbody rb;
    private readonly Pathfinding pathfinding;

    private List<Vector3> path;
    private int waypointIndex;
    private Vector3 currentDestination;
    private bool hasDestination;
    private float repathTimer;

    private bool wantsToMove;
    private float moveSpeed;
    private bool hasFaceTarget;
    private Vector3 faceTarget;
    private bool isSpinning;
    private float spinSpeed;

    public bool HasArrived { get { return hasDestination && path != null && waypointIndex >= path.Count; } }
    public bool PathFailed { get { return hasDestination && path == null; } }

    public GridMotor(Transform transform, Rigidbody rb, Pathfinding pathfinding)
    {
        this.transform = transform;
        this.rb = rb;
        this.pathfinding = pathfinding;
    }

    public void BeginFrame(float dt)
    {
        repathTimer -= dt;
        wantsToMove = false;
        hasFaceTarget = false;
        isSpinning = false;
    }

    // Repaths only on a new destination, throttled target movement, or failed-path retry.
    public void MoveTowards(Vector3 destination, float speed, float pathUpdateInterval)
    {
        wantsToMove = true;
        moveSpeed = speed;

        bool newDestination = !hasDestination;
        bool moved = hasDestination && SpatialUtility.FlatDistance(destination, currentDestination) > RepathDistance;
        bool retryFailed = path == null && repathTimer <= 0f;

        if (newDestination || (moved && repathTimer <= 0f) || retryFailed)
        {
            currentDestination = destination;
            hasDestination = true;
            repathTimer = pathUpdateInterval;
            path = pathfinding.FindPath(transform.position, destination);
            waypointIndex = 0;
        }
    }

    public void FacePoint(Vector3 point)
    {
        hasFaceTarget = true;
        faceTarget = point;
    }

    public void Spin(float degreesPerSecond)
    {
        isSpinning = true;
        spinSpeed = degreesPerSecond;
    }

    public void ClearDestination()
    {
        hasDestination = false;
        path = null;
        waypointIndex = 0;
        repathTimer = 0f;
    }

    public void ApplyPhysics(float fixedDt, float turnSpeed, float waypointReachedDistance)
    {
        Vector3 moveDirection = wantsToMove ? GetPathDirection(waypointReachedDistance) : Vector3.zero;
        Vector3 velocity = moveDirection * moveSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;

        if (isSpinning)
        {
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, spinSpeed * fixedDt, 0f));
            return;
        }

        Vector3 facing = hasFaceTarget ? faceTarget - transform.position : moveDirection;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facing);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * fixedDt));
        }
    }

    public void DrawPathGizmos()
    {
        if (path == null) return;
        Gizmos.color = Color.white;
        Vector3 previous = transform.position;
        for (int i = waypointIndex; i < path.Count; i++)
        {
            Gizmos.DrawLine(previous, path[i]);
            previous = path[i];
        }
    }

    private Vector3 GetPathDirection(float waypointReachedDistance)
    {
        if (path == null) return Vector3.zero;

        while (waypointIndex < path.Count &&
               SpatialUtility.FlatDistance(transform.position, path[waypointIndex]) <= waypointReachedDistance)
        {
            waypointIndex++;
        }

        if (waypointIndex >= path.Count) return Vector3.zero;

        Vector3 direction = path[waypointIndex] - transform.position;
        direction.y = 0f;
        return direction.normalized;
    }
}
