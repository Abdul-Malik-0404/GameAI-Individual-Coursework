using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody))]
public class NavMeshController : MonoBehaviour
{
    [Header("Targeting")]
    public Transform target;

    [Header("Movement")]
    public float turnSpeed = 10f;

    [Header("Optimization")]
    public float pathUpdateInterval = 0.3f;

    private NavMeshAgent agent;
    private Rigidbody rb;
    private float pathTimer;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();

        // The agent only PLANS the path. The Rigidbody does the actual moving (same as the A* NPC).
        agent.updatePosition = false;
        agent.updateRotation = false;

        rb.isKinematic = false;
        rb.freezeRotation = true; // Stops it tipping over when hit

        // Automatically find the Player if the slot is empty
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
            }
        }
    }

    void Update()
    {
        // Physics can push the body around, so keep the agent's idea of where it is glued to the real body
        agent.nextPosition = transform.position;

        if (target == null) return;

        // Timer prevents the NPC from doing heavy path math every single frame
        pathTimer -= Time.deltaTime;
        if (pathTimer <= 0f)
        {
            pathTimer = pathUpdateInterval;
            agent.SetDestination(target.position);
        }
    }

    void FixedUpdate()
    {
        Vector3 move = Vector3.zero;

        bool arrived = !agent.pathPending && agent.hasPath && agent.remainingDistance <= agent.stoppingDistance;
        if (agent.hasPath && !arrived)
        {
            // The velocity the agent WANTS (direction along the path x agent Speed)
            move = agent.desiredVelocity;
            move.y = 0f;
        }

        // Overwrite the velocity every physics step, so a bullet's push only lasts one step.
        // Keep Y so gravity still works.
        rb.linearVelocity = new Vector3(move.x, rb.linearVelocity.y, move.z);

        // Turn to face the direction of travel
        if (move.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(move);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
        }
    }
}
