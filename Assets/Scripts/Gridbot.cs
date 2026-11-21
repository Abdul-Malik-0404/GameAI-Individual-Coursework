using UnityEngine;

// A* grid-based marksman NPC coordinating perception, locomotion, cover, and a 5-state FSM.
[RequireComponent(typeof(Rigidbody))]
public class GridBot : MonoBehaviour, IDamageable, IHUDTarget
{
    public enum BotState { Patrol, Investigate, Pursue, Attack, Retreat }

    [Header("State")]
    public BotState currentState = BotState.Patrol;

    [Header("Identity")]
    [Tooltip("Leave empty to auto-number in scene order.")]
    public string npcId = "";

    [Header("References")]
    [Tooltip("Defaults to the object tagged 'Player' if empty.")]
    public Transform target;
    public GameObject bulletPrefab;
    public Transform firePoint;

    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    [Range(0f, 1f)] public float retreatHealthPercent = 0.2f;
    [Range(0f, 1f)] public float recoverHealthPercent = 0.6f;
    public float healthRegenPerSecond = 5f;

    [Header("Senses")]
    public float viewDistance = 15f;
    [Range(10f, 360f)] public float viewAngle = 110f;
    public float proximityRadius = 2.5f;
    public float loseSightDelay = 0.5f;
    public float predictionTime = 1f;
    public float hearingRadius = 15f;
    [Range(0f, 1f)] public float wallMuffling = 0.5f;

    [Header("Movement")]
    public float patrolSpeed = 2.5f;
    public float chaseSpeed = 4.5f;
    public float turnSpeed = 8f;
    public float pathUpdateInterval = 0.3f;
    public float waypointReachedDistance = 0.2f;

    [Header("Patrol")]
    public Transform[] patrolPoints;
    public float wanderRadius = 8f;
    public float minPatrolWait = 1f;
    public float maxPatrolWait = 3f;
    public float patrolLegTimeout = 15f;

    [Header("Investigate")]
    public float lookAroundDuration = 3f;
    public float lookAroundSpeed = 120f;
    [Range(0f, 1f)] public float searchAgainChance = 0.5f;
    public int maxExtraSearches = 2;
    public float searchRadius = 4f;
    public float investigateTimeout = 25f;

    [Header("Attack")]
    public float attackRange = 8f;
    public float attackRangeBuffer = 1.5f;
    public float fireCooldown = 0.8f;
    public float bulletSpeed = 15f;
    public int bulletDamage = 10;
    public float aimSpread = 4f;

    [Header("Damage Model (Option A)")]
    [Tooltip("Marksman profile: few grazes, high crits (expected 1.1x base damage).")]
    public DamageProfile damageProfile = new DamageProfile(0.25f, 0.5f, 0.15f, 2.5f);

    [Header("Retreat")]
    public float coverSearchRadius = 10f;
    public float minDistanceFromThreat = 5f;
    public float coverCheckInterval = 0.5f;

    [Header("Debug")]
    public bool logStateChanges = true;

    [HideInInspector] public GridBotSquad squad;

    private readonly StateMachine<BotState> fsm = new StateMachine<BotState>();
    private GridBotInvestigateState investigateState;
    private GridBotRetreatState retreatState;

    private bool initialized;
    private bool isDead;
    private float fireTimer;

    public GridManager Grid { get; private set; }
    public GridMotor Motor { get; private set; }
    public NpcPerception Perception { get; private set; }
    public Vector3 HomePosition { get; private set; }
    public float StateTime { get { return fsm.StateTime; } }
    public float HealthPercent { get { return currentHealth / maxHealth; } }
    public bool CanFire { get { return fireTimer <= 0f; } }

    public string Id { get { return npcId; } }
    public Color StateColor { get { return GetStateColor(currentState); } }
    public float CurrentHealth { get { return currentHealth; } }
    public float MaxHealth { get { return maxHealth; } }

    public string StateName
    {
        get
        {
            if (squad != null && currentState == BotState.Patrol)
                return squad.Leader == this ? "Patrol (Leader)" : "Formation";
            return currentState.ToString();
        }
    }

    void Awake()
    {
        npcId = SpatialUtility.ResolveNpcId(this, npcId, "GridBot");
    }

    void OnEnable()
    {
        PlayerController.OnShotFired += HearGunshot;
    }

    void OnDisable()
    {
        PlayerController.OnShotFired -= HearGunshot;
    }

    void Start()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        currentHealth = maxHealth;
        HomePosition = transform.position;

        Pathfinding pathfinding = Pathfinding.Instance;
        if (pathfinding == null)
        {
            Debug.LogError(npcId + ": no Pathfinding in the scene. Add GridManager + Pathfinding.");
            enabled = false;
            return;
        }

        Grid = pathfinding.GetComponent<GridManager>();
        Motor = new GridMotor(transform, rb, pathfinding);
        Perception = new NpcPerception(transform);
        Perception.Initialize(target, Grid.unwalkableMask);
        SyncPerceptionSettings();

