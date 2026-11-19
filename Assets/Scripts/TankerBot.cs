using UnityEngine;
using UnityEngine.AI;

// NPC BRAIN #2 - "TANKER"
// A slow, heavily armoured NPC that guards a post. Moves with Unity's NavMesh
// (the NavMeshAgent plans the path, the Rigidbody does the moving - the same way GridBot and the player move).
// Put this on the NavMesh NPC.
//
// How it differs from GridBot (A*):
//   GridBot : patrols, sees with a view cone, fires single aimed shots, FLEES and heals at low health.
//   Tanker  : guards one post, HEARS gunshots through walls, has an ARMOURED FRONT, fires shotgun
//             volleys, CHARGES and rams, is STUNNED after a charge (its weak moment), and becomes
//             ENRAGED at low health instead of fleeing.
//
// States and what makes it change state:
//   GUARD   -> Chase   : sees the player
//           -> Search  : hears a gunshot nearby, or is shot without seeing the shooter
//   SEARCH  -> Chase   : sees the player
//           -> Guard   : reached the spot, looked around, found nothing (walks back to its post)
//   CHASE   -> Bombard : player within bombard range and visible
//           -> Charge  : charge ready + player in charge range + clear straight line + dice roll
//                        (guaranteed if the player keeps it out of range for a few seconds = kiting)
//           -> Search  : lost sight of the player (goes to their last known position)
//   BOMBARD -> Chase   : player moved out of range
//           -> Charge  : same rule as in Chase
//           -> Search  : lost sight of the player
//   CHARGE  -> Recover : rammed the player, crashed into a wall, or ran out of charge time
//   RECOVER -> Chase / Bombard / Search : once the stun wears off (longer after crashing into a wall)
// At 35% health it becomes ENRAGED for the rest of its life: faster, fires faster, charges more often.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody))]
public class TankerBot : MonoBehaviour, IDamageable, IHUDTarget
{
    // ============================ FSM ============================
    public enum TankerState { Guard, Search, Chase, Bombard, Charge, Recover }

    [Header("State (watch this change in Play mode)")]
    public TankerState currentState = TankerState.Guard;

    [Header("Identity")]
    [Tooltip("Name shown above the NPC in game. Leave empty to number automatically (Tanker 1, Tanker 2, ...).")]
    public string npcId = "";

    [Header("References")]
    [Tooltip("Who to fight. If empty, the object tagged 'Player' is used.")]
    public Transform target;
    [Tooltip("Can be the same bullet prefab the player uses.")]
    public GameObject bulletPrefab;
    [Tooltip("Empty child object at the front of the NPC where bullets spawn.")]
    public Transform firePoint;
    [Tooltip("Layer(s) that block vision: set this to your wall layer (e.g. Unwalkable). If left as Nothing, the A* grid's Unwalkable Mask is used.")]
    public LayerMask obstacleMask;
    [Tooltip("Where it stands guard (an empty GameObject, NOT a child of the Tanker). If empty, its start position is used.")]
    public Transform guardPost;

    [Header("Health & Armour")]
    public float maxHealth = 150f;
    public float currentHealth = 150f;
    [Tooltip("Damage multiplier for shots that hit its armoured front (0.5 = half damage).")]
    [Range(0f, 1f)] public float frontArmourMultiplier = 0.5f;
    [Tooltip("Width of the armoured front, in degrees.")]
    [Range(0f, 360f)] public float frontArmourAngle = 120f;
    [Tooltip("Damage multiplier while stunned after a charge (its weak moment).")]
    public float stunnedDamageMultiplier = 1.5f;
    [Tooltip("Becomes enraged at this fraction of max health (0.35 = 35%).")]
    [Range(0f, 1f)] public float enrageHealthPercent = 0.35f;

    [Header("Senses")]
    public float viewDistance = 14f;
    [Tooltip("Width of the view cone in degrees (used while Guarding / Searching).")]
    [Range(10f, 360f)] public float viewAngle = 140f;
    [Tooltip("Hears the player's gunshots within this distance, even through walls.")]
    public float hearingRadius = 20f;
    [Tooltip("Seconds the player can be out of sight before the Tanker counts them as lost.")]
    public float loseSightDelay = 1f;

