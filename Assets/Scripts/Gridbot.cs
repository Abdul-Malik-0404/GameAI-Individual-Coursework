using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GridBot : MonoBehaviour, IDamageable, IHUDTarget
{
    public enum BotState { Patrol, Investigate, Pursue, Attack, Retreat }

    [Header("State (watch this change in Play mode)")]
    public BotState currentState = BotState.Patrol;

    [Header("Identity")]
    [Tooltip("Name shown above the NPC in game. Leave empty to number automatically (GridBot 1, GridBot 2, ...).")]
    public string npcId = "";

    [Header("References")]
    [Tooltip("Who to fight. If left empty, the object tagged 'Player' is used.")]
    public Transform target;
    [Tooltip("Can be the same bullet prefab the player uses.")]
    public GameObject bulletPrefab;
    [Tooltip("Empty child object at the front of the NPC where bullets spawn.")]
    public Transform firePoint;

    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    [Tooltip("Retreat when health falls to this fraction of max (0.2 = 20%).")]
    [Range(0f, 1f)] public float retreatHealthPercent = 0.2f;
    [Tooltip("Stop retreating once healed back to this fraction. Must be higher than Retreat Health Percent.")]
    [Range(0f, 1f)] public float recoverHealthPercent = 0.6f;
    [Tooltip("Health regained per second while hidden from the player during Retreat.")]
    public float healthRegenPerSecond = 5f;

    [Header("Senses")]
    public float viewDistance = 15f;
    [Tooltip("Width of the view cone in degrees (used while Patrolling / Investigating).")]
    [Range(10f, 360f)] public float viewAngle = 110f;
    [Tooltip("The player is noticed inside this distance even when outside the view cone.")]
    public float proximityRadius = 2.5f;
    [Tooltip("Seconds the player can be out of sight before the bot counts them as lost.")]
    public float loseSightDelay = 0.5f;
    [Tooltip("When the player is lost, guess where they went: last position + velocity x this many seconds.")]
    public float predictionTime = 1f;
    [Tooltip("Hears the player's gunshots within this distance when nothing is in the way.")]
    public float hearingRadius = 15f;
    [Tooltip("Walls muffle sound: through a wall the hearing range is multiplied by this (0.5 = half).")]
    [Range(0f, 1f)] public float wallMuffling = 0.5f;

    [Header("Movement")]
    public float patrolSpeed = 2.5f;    // Patrol and Investigate
    public float chaseSpeed = 4.5f;     // Pursue and Retreat
    public float turnSpeed = 8f;
    public float pathUpdateInterval = 0.3f;
    public float waypointReachedDistance = 0.2f;

    [Header("Patrol")]
    [Tooltip("Empty GameObjects on open floor. Leave empty to wander randomly around the start position.")]
    public Transform[] patrolPoints;
    public float wanderRadius = 8f;
    public float minPatrolWait = 1f;
    public float maxPatrolWait = 3f;
    [Tooltip("Give up on a patrol point if it takes longer than this (stuck).")]
    public float patrolLegTimeout = 15f;

    [Header("Investigate")]
    public float lookAroundDuration = 3f;
    [Tooltip("Degrees per second it turns while scanning an area.")]
    public float lookAroundSpeed = 120f;
    [Tooltip("Chance (0-1) to search another nearby spot after finding nothing.")]
    [Range(0f, 1f)] public float searchAgainChance = 0.5f;
    public int maxExtraSearches = 2;
    public float searchRadius = 4f;
    public float investigateTimeout = 25f;

    [Header("Attack")]
    public float attackRange = 8f;
    [Tooltip("Extra distance before leaving Attack, so it doesn't flicker between Attack and Pursue.")]
    public float attackRangeBuffer = 1.5f;
    public float fireCooldown = 0.8f;
    public float bulletSpeed = 15f;
    public int bulletDamage = 10;
    [Tooltip("Random aim error in degrees, so it doesn't hit every single shot.")]
    public float aimSpread = 4f;

    [Header("Damage Model (Option A)")]
    [Tooltip("GridBot is a marksman: few grazes and a big critical. Average = 1.1 x base damage.")]
    public DamageProfile damageProfile = new DamageProfile(0.25f, 0.5f, 0.15f, 2.5f);

    [Header("Retreat")]
    public float coverSearchRadius = 10f;
    [Tooltip("Hiding spots closer than this to the threat are ignored.")]
    public float minDistanceFromThreat = 5f;
    public float coverCheckInterval = 0.5f;

    [Header("Debug")]
    public bool logStateChanges = true;

    private Rigidbody rb;
    private Rigidbody targetRb;
    private PlayerController targetPlayer;  // Used to ignore the player while they are dead
    private Pathfinding pathfinding;
    private GridManager grid;
    private bool initialized;
    private bool isDead;

    // Path following
    private List<Vector3> path;
    private int waypointIndex;
    private Vector3 currentDestination;
    private bool hasDestination;
    private float repathTimer;
    private const float RepathDistance = 0.5f;

    // What the state logic wants this frame (cleared at the start of every Update)
    private bool wantsToMove;
    private bool hasFaceTarget;
    private Vector3 faceTarget;
    private bool isSpinning;

    // Senses and memory
    private bool seesPlayer;
    private bool hasPlayerMemory;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 lastKnownPlayerVelocity;
    private float lastSeenTime = -999f;
    private bool hasThreat;
    private Vector3 lastThreatPosition;     // Last place danger came from (player seen, or shot fired from)

    // Per-state data
    private float stateTimer;
    private Vector3 homePosition;
    private Vector3 patrolDestination;
    private int patrolIndex = -1;
    private float patrolWaitTimer;
    private float patrolLegTimer;
    private Vector3 investigatePoint;
    private bool isLookingAround;
    private float lookAroundTimer;
    private int searchesDone;
    private float fireTimer;
    private Vector3 coverPoint;
    private float coverCheckTimer;
    private bool coverCompromised;

    private float HealthPercent
    {
        get { return currentHealth / maxHealth; }
    }

    // Best guess of where the danger is right now
    private Vector3 ThreatPosition
    {
        get
        {
            if (seesPlayer) return target.position;
            if (hasThreat) return lastThreatPosition;
            return transform.position + transform.forward; // Unknown: assume it's in front
        }
    }

    // GameHUD reads these to draw the label above this NPC.
    public string Id { get { return npcId; } }
    public string StateName { get { return currentState.ToString(); } }
    public Color StateColor { get { return GetStateColor(currentState); } }
    public float CurrentHealth { get { return currentHealth; } }
    public float MaxHealth { get { return maxHealth; } }

    void Awake()
    {
        // No ID typed in: number the GridBots in scene order (GridBot 1, GridBot 2, ...).
        // Works the same after the level restarts, unlike a counter that keeps counting.
        if (string.IsNullOrEmpty(npcId))
        {
            GridBot[] all = FindObjectsByType<GridBot>(FindObjectsSortMode.InstanceID);
            npcId = "GridBot " + (System.Array.IndexOf(all, this) + 1);
        }
    }

    void OnEnable()
    {
        PlayerController.OnShotFired += HearGunshot;   // Start listening for the player's gunshots
    }

    void OnDisable()
    {
        PlayerController.OnShotFired -= HearGunshot;   // Stop listening (always undo a subscription)
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        currentHealth = maxHealth;
        homePosition = transform.position;

        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }
        if (target != null)
        {
            targetRb = target.GetComponent<Rigidbody>();
            targetPlayer = target.GetComponent<PlayerController>();
        }

        pathfinding = Pathfinding.Instance;
        if (pathfinding == null)
        {
            Debug.LogError(name + ": no Pathfinding in the scene. Add GridManager + Pathfinding to an empty GameObject.");
            enabled = false;
            return;
        }
        grid = pathfinding.GetComponent<GridManager>();

        if (bulletPrefab == null || firePoint == null)
            Debug.LogWarning(name + ": Bullet Prefab or Fire Point is empty, so this bot can't shoot.");
    }

    void Update()
    {
        if (isDead || !grid.IsReady) return;

        // Enter the starting state once the grid has been built
        if (!initialized)
        {
            initialized = true;
            ChangeState(currentState);
        }

        stateTimer += Time.deltaTime;
        repathTimer -= Time.deltaTime;
        fireTimer -= Time.deltaTime;

        wantsToMove = false;
        hasFaceTarget = false;
        isSpinning = false;

        // Senses
        // Calm (Patrol/Investigate): only sees inside its view cone.
        // Alert (Pursue/Attack/Retreat): keeps track of the player all around, as long as no wall is in the way.
        bool alert = currentState == BotState.Pursue || currentState == BotState.Attack || currentState == BotState.Retreat;
        seesPlayer = CanSeePlayer(!alert);
        if (seesPlayer) RememberPlayer();

        // Global transition: low health overrides every other state
        if (currentState != BotState.Retreat && HealthPercent <= retreatHealthPercent)
            ChangeState(BotState.Retreat);

        // Run the current state
        switch (currentState)
        {
            case BotState.Patrol:      UpdatePatrol();      break;
            case BotState.Investigate: UpdateInvestigate(); break;
            case BotState.Pursue:      UpdatePursue();      break;
            case BotState.Attack:      UpdateAttack();      break;
            case BotState.Retreat:     UpdateRetreat();     break;
        }
    }

    void FixedUpdate()
    {
        if (isDead || !initialized) return;

        Vector3 moveDirection = wantsToMove ? GetPathDirection() : Vector3.zero;
        bool fast = currentState == BotState.Pursue || currentState == BotState.Retreat;
        float speed = fast ? chaseSpeed : patrolSpeed;

        // Move with the Rigidbody so walls still stop it; keep Y so gravity still works
        Vector3 velocity = moveDirection * speed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;

        // Scanning an area: turn on the spot
        if (isSpinning)
        {
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, lookAroundSpeed * Time.fixedDeltaTime, 0f));
            return;
        }

        // Otherwise face the requested point, or the direction of travel
        Vector3 facing = hasFaceTarget ? faceTarget - transform.position : moveDirection;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facing);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }
    }

    void ChangeState(BotState newState)
    {
        if (logStateChanges && newState != currentState)
            Debug.Log(name + ": " + currentState + " -> " + newState);

        currentState = newState;
        stateTimer = 0f;
        ClearDestination();

        // One-off setup when entering a state
        switch (newState)
        {
            case BotState.Patrol:
                PickNextPatrolPoint();
                break;

            case BotState.Investigate:
                isLookingAround = false;
                searchesDone = 0;
                break;

            case BotState.Pursue:
                break;

            case BotState.Attack:
                fireTimer = Random.Range(0.2f, fireCooldown); // Random reaction time before the first shot
                break;

            case BotState.Retreat:
                coverPoint = FindCoverPoint();
                coverCheckTimer = coverCheckInterval;
                coverCompromised = false;
                break;
        }
    }

    void StartInvestigating(Vector3 point)
    {
        investigatePoint = point;
        ChangeState(BotState.Investigate);
    }


    // PATROL: walk between random patrol points, pausing a random time at each
    void UpdatePatrol()
    {
        if (seesPlayer)
        {
            ChangeState(BotState.Pursue);
            return;
        }

        // Pausing at a point
        if (patrolWaitTimer > 0f)
        {
            patrolWaitTimer -= Time.deltaTime;
            if (patrolWaitTimer <= 0f) PickNextPatrolPoint();
            return;
        }

        MoveTowards(patrolDestination);
        patrolLegTimer += Time.deltaTime;

        // Arrived, unreachable, or stuck: pause, then pick another point
        if (HasArrived() || PathFailed() || patrolLegTimer > patrolLegTimeout)
            patrolWaitTimer = Mathf.Max(0.1f, Random.Range(minPatrolWait, maxPatrolWait));
    }

    // INVESTIGATE: go to a point of interest (shot origin / last known position), then scan the area
    void UpdateInvestigate()
    {
        if (seesPlayer)
        {
            ChangeState(BotState.Pursue);
            return;
        }

        if (stateTimer > investigateTimeout)
        {
            ChangeState(BotState.Patrol);
            return;
        }

        if (!isLookingAround)
        {
            MoveTowards(investigatePoint);
            if (HasArrived() || PathFailed())
            {
                isLookingAround = true;
                lookAroundTimer = lookAroundDuration;
            }
            return;
        }

        // At the spot: turn around to scan the area
        isSpinning = true;
        lookAroundTimer -= Time.deltaTime;
        if (lookAroundTimer > 0f) return;

        // Found nothing. Probability decides: search another spot nearby, or give up and patrol.
        if (searchesDone < maxExtraSearches && Random.value < searchAgainChance)
        {
            searchesDone++;
            investigatePoint = RandomWalkablePointNear(investigatePoint, searchRadius);
            isLookingAround = false;
            ClearDestination();
        }
        else
        {
            ChangeState(BotState.Patrol);
        }
    }

    // PURSUE: chase the player with A* while it can see them
    void UpdatePursue()
    {
        // Lost sight for more than a moment: go to where it predicts the player went
        if (!seesPlayer && Time.time - lastSeenTime > loseSightDelay)
        {
            StartInvestigating(PredictedPlayerPosition());
            return;
        }

        // Close enough with a clear shot: attack
        if (seesPlayer && FlatDistance(transform.position, target.position) <= attackRange)
        {
            ChangeState(BotState.Attack);
            return;
        }

        // Only uses the player's real position while it can actually see them
        MoveTowards(seesPlayer ? target.position : lastKnownPlayerPosition);

        if (PathFailed())
        {
            hasFaceTarget = true;
            faceTarget = lastKnownPlayerPosition;
        }
    }

    // ATTACK: stand still, face the player and shoot
    void UpdateAttack()
    {
        if (!seesPlayer)
        {
            // Player ducked out of sight: hold for a moment, then go after them
            if (Time.time - lastSeenTime > loseSightDelay)
            {
                StartInvestigating(PredictedPlayerPosition());
            }
            else
            {
                hasFaceTarget = true;
                faceTarget = lastKnownPlayerPosition;
            }
            return;
        }

        // Player backed off out of range: chase again
        if (FlatDistance(transform.position, target.position) > attackRange + attackRangeBuffer)
        {
            ChangeState(BotState.Pursue);
            return;
        }

        hasFaceTarget = true;
        faceTarget = target.position;

        if (fireTimer <= 0f && IsFacing(target.position, 15f))
        {
            ShootAtTarget();
            fireTimer = fireCooldown;
        }
    }

    // RETREAT: run to the best hiding spot, heal while hidden, come back when healthy
    void UpdateRetreat()
    {
        // Heal slowly, but only while the player can't see it
        if (!IsExposedToPlayer())
            currentHealth = Mathf.Min(maxHealth, currentHealth + healthRegenPerSecond * Time.deltaTime);

        // Healthy again: go back on the hunt, starting where the player was last seen
        if (HealthPercent >= recoverHealthPercent)
        {
            if (hasPlayerMemory) StartInvestigating(lastKnownPlayerPosition);
            else ChangeState(BotState.Patrol);
            return;
        }

        // Every so often, check the hiding spot is still hidden from the threat (the player may have moved)
        coverCheckTimer -= Time.deltaTime;
        if (coverCheckTimer <= 0f)
        {
            coverCheckTimer = coverCheckInterval;
            if (coverCompromised || !IsHiddenFrom(ThreatPosition, coverPoint))
            {
                coverPoint = FindCoverPoint();
                coverCompromised = false;
                ClearDestination();
            }
        }

        MoveTowards(coverPoint);

        // In cover: watch the direction the danger will come from
        if (HasArrived())
        {
            hasFaceTarget = true;
            faceTarget = ThreatPosition;
        }
    }

    public void TakeDamage(int damage, Vector3 sourcePosition)
    {
        if (isDead) return;

        currentHealth -= damage;
        Debug.Log(name + " HP: " + currentHealth);

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        // Being shot tells the bot roughly where the danger is
        lastThreatPosition = sourcePosition;
        hasThreat = true;

        if (!initialized) return;

        switch (currentState)
        {
            case BotState.Patrol:
            case BotState.Investigate:
                // Shot by someone it can't see: go and check where the bullet came from
                if (!seesPlayer) StartInvestigating(sourcePosition);
                break;

            case BotState.Retreat:
                // Its hiding spot isn't safe: find another one
                coverCompromised = true;
                break;

            // Pursue / Attack: already fighting, nothing extra to do.
            // (Dropping to low health is handled at the top of Update.)
        }
    }

    void Die()
    {
        isDead = true;
        Debug.Log(name + " defeated!");
        Destroy(gameObject);
    }

    bool CanSeePlayer(bool useViewCone)
    {
        if (target == null) return false;
        if (targetPlayer != null && targetPlayer.IsDead) return false; // Dead: the level is about to restart

        Vector3 toPlayer = target.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance > viewDistance) return false;

        // Outside the view cone it only notices the player if they are very close
        if (useViewCone && distance > proximityRadius && Vector3.Angle(transform.forward, toPlayer) > viewAngle * 0.5f)
            return false;

        // Walls block vision
        return pathfinding.HasLineOfSight(transform.position, target.position);
    }

    void RememberPlayer()
    {
        lastKnownPlayerPosition = target.position;
        lastKnownPlayerVelocity = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        lastSeenTime = Time.time;
        hasPlayerMemory = true;

        lastThreatPosition = target.position;
        hasThreat = true;
    }

    // Called by PlayerController every time the player fires
    void HearGunshot(Vector3 shotPosition)
    {
        if (isDead || !initialized) return;
        if (targetPlayer != null && targetPlayer.IsDead) return;

        // Walls muffle sound: through a wall it only hears shots within a shorter range
        float range = hearingRadius;
        if (!pathfinding.HasLineOfSight(transform.position, shotPosition))
            range *= wallMuffling;
        if (FlatDistance(transform.position, shotPosition) > range) return;

        // The shot gives away roughly where the player is (and where the danger is, for Retreat's cover checks)
        lastThreatPosition = shotPosition;
        hasThreat = true;
        if (!seesPlayer)
        {
            lastKnownPlayerPosition = shotPosition;
            lastKnownPlayerVelocity = Vector3.zero; // Sound gives a place, not a direction
            hasPlayerMemory = true;
        }

        switch (currentState)
        {
            case BotState.Patrol:
            case BotState.Investigate:
                // Heard a shot but can't see the shooter: go and check where it came from
                if (!seesPlayer) StartInvestigating(shotPosition);
                break;

            // Pursue / Attack: memory was updated above, so if it loses sight it heads for the shot.
            // Retreat: the threat position was updated above, so the next cover check hides from the shot.
        }
    }

    // Where the player probably is now: last seen position, pushed forward along the way they were moving
    Vector3 PredictedPlayerPosition()
    {
        Vector3 velocity = lastKnownPlayerVelocity;
        velocity.y = 0f;
        return lastKnownPlayerPosition + velocity * predictionTime;
    }

    // Can the player see this bot? (Used for healing only while hidden.)
    bool IsExposedToPlayer()
    {
        if (target == null) return false;
        if (targetPlayer != null && targetPlayer.IsDead) return false;
        return FlatDistance(transform.position, target.position) <= viewDistance
            && pathfinding.HasLineOfSight(target.position, transform.position);
    }

    bool IsHiddenFrom(Vector3 viewer, Vector3 spot)
    {
        return !pathfinding.HasLineOfSight(viewer, spot);
    }

    void PickNextPatrolPoint()
    {
        ClearDestination();
        patrolLegTimer = 0f;
        patrolWaitTimer = 0f;

        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            // Random patrol point, never the same one twice in a row
            int index = Random.Range(0, patrolPoints.Length);
            if (patrolPoints.Length > 1 && index == patrolIndex)
                index = (index + Random.Range(1, patrolPoints.Length)) % patrolPoints.Length;

            patrolIndex = index;
            if (patrolPoints[index] != null)
            {
                patrolDestination = patrolPoints[index].position;
                return;
            }
        }

        // No patrol points set up: wander to a random open spot near where it started
        patrolDestination = RandomWalkablePointNear(homePosition, wanderRadius);
    }

    Vector3 RandomWalkablePointNear(Vector3 centre, float radius)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Node node = grid.NodeFromWorldPoint(centre + new Vector3(offset.x, 0f, offset.y));
            if (node.isWalkable) return node.worldPosition;
        }
        return centre;
    }

    // Scans the grid around the bot for the best place to hide from the threat.
    //   Priority 1: a spot the threat CAN'T see (cover). Prefers close spots (less time exposed while running),
    //               then spots further from the threat.
    //   Priority 2: if there's no cover nearby, the open spot furthest from the threat.
    // Spots that mean running towards the threat are never chosen.
    Vector3 FindCoverPoint()
    {
        Vector3 threat = ThreatPosition;
        Vector3 myPosition = transform.position;

        Vector3 awayFromThreat = myPosition - threat;
        awayFromThreat.y = 0f;
        awayFromThreat = awayFromThreat.sqrMagnitude > 0.01f ? awayFromThreat.normalized : -transform.forward;

        bool foundCover = false;
        Vector3 bestCover = myPosition;
        float bestCoverScore = float.MinValue;
        Vector3 bestOpen = myPosition + awayFromThreat * coverSearchRadius; // Fallback: straight away from the threat
        float bestOpenDistance = float.MinValue;

        float step = grid.nodeRadius * 2f;
        HashSet<Node> checkedNodes = new HashSet<Node>();

        for (float x = -coverSearchRadius; x <= coverSearchRadius; x += step)
        {
            for (float z = -coverSearchRadius; z <= coverSearchRadius; z += step)
            {
                if (x * x + z * z > coverSearchRadius * coverSearchRadius) continue; // Keep the search circular

                Node node = grid.NodeFromWorldPoint(myPosition + new Vector3(x, 0f, z));
                if (!node.isWalkable || !checkedNodes.Add(node)) continue;

                Vector3 spot = new Vector3(node.worldPosition.x, myPosition.y, node.worldPosition.z);
                Vector3 toSpot = spot - myPosition;
                toSpot.y = 0f;
                float runDistance = toSpot.magnitude;

                // Never run towards the threat to reach a hiding spot
                if (runDistance > 0.1f && Vector3.Dot(toSpot / runDistance, awayFromThreat) < -0.2f) continue;

                float threatDistance = FlatDistance(spot, threat);
                if (threatDistance < minDistanceFromThreat) continue;

                if (IsHiddenFrom(threat, spot))
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

    bool IsFacing(Vector3 point, float maxAngle)
    {
        Vector3 toPoint = point - transform.position;
        toPoint.y = 0f;
        return toPoint.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, toPoint) <= maxAngle;
    }

    void ShootAtTarget()
    {
        if (bulletPrefab == null || firePoint == null || target == null) return;

        // Lead the shot: aim where the player will be when the bullet arrives
        Vector3 aimPoint = target.position;
        if (targetRb != null)
        {
            float travelTime = Vector3.Distance(firePoint.position, aimPoint) / bulletSpeed;
            aimPoint += targetRb.linearVelocity * travelTime;
        }

        Vector3 direction = aimPoint - firePoint.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;

        // Random aim error so it's beatable
        direction = Quaternion.Euler(0f, Random.Range(-aimSpread, aimSpread), 0f) * direction.normalized;

        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, Quaternion.LookRotation(direction));

        Bullet bulletScript = bullet.GetComponent<Bullet>();
        if (bulletScript != null)
        {
            bulletScript.firedByPlayer = false;
            bulletScript.damage = bulletDamage;
            bulletScript.damageProfile = damageProfile; // Option A: this bullet rolls its damage on hit
            bulletScript.shooterName = npcId;
        }

        Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
        if (bulletRb != null) bulletRb.linearVelocity = direction * bulletSpeed;

        Destroy(bullet, 3f);
    }

    // Call every frame the bot should be walking. Only re-runs A* when needed:
    // a new destination, the destination moved (and the refresh timer ran out), or the last search failed.
    void MoveTowards(Vector3 destination)
    {
        wantsToMove = true;

        bool newDestination = !hasDestination;
        bool destinationMoved = hasDestination && FlatDistance(destination, currentDestination) > RepathDistance;
        bool retryFailedPath = path == null && repathTimer <= 0f;

        if (newDestination || (destinationMoved && repathTimer <= 0f) || retryFailedPath)
        {
            currentDestination = destination;
            hasDestination = true;
            repathTimer = pathUpdateInterval;
            path = pathfinding.FindPath(transform.position, destination);
            waypointIndex = 0;
        }
    }

    void ClearDestination()
    {
        hasDestination = false;
        path = null;
        waypointIndex = 0;
        repathTimer = 0f;
    }

    bool HasArrived()
    {
        return hasDestination && path != null && waypointIndex >= path.Count;
    }

    bool PathFailed()
    {
        return hasDestination && path == null;
    }

    Vector3 GetPathDirection()
    {
        if (path == null) return Vector3.zero;

        // Skip waypoints already reached
        while (waypointIndex < path.Count &&
               FlatDistance(transform.position, path[waypointIndex]) <= waypointReachedDistance)
        {
            waypointIndex++;
        }

        if (waypointIndex >= path.Count) return Vector3.zero;

        Vector3 direction = path[waypointIndex] - transform.position;
        direction.y = 0f;
        return direction.normalized;
    }

    float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // Always: coloured ball above the head = current state, white line = current A* path.
    void OnDrawGizmos()
    {
        Gizmos.color = GetStateColor(currentState);
        Gizmos.DrawSphere(transform.position + Vector3.up * 2f, 0.25f);

        if (path != null)
        {
            Gizmos.color = Color.white;
            Vector3 previous = transform.position;
            for (int i = waypointIndex; i < path.Count; i++)
            {
                Gizmos.DrawLine(previous, path[i]);
                previous = path[i];
            }
        }
    }

    // When selected: view cone, attack range, memory and cover points.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * transform.forward;
        Gizmos.DrawLine(transform.position, transform.position + left * viewDistance);
        Gizmos.DrawLine(transform.position, transform.position + right * viewDistance);
        Gizmos.DrawWireSphere(transform.position, proximityRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Hearing: outer circle = in the open, inner circle = through a wall
        Gizmos.color = new Color(0.5f, 0.5f, 1f);
        Gizmos.DrawWireSphere(transform.position, hearingRadius);
        Gizmos.DrawWireSphere(transform.position, hearingRadius * wallMuffling);

        if (!Application.isPlaying) return;

        if (hasPlayerMemory)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(lastKnownPlayerPosition, 0.4f);
        }
        if (currentState == BotState.Investigate)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(investigatePoint, Vector3.one * 0.6f);
        }
        if (currentState == BotState.Retreat)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(coverPoint, Vector3.one * 0.8f);
        }
    }

    // Used by the Scene-view gizmo and by the in-game HUD label
    Color GetStateColor(BotState state)
    {
        switch (state)
        {
            case BotState.Patrol:      return Color.green;
            case BotState.Investigate: return Color.yellow;
            case BotState.Pursue:      return new Color(1f, 0.5f, 0f); // Orange
            case BotState.Attack:      return Color.red;
            default:                   return Color.cyan;              // Retreat
        }
    }
}
