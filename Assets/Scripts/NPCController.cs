using System.Collections.Generic;
using UnityEngine;

// Moves an NPC along the A* path towards a target (the player).
// Put this on the NPC. The NPC also needs a Collider.
[RequireComponent(typeof(Rigidbody))]
public class NPCController : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Who to chase. If left empty, the object tagged 'Player' is used.")]
    public Transform target;

    [Header("Movement Settings")]
    public float moveSpeed = 3.5f;
    public float turnSpeed = 10f;
    [Tooltip("The NPC stops when it is this close to the target and can see it.")]
    public float stoppingDistance = 1.5f;

    [Header("Pathfinding Settings")]
    [Tooltip("Seconds between path recalculations (the player keeps moving, so the path must be refreshed).")]
    public float pathUpdateInterval = 0.3f;
    [Tooltip("How close the NPC must get to a waypoint before heading for the next one.")]
    public float waypointReachedDistance = 0.2f;

    private Rigidbody rb;
    private List<Vector3> path;     // Waypoints from A*, in walking order
    private int waypointIndex;      // The waypoint we are currently walking towards
    private float pathTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // Stops the NPC tipping over when it bumps into things

        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                target = player.transform;
        }
    }

    void Update()
    {
        if (target == null || Pathfinding.Instance == null)
            return;

        // Ask A* for a fresh path every pathUpdateInterval seconds
        pathTimer -= Time.deltaTime;
        if (pathTimer <= 0f)
        {
            pathTimer = pathUpdateInterval;
            path = Pathfinding.Instance.FindPath(transform.position, target.position);
            waypointIndex = 0;
        }
    }

    void FixedUpdate()
    {
        Vector3 direction = GetMoveDirection();

        // Move with the Rigidbody so walls still stop the NPC; keep Y so gravity still works
        Vector3 velocity = direction * moveSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;

        // Turn the NPC's front face towards where it is walking
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
        }
    }

    // Works out which way to walk this physics step (zero = stand still).
    Vector3 GetMoveDirection()
    {
        if (target == null || path == null || Pathfinding.Instance == null)
            return Vector3.zero;

        // Close enough and nothing in the way: stop.
        // (The line-of-sight test stops the NPC "arriving" on the wrong side of a wall.)
        if (FlatDistance(transform.position, target.position) <= stoppingDistance &&
            Pathfinding.Instance.HasLineOfSight(transform.position, target.position))
        {
            return Vector3.zero;
        }

        // Skip any waypoints we have already reached
        while (waypointIndex < path.Count &&
               FlatDistance(transform.position, path[waypointIndex]) <= waypointReachedDistance)
        {
            waypointIndex++;
        }

        // No waypoints left means we are in the target's cell, so walk straight at the target
        Vector3 destination = waypointIndex < path.Count ? path[waypointIndex] : target.position;

        Vector3 toDestination = destination - transform.position;
        toDestination.y = 0f;
        return toDestination.normalized;
    }

    // Distance ignoring height, since the grid is flat
    float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // Scene view helper: draws the NPC's current path in green.
    void OnDrawGizmos()
    {
        if (path == null)
            return;

        Gizmos.color = Color.green;
        Vector3 previous = transform.position;
        for (int i = waypointIndex; i < path.Count; i++)
        {
            Gizmos.DrawLine(previous, path[i]);
            Gizmos.DrawSphere(path[i], 0.1f);
            previous = path[i];
        }
    }
}
