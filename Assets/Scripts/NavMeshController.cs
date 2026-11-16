using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NavMeshController : MonoBehaviour
{
    [Header("Targeting")]
    public Transform target;
    
    [Header("Optimization")]
    public float pathUpdateInterval = 0.3f;
    
    private NavMeshAgent agent;
    private float pathTimer;

    void Start()
    {
        // Grab the physical NavMeshAgent component off the NPC
        agent = GetComponent<NavMeshAgent>();

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
        if (target == null) return;

        // Timer prevents the NPC from doing heavy path math every single frame
        pathTimer -= Time.deltaTime;
        if (pathTimer <= 0f)
        {
            pathTimer = pathUpdateInterval;
            
            // The core command: Tells Unity's C++ backend to calculate the path and steer
            agent.SetDestination(target.position);
        }
    }
}