    [Header("Movement")]
    public float guardSpeed = 2f;       // Guard and Search
    public float chaseSpeed = 3f;       // Chase
    [Tooltip("Heavy: turns slowly, which gives the player a chance to get behind its armour.")]
    public float turnSpeed = 4f;
    public float pathUpdateInterval = 0.3f;

    [Header("Guard")]
    [Tooltip("How far it sweeps left and right while guarding, in degrees.")]
    public float scanAngle = 60f;
    [Tooltip("How fast it sweeps, in degrees per second.")]
    public float scanSpeed = 30f;

    [Header("Search")]
    public float searchLookDuration = 3f;
    public float searchTimeout = 20f;

    [Header("Bombard (shotgun volleys)")]
    public float bombardRange = 10f;
    [Tooltip("Extra distance before leaving Bombard, so it doesn't flicker between Bombard and Chase.")]
    public float bombardRangeBuffer = 2f;
    public int pelletsPerVolley = 3;
    [Tooltip("Degrees between neighbouring pellets.")]
    public float spreadAngle = 12f;
    public int volleysPerBurst = 3;
    public float timeBetweenVolleys = 0.5f;
    public float reloadTime = 1.8f;
    public float bulletSpeed = 12f;
    public int bulletDamage = 8;

    [Header("Damage Model (Option A)")]
    [Tooltip("Tanker fires scattered pellets: many grazes, rare crits. Average = 0.9 x base damage. Also used for the ram.")]
    public DamageProfile damageProfile = new DamageProfile(0.4f, 0.5f, 0.1f, 2f);

    [Header("Charge")]
    public float chargeMinRange = 4f;
    public float chargeMaxRange = 14f;
    public float chargeCooldown = 6f;
    [Tooltip("Chance (0-1) to charge each time it considers it (once a second).")]
    [Range(0f, 1f)] public float chargeChance = 0.35f;
    [Tooltip("If the player keeps it out of bombard range this long, it charges for sure.")]
    public float kiteTimeBeforeCharge = 3f;
    [Tooltip("Seconds it stands still aiming before the rush - the player's warning to dodge.")]
    public float windUpTime = 0.8f;
    public float chargeSpeed = 12f;
    public float chargeMaxTime = 1.5f;
    public int ramDamage = 25;
    public float ramKnockback = 10f;
    [Tooltip("Stun after a charge that hit the player or missed.")]
    public float recoverTime = 1.2f;
    [Tooltip("Stun after crashing into a wall.")]
    public float crashRecoverTime = 2.5f;

    [Header("Enraged (low health)")]
    public float enragedSpeedMultiplier = 1.3f;
    public float enragedFireRateMultiplier = 1.6f;
    public float enragedChargeCooldownMultiplier = 0.5f;
    [Range(0f, 1f)] public float enragedChargeChance = 0.6f;

    [Header("Debug")]
    public bool logStateChanges = true;

    // ======================= Private data =======================
    private NavMeshAgent agent;
    private Rigidbody rb;
    private PlayerController targetPlayer;
    private bool isDead;
    private bool isEnraged;

    // What the state logic wants this frame (cleared at the start of every Update)
    private bool wantsToMove;
    private bool isRushing;
    private bool hasFaceTarget;
    private Vector3 faceTarget;

    // Navigation
    private Vector3 currentDestination;
    private bool hasDestination;
    private float repathTimer;

    // Senses and memory
    private bool seesPlayer;
    private Vector3 lastKnownPlayerPosition;
    private float lastSeenTime = -999f;

    // Per-state data
    private float stateTimer;
    private Vector3 guardPosition;
    private Quaternion guardRotation;
    private Vector3 searchPoint;
    private bool searchLooking;
    private float searchLookTimer;
    private int volleysFired;
    private float volleyTimer;
    private float chargeReadyTime;
    private float chargeDecisionTimer;
    private float outOfRangeTime;
    private bool chargeWindingUp;
    private Vector3 chargeDirection;
    private float rushTime;
    private float currentRecoverTime;

    private float HealthPercent
    {
        get { return currentHealth / maxHealth; }
    }

