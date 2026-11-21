using UnityEngine;
using UnityEngine.AI;

// Bridges NavMeshAgent path planning with Rigidbody movement and straight-line charge overrides.
public class NavMeshMotor
{
    private const float RepathDistance = 0.5f;

    private readonly Transform transform;
    private readonly NavMeshAgent agent;
    private readonly Rigidbody rb;

    private Vector3 currentDestination;
    private bool hasDestination;
    private float repathTimer;

    private bool wantsToMove;
    private bool hasFaceTarget;
    private Vector3 faceTarget;

    public float StoppingDistance { get { return agent.stoppingDistance; } }
    public bool HasArrived { get { return hasDestination && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f; } }
    public bool PathFailed { get { return hasDestination && !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid; } }

    public float HorizontalSpeed
    {
        get
        {
            Vector3 v = rb.linearVelocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    public NavMeshMotor(Transform transform, NavMeshAgent agent, Rigidbody rb)
    {
        this.transform = transform;
        this.agent = agent;
        this.rb = rb;

        // NavMeshAgent plans routes while the Rigidbody owns physical movement and collisions.
        agent.updatePosition = false;
        agent.updateRotation = false;
        rb.isKinematic = false;
        rb.freezeRotation = true;
    }

    public void BeginFrame(float dt, float speed)
    {
        agent.nextPosition = transform.position;
        agent.speed = speed;
        repathTimer -= dt;
        wantsToMove = false;
        hasFaceTarget = false;
    }

    public void MoveTo(Vector3 destination, float pathUpdateInterval)
    {
        wantsToMove = true;

        bool newDestination = !hasDestination;
        bool moved = hasDestination && SpatialUtility.FlatDistance(destination, currentDestination) > RepathDistance;

        if (newDestination || (moved && repathTimer <= 0f))
        {
            currentDestination = destination;
            hasDestination = true;
            repathTimer = pathUpdateInterval;
            agent.SetDestination(destination);
        }
    }

    public void FacePoint(Vector3 point)
    {
        hasFaceTarget = true;
        faceTarget = point;
    }

    public void ClearDestination()
    {
        hasDestination = false;
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
    }

    public bool HasClearNavMeshLine(Vector3 targetPosition)
    {
        NavMeshHit hit;
        return !NavMesh.Raycast(agent.nextPosition, targetPosition, out hit, NavMesh.AllAreas);
    }

    public void StopHorizontal()
    {
        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
    }

    public void SetHorizontalVelocity(Vector3 xzVelocity)
    {
        rb.linearVelocity = new Vector3(xzVelocity.x, rb.linearVelocity.y, xzVelocity.z);
    }

    public void ApplyNavMeshPhysics(float fixedDt, float turnSpeed)
    {
        Vector3 move = Vector3.zero;
        if (wantsToMove && agent.hasPath && !HasArrived)
        {
            move = agent.desiredVelocity;
            move.y = 0f;
        }

        rb.linearVelocity = new Vector3(move.x, rb.linearVelocity.y, move.z);

        Vector3 facing = hasFaceTarget ? faceTarget - transform.position : move;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facing);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * fixedDt));
        }
    }
}