        investigateState = new GridBotInvestigateState(this);
        retreatState = new GridBotRetreatState(this);
        fsm.Register(new GridBotPatrolState(this));
        fsm.Register(investigateState);
        fsm.Register(new GridBotPursueState(this));
        fsm.Register(new GridBotAttackState(this));
        fsm.Register(retreatState);

        if (bulletPrefab == null || firePoint == null)
            Debug.LogWarning(npcId + ": Bullet Prefab or Fire Point is empty, so this bot can't shoot.");
    }

    void Update()
    {
        if (isDead || Grid == null || !Grid.IsReady) return;

        if (!initialized)
        {
            initialized = true;
            ChangeState(currentState);
        }

        fireTimer -= Time.deltaTime;
        Motor.BeginFrame(Time.deltaTime);
        SyncPerceptionSettings();

        bool alert = currentState == BotState.Pursue || currentState == BotState.Attack || currentState == BotState.Retreat;
        Perception.UpdateVision(!alert);

        // Low-health retreat takes priority over all other states.
        if (currentState != BotState.Retreat && HealthPercent <= retreatHealthPercent)
            ChangeState(BotState.Retreat);

        fsm.Tick(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (isDead || !initialized) return;
        Motor.ApplyPhysics(Time.fixedDeltaTime, turnSpeed, waypointReachedDistance);
    }

    public void ChangeState(BotState nextState)
    {
        currentState = nextState;
        fsm.ChangeState(nextState, logStateChanges, npcId);
    }

    public void StartInvestigating(Vector3 point)
    {
        investigateState.TargetPoint = point;
        ChangeState(BotState.Investigate);
    }

    public void ResetFireTimer(float delay)
    {
        fireTimer = delay;
    }

    public void ShootAtTarget()
    {
        NpcShooter.FireLeadingShot(
            bulletPrefab, firePoint, Perception.Target, Perception.TargetRigidbody,
            bulletSpeed, bulletDamage, aimSpread, damageProfile, npcId);
        fireTimer = fireCooldown;
    }

    public void RegenerateHealth(float dt)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + healthRegenPerSecond * dt);
    }

    public void TakeDamage(int damage, Vector3 sourcePosition)
    {
        if (isDead) return;

        currentHealth -= damage;
        Debug.Log(npcId + " HP: " + currentHealth);

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        Perception.RecordThreat(sourcePosition, false);
        if (!initialized) return;

        if (squad != null) squad.ReportPlayer(this, sourcePosition);

        if ((currentState == BotState.Patrol || currentState == BotState.Investigate) && !Perception.SeesPlayer)
            StartInvestigating(sourcePosition);
        else if (currentState == BotState.Retreat)
            retreatState.MarkCoverCompromised();
    }

    public void HearSquadAlert(Vector3 position)
    {
        if (isDead || !initialized || Perception.SeesPlayer) return;
        if (currentState != BotState.Patrol && currentState != BotState.Investigate) return;

        Perception.RecordThreat(position, false);
        StartInvestigating(position);
    }

    private void HearGunshot(Vector3 shotPosition)
    {
        if (isDead || !initialized || !Perception.TryHearGunshot(shotPosition)) return;

        if ((currentState == BotState.Patrol || currentState == BotState.Investigate) && !Perception.SeesPlayer)
            StartInvestigating(shotPosition);
    }

    private void SyncPerceptionSettings()
    {
        Perception.ViewDistance = viewDistance;
        Perception.ViewAngle = viewAngle;
        Perception.ProximityRadius = proximityRadius;
        Perception.LoseSightDelay = loseSightDelay;
        Perception.PredictionTime = predictionTime;
        Perception.HearingRadius = hearingRadius;
        Perception.WallMuffling = wallMuffling;
    }

    private void Die()
    {
        isDead = true;
        Debug.Log(npcId + " defeated!");
        Destroy(gameObject);
    }

    private static Color GetStateColor(BotState state)
    {
        switch (state)
        {
            case BotState.Patrol:      return Color.green;
            case BotState.Investigate: return Color.yellow;
            case BotState.Pursue:      return new Color(1f, 0.5f, 0f);
            case BotState.Attack:      return Color.red;
            default:                   return Color.cyan;
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = GetStateColor(currentState);
        Gizmos.DrawSphere(transform.position + Vector3.up * 2f, 0.25f);
        if (Motor != null) Motor.DrawPathGizmos();
    }

    void OnDrawGizmosSelected()
    {
        SpatialUtility.DrawConeGizmo(transform.position, transform.forward, viewAngle, viewDistance, Color.yellow);
        Gizmos.DrawWireSphere(transform.position, proximityRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = new Color(0.5f, 0.5f, 1f);
        Gizmos.DrawWireSphere(transform.position, hearingRadius);
        Gizmos.DrawWireSphere(transform.position, hearingRadius * wallMuffling);

        if (!Application.isPlaying || Perception == null) return;

        if (Perception.HasMemory)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(Perception.LastKnownPosition, 0.4f);
        }
        if (currentState == BotState.Investigate && investigateState != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(investigateState.TargetPoint, Vector3.one * 0.6f);
        }
        if (currentState == BotState.Retreat && retreatState != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(retreatState.CoverPoint, Vector3.one * 0.8f);
        }
    }
}