    // ===================== HUD (IHUDTarget) =====================
    public string Id { get { return npcId; } }
    public string StateName { get { return isEnraged ? currentState + " (Enraged)" : currentState.ToString(); } }
    public Color StateColor { get { return GetStateColor(currentState); } }
    public float CurrentHealth { get { return currentHealth; } }
    public float MaxHealth { get { return maxHealth; } }

    // ========================= Unity =========================
    void Awake()
    {
        // No ID typed in: number the Tankers in scene order (Tanker 1, Tanker 2, ...).
        // Works the same after the level restarts, unlike a counter that keeps counting.
        if (string.IsNullOrEmpty(npcId))
        {
            TankerBot[] all = FindObjectsByType<TankerBot>(FindObjectsSortMode.InstanceID);
            npcId = "Tanker " + (System.Array.IndexOf(all, this) + 1);
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
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();

        // The agent only PLANS the path. The Rigidbody does the actual moving.
        agent.updatePosition = false;
        agent.updateRotation = false;
        rb.isKinematic = false;
        rb.freezeRotation = true;

        currentHealth = maxHealth;

        // No Obstacle Mask set: use the SAME wall layer as the A* grid, so both NPC types agree on what a wall is
        if (obstacleMask.value == 0 && Pathfinding.Instance != null)
        {
            GridManager grid = Pathfinding.Instance.GetComponent<GridManager>();
            if (grid != null) obstacleMask = grid.unwalkableMask;
        }
        if (obstacleMask.value == 0)
            obstacleMask = LayerMask.GetMask("Unwalkable", "Wall");
        if (obstacleMask.value == 0)
            Debug.LogWarning(npcId + ": no Obstacle Mask set and no wall layer found, so it can see through walls.");

        guardPosition = guardPost != null ? guardPost.position : transform.position;
        guardRotation = guardPost != null ? guardPost.rotation : transform.rotation;

        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }
        if (target != null) targetPlayer = target.GetComponent<PlayerController>();

        if (bulletPrefab == null || firePoint == null)
            Debug.LogWarning(npcId + ": Bullet Prefab or Fire Point is empty, so it can't shoot.");

        ChangeState(currentState);
    }

    void Update()
    {
        if (isDead || agent == null) return;

        // Physics can push the body around, so keep the agent's idea of where it is glued to the real body
        agent.nextPosition = transform.position;

        stateTimer += Time.deltaTime;
        repathTimer -= Time.deltaTime;
        volleyTimer -= Time.deltaTime;
        chargeDecisionTimer -= Time.deltaTime;

        wantsToMove = false;
        isRushing = false;
        hasFaceTarget = false;

        // ---- Senses ----
        // Calm (Guard/Search): only sees inside its view cone. In a fight: tracks the player all around.
        bool alert = currentState != TankerState.Guard && currentState != TankerState.Search;
        seesPlayer = CanSeePlayer(!alert);
        if (seesPlayer)
        {
            lastKnownPlayerPosition = target.position;
            lastSeenTime = Time.time;
        }

        // ---- Low health: enrage (a one-way phase change, instead of fleeing like GridBot) ----
        if (!isEnraged && HealthPercent <= enrageHealthPercent)
            Enrage();

        agent.speed = CurrentMoveSpeed();

        // ---- Run the current state ----
        switch (currentState)
        {
            case TankerState.Guard:   UpdateGuard();   break;
            case TankerState.Search:  UpdateSearch();  break;
            case TankerState.Chase:   UpdateChase();   break;
            case TankerState.Bombard: UpdateBombard(); break;
            case TankerState.Charge:  UpdateCharge();  break;
            case TankerState.Recover: UpdateRecover(); break;
        }
    }

    void FixedUpdate()
    {
        if (isDead || agent == null) return;

        // ---- Charging: rush in a straight line, ignoring the NavMesh ----
        if (isRushing && currentState == TankerState.Charge)
        {
            rushTime += Time.fixedDeltaTime;

            // Physics slowed it right down since last step: it slammed into something solid
            Vector3 actualVelocity = rb.linearVelocity;
            actualVelocity.y = 0f;
            if (rushTime > 0.2f && actualVelocity.magnitude < chargeSpeed * 0.3f)
            {
                if (logStateChanges) Debug.Log(npcId + " crashed into a wall!");
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                StartRecover(crashRecoverTime);
                return;
            }

            // Ran for the full time without hitting anything: the player dodged it
            if (rushTime >= chargeMaxTime)
            {
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                StartRecover(recoverTime);
                return;
            }

            rb.linearVelocity = new Vector3(chargeDirection.x * chargeSpeed, rb.linearVelocity.y, chargeDirection.z * chargeSpeed);
            return; // No turning mid-charge
        }

        // ---- Normal movement: follow the NavMesh path ----
        Vector3 move = Vector3.zero;
        if (wantsToMove && agent.hasPath && !HasArrived())
        {
            move = agent.desiredVelocity; // Direction along the path x agent speed
            move.y = 0f;
        }

        // Overwrite velocity every physics step (keep Y for gravity)
        rb.linearVelocity = new Vector3(move.x, rb.linearVelocity.y, move.z);

        // Turn towards the face target, or the direction of travel
        Vector3 facing = hasFaceTarget ? faceTarget - transform.position : move;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facing);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }
    }

    // Ramming the player during a charge
    void OnCollisionEnter(Collision collision)
    {
        if (isDead || currentState != TankerState.Charge || chargeWindingUp) return;

        PlayerController player = collision.collider.GetComponentInParent<PlayerController>();
        if (player == null) return;

        // Option A: the ram's damage is rolled too
        string hitType;
        int rolledDamage = damageProfile.Roll(ramDamage, npcId + " ram", out hitType);
        player.TakeDamage(rolledDamage, hitType == "HIT" ? "RAM" : "RAM " + hitType);
        player.ApplyKnockback(chargeDirection * ramKnockback, 0.35f); // Flat push: no lift off the floor

        if (logStateChanges) Debug.Log(npcId + " rammed the player!");
        StartRecover(recoverTime);
    }

    // ===================== State changes =====================
    void ChangeState(TankerState newState)
    {
        if (logStateChanges && newState != currentState)
            Debug.Log(npcId + ": " + currentState + " -> " + newState);

        currentState = newState;
        stateTimer = 0f;
        ClearDestination();

        // One-off setup when entering a state
        switch (newState)
        {
            case TankerState.Guard:
                outOfRangeTime = 0f;
                break;

            case TankerState.Search:
                searchLooking = false;
                break;

            case TankerState.Chase:
                outOfRangeTime = 0f;
                break;

            case TankerState.Bombard:
                volleysFired = 0;
                volleyTimer = Random.Range(0.3f, 0.8f); // Short random delay before the first volley
                break;

            case TankerState.Charge:
                chargeWindingUp = true;
                rushTime = 0f;
                float cooldown = chargeCooldown * (isEnraged ? enragedChargeCooldownMultiplier : 1f);
                chargeReadyTime = Time.time + windUpTime + cooldown;
                break;

            case TankerState.Recover:
                break;
        }
    }

    void StartSearch(Vector3 point)
    {
        searchPoint = point;
        ChangeState(TankerState.Search);
    }

    void StartRecover(float duration)
    {
        currentRecoverTime = duration;
        ChangeState(TankerState.Recover);
    }

    void Enrage()
    {
        isEnraged = true;
        chargeReadyTime = Time.time; // Can charge straight away
        Debug.Log(npcId + " is ENRAGED!");
    }

    // ======================== States ========================

    // 1. GUARD: hold the guard post and sweep left and right
    void UpdateGuard()
    {
        if (seesPlayer)
        {
            ChangeState(TankerState.Chase);
            return;
        }

        // Not at the post (e.g. back from a search): walk back to it
        if (FlatDistance(transform.position, guardPosition) > agent.stoppingDistance + 0.5f)
        {
            MoveTo(guardPosition);
            return;
        }

        // At the post: sweep its gaze from side to side around the post's facing
        float phase = Time.time * scanSpeed / Mathf.Max(1f, scanAngle);
        float angle = scanAngle * Mathf.Sin(phase);
        Vector3 lookDirection = guardRotation * Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        hasFaceTarget = true;
        faceTarget = transform.position + lookDirection * 5f;
    }

    // 2. SEARCH: go to where the noise / shot / last sighting was, look around, then go back to guarding
    void UpdateSearch()
    {
        if (seesPlayer)
        {
            ChangeState(TankerState.Chase);
            return;
        }

        if (stateTimer > searchTimeout)
        {
            ChangeState(TankerState.Guard);
            return;
        }

        if (!searchLooking)
        {
            MoveTo(searchPoint);
            if (HasArrived() || PathFailed())
            {
                searchLooking = true;
                searchLookTimer = searchLookDuration;
            }
            return;
        }

        // Turn slowly on the spot to look around
        hasFaceTarget = true;
        faceTarget = transform.position + Quaternion.Euler(0f, Time.time * 90f, 0f) * Vector3.forward * 5f;

        searchLookTimer -= Time.deltaTime;
        if (searchLookTimer <= 0f)
            ChangeState(TankerState.Guard); // Found nothing: back to the post
    }

    // 3. CHASE: close the distance with the NavMesh
    void UpdateChase()
    {
        if (!seesPlayer && Time.time - lastSeenTime > loseSightDelay)
        {
            StartSearch(lastKnownPlayerPosition);
            return;
        }

        float distance = seesPlayer ? FlatDistance(transform.position, target.position) : float.MaxValue;

        if (seesPlayer && distance <= bombardRange)
        {
            ChangeState(TankerState.Bombard);
            return;
        }

        // The player is keeping it out of range: remember for how long (used to force a charge)
        if (seesPlayer) outOfRangeTime += Time.deltaTime;

        if (TryStartCharge(distance)) return;

        MoveTo(seesPlayer ? target.position : lastKnownPlayerPosition);
    }

    // 4. BOMBARD: stand and fire shotgun volleys in bursts
    void UpdateBombard()
    {
        if (!seesPlayer)
        {
            if (Time.time - lastSeenTime > loseSightDelay)
            {
                StartSearch(lastKnownPlayerPosition);
            }
            else
            {
                hasFaceTarget = true;
                faceTarget = lastKnownPlayerPosition;
            }
            return;
        }

        float distance = FlatDistance(transform.position, target.position);
        if (distance > bombardRange + bombardRangeBuffer)
        {
            ChangeState(TankerState.Chase);
            return;
        }

        hasFaceTarget = true;
        faceTarget = target.position;

        if (TryStartCharge(distance)) return;

        // A burst of volleys, then a reload pause (the player's window to move in)
        if (volleyTimer <= 0f && IsFacing(target.position, 20f))
        {
            FireVolley();
            volleysFired++;

            float fireRate = isEnraged ? enragedFireRateMultiplier : 1f;
            if (volleysFired >= volleysPerBurst)
            {
                volleysFired = 0;
                volleyTimer = reloadTime / fireRate;
            }
            else
            {
                volleyTimer = timeBetweenVolleys / fireRate;
            }
        }
    }

    // 5. CHARGE: wind up (stand and aim), then rush in a straight line (handled in FixedUpdate)
    void UpdateCharge()
    {
        if (chargeWindingUp)
        {
            Vector3 aimPoint = seesPlayer ? target.position : lastKnownPlayerPosition;
            hasFaceTarget = true;
            faceTarget = aimPoint;

            if (stateTimer >= windUpTime)
            {
                // Lock the direction now: the rush can't steer, so the player can sidestep it
                chargeDirection = aimPoint - transform.position;
                chargeDirection.y = 0f;
                chargeDirection = chargeDirection.sqrMagnitude > 0.01f ? chargeDirection.normalized : transform.forward;
                chargeWindingUp = false;
                rushTime = 0f;
            }
            return;
        }

        isRushing = true;
    }

    // 6. RECOVER: stunned after a charge. Stands still and takes extra damage.
    void UpdateRecover()
    {
        if (stateTimer < currentRecoverTime) return;

        if (seesPlayer)
        {
            bool inRange = FlatDistance(transform.position, target.position) <= bombardRange;
            ChangeState(inRange ? TankerState.Bombard : TankerState.Chase);
        }
        else
        {
            StartSearch(lastKnownPlayerPosition);
        }
    }

    // Decides whether to charge. Checked at most once a second.
    bool TryStartCharge(float distance)
    {
        if (!seesPlayer || Time.time < chargeReadyTime) return false;
        if (distance < chargeMinRange || distance > chargeMaxRange) return false;
        if (chargeDecisionTimer > 0f) return false;
        chargeDecisionTimer = 1f;

        // Being kited (kept out of range) for a while guarantees a charge; otherwise it's a dice roll
        bool beingKited = outOfRangeTime >= kiteTimeBeforeCharge;
        float chance = isEnraged ? enragedChargeChance : chargeChance;
        if (!beingKited && Random.value > chance) return false;

        // Only charge if there's a straight, clear run to the player across the NavMesh
        NavMeshHit hit;
        if (NavMesh.Raycast(agent.nextPosition, target.position, out hit, NavMesh.AllAreas)) return false;

        ChangeState(TankerState.Charge);
        return true;
    }

    // ======================== Damage ========================
    public void TakeDamage(int damage, Vector3 sourcePosition)
    {
        if (isDead) return;

        // Where it was hit matters: front armour halves damage, being stunned increases it
        float multiplier = 1f;
        string hitType = "side/back";
        if (currentState == TankerState.Recover)
        {
            multiplier = stunnedDamageMultiplier;
            hitType = "stunned";
        }
        else if (IsInFrontArc(sourcePosition))
        {
            multiplier = frontArmourMultiplier;
            hitType = "armour";
        }

        float finalDamage = damage * multiplier;
        currentHealth -= finalDamage;
        Debug.Log(npcId + " hit (" + hitType + "): -" + finalDamage + "  HP: " + currentHealth);

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        // Shot by someone it can't see: the shot's origin is its best clue
        if (!seesPlayer)
        {
            lastKnownPlayerPosition = sourcePosition;

            if (agent != null && (currentState == TankerState.Guard || currentState == TankerState.Search))
                StartSearch(sourcePosition);
        }
    }

    void Die()
    {
        isDead = true;
        Debug.Log(npcId + " defeated!");
        Destroy(gameObject);
    }

    // ======================= Senses =======================
    bool CanSeePlayer(bool useViewCone)
    {
        if (target == null) return false;
        if (targetPlayer != null && targetPlayer.IsDead) return false; // Dead: the level is about to restart

        Vector3 toPlayer = target.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance > viewDistance) return false;

        // Outside the view cone it only notices the player if they are right next to it
        if (useViewCone && distance > 3f && Vector3.Angle(transform.forward, toPlayer) > viewAngle * 0.5f)
            return false;

        // Walls block vision
        return !Physics.Linecast(transform.position, target.position, obstacleMask);
    }

    // Called by PlayerController every time the player fires
    void HearGunshot(Vector3 shotPosition)
    {
        if (isDead || agent == null) return;
        if (targetPlayer != null && targetPlayer.IsDead) return;
        if (FlatDistance(transform.position, shotPosition) > hearingRadius) return;

        // Sound gives away roughly where the player is, even through walls
        if (!seesPlayer)
        {
            lastKnownPlayerPosition = shotPosition;
        }

        if (currentState == TankerState.Guard || currentState == TankerState.Search)
            StartSearch(shotPosition);
    }

    bool IsInFrontArc(Vector3 point)
    {
        Vector3 toPoint = point - transform.position;
        toPoint.y = 0f;
        if (toPoint.sqrMagnitude < 0.01f) return true;
        return Vector3.Angle(transform.forward, toPoint) <= frontArmourAngle * 0.5f;
    }

    bool IsFacing(Vector3 point, float maxAngle)
    {
        Vector3 toPoint = point - transform.position;
        toPoint.y = 0f;
        return toPoint.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, toPoint) <= maxAngle;
    }

    // ======================= Attacks =======================
    // Fires several pellets fanned out around the aim direction (e.g. -12, 0, +12 degrees)
    void FireVolley()
    {
        if (bulletPrefab == null || firePoint == null || target == null) return;

        Vector3 aim = target.position - firePoint.position;
        aim.y = 0f;
        if (aim.sqrMagnitude < 0.001f) return;
        aim.Normalize();

        float firstAngle = -spreadAngle * (pelletsPerVolley - 1) * 0.5f;
        for (int i = 0; i < pelletsPerVolley; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, firstAngle + spreadAngle * i, 0f) * aim;
            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, Quaternion.LookRotation(direction));

            Bullet bulletScript = bullet.GetComponent<Bullet>();
            if (bulletScript != null)
            {
                bulletScript.firedByPlayer = false;
                bulletScript.damage = bulletDamage;
                bulletScript.damageProfile = damageProfile; // Option A: each pellet rolls its damage on hit
                bulletScript.shooterName = npcId;
            }

            Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
            if (bulletRb != null) bulletRb.linearVelocity = direction * bulletSpeed;

            Destroy(bullet, 3f);
        }
    }

    // ===================== NavMesh movement =====================
    // Call every frame the Tanker should walk. Only asks the NavMesh for a new path when needed.
    void MoveTo(Vector3 destination)
    {
        wantsToMove = true;

        bool newDestination = !hasDestination;
        bool destinationMoved = hasDestination && FlatDistance(destination, currentDestination) > 0.5f;

        if (newDestination || (destinationMoved && repathTimer <= 0f))
        {
            currentDestination = destination;
            hasDestination = true;
            repathTimer = pathUpdateInterval;
            agent.SetDestination(destination);
        }
    }

    void ClearDestination()
    {
        hasDestination = false;
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
    }

    bool HasArrived()
    {
        return hasDestination && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;
    }

    bool PathFailed()
    {
        return hasDestination && !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid;
    }

    float CurrentMoveSpeed()
    {
        bool calm = currentState == TankerState.Guard || currentState == TankerState.Search;
        float speed = calm ? guardSpeed : chaseSpeed;
        return isEnraged ? speed * enragedSpeedMultiplier : speed;
    }

    float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ========================= Colours & Gizmos =========================
    Color GetStateColor(TankerState state)
    {
        switch (state)
        {
            case TankerState.Guard:   return Color.green;
            case TankerState.Search:  return Color.yellow;
            case TankerState.Chase:   return new Color(1f, 0.5f, 0f);   // Orange
            case TankerState.Bombard: return Color.red;
            case TankerState.Charge:  return Color.magenta;
            default:                  return new Color(0.5f, 0.8f, 1f); // Recover: light blue
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = GetStateColor(currentState);
        Gizmos.DrawSphere(transform.position + Vector3.up * 2.4f, 0.3f);
    }

    void OnDrawGizmosSelected()
    {
        // Hearing radius
        Gizmos.color = new Color(0.5f, 0.5f, 1f);
        Gizmos.DrawWireSphere(transform.position, hearingRadius);

        // View cone
        Gizmos.color = Color.yellow;
        Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * transform.forward;
        Gizmos.DrawLine(transform.position, transform.position + left * viewDistance);
        Gizmos.DrawLine(transform.position, transform.position + right * viewDistance);

        // Armoured front
        Gizmos.color = Color.gray;
        Vector3 armourLeft = Quaternion.Euler(0f, -frontArmourAngle * 0.5f, 0f) * transform.forward;
        Vector3 armourRight = Quaternion.Euler(0f, frontArmourAngle * 0.5f, 0f) * transform.forward;
        Gizmos.DrawLine(transform.position, transform.position + armourLeft * 2f);
        Gizmos.DrawLine(transform.position, transform.position + armourRight * 2f);

        // Bombard range (red) and max charge range (magenta)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, bombardRange);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, chargeMaxRange);

        if (!Application.isPlaying) return;

        // Guard post
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(guardPosition, Vector3.one * 0.8f);

        // Charge line while winding up or rushing
        if (currentState == TankerState.Charge)
        {
            Gizmos.color = Color.magenta;
            Vector3 direction = chargeWindingUp ? (faceTarget - transform.position) : chargeDirection;
            direction.y = 0f;
            Gizmos.DrawLine(transform.position, transform.position + direction.normalized * chargeMaxRange);
        }
    }
}
