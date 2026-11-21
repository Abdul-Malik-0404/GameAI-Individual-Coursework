using UnityEngine;
using UnityEngine.AI;

// NavMesh-driven heavy bruiser NPC with directional front armour, enrage, and a 6-state FSM.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody))]
public class TankerBot : MonoBehaviour, IDamageable, IHUDTarget
{
    public enum TankerState { Guard, Search, Chase, Bombard, Charge, Recover }

    [Header("State")]
    public TankerState currentState = TankerState.Guard;

    [Header("Identity")]
    [Tooltip("Leave empty to auto-number in scene order.")]
    public string npcId = "";

    [Header("References")]
    [Tooltip("Defaults to the object tagged 'Player' if empty.")]
    public Transform target;
    public GameObject bulletPrefab;
    public Transform firePoint;
    [Tooltip("Wall layer(s) blocking vision. Falls back to the A* grid's Unwalkable Mask.")]
    public LayerMask obstacleMask;
    [Tooltip("External Transform for the guard post; defaults to start pose.")]
    public Transform guardPost;

    [Header("Health & Armour")]
    public float maxHealth = 150f;
    public float currentHealth = 150f;
    [Range(0f, 1f)] public float frontArmourMultiplier = 0.5f;
    [Range(0f, 360f)] public float frontArmourAngle = 120f;
    public float stunnedDamageMultiplier = 1.5f;
    [Range(0f, 1f)] public float enrageHealthPercent = 0.35f;

    [Header("Senses")]
    public float viewDistance = 14f;
    [Range(10f, 360f)] public float viewAngle = 140f;
    public float hearingRadius = 20f;
    public float loseSightDelay = 1f;

    [Header("Movement")]
    public float guardSpeed = 2f;
    public float chaseSpeed = 3f;
    public float turnSpeed = 4f;
    public float pathUpdateInterval = 0.3f;

    [Header("Guard")]
    public float scanAngle = 60f;
    public float scanSpeed = 30f;

    [Header("Search")]
    public float searchLookDuration = 3f;
    public float searchTimeout = 20f;

    [Header("Bombard")]
    public float bombardRange = 10f;
    public float bombardRangeBuffer = 2f;
    public int pelletsPerVolley = 3;
    public float spreadAngle = 12f;
    public int volleysPerBurst = 3;
    public float timeBetweenVolleys = 0.5f;
    public float reloadTime = 1.8f;
    public float bulletSpeed = 12f;
    public int bulletDamage = 8;

    [Header("Damage Model (Option A)")]
    [Tooltip("Pellet/ram profile: frequent grazes, rare crits (expected 0.9x base damage).")]
    public DamageProfile damageProfile = new DamageProfile(0.4f, 0.5f, 0.1f, 2f);

    [Header("Charge")]
    public float chargeMinRange = 4f;
    public float chargeMaxRange = 14f;
    public float chargeCooldown = 6f;
    [Range(0f, 1f)] public float chargeChance = 0.35f;
    public float kiteTimeBeforeCharge = 3f;
    public float windUpTime = 0.8f;
    public float chargeSpeed = 12f;
    public float chargeMaxTime = 1.5f;
    public int ramDamage = 25;
    public float ramKnockback = 10f;
    public float recoverTime = 1.2f;
    public float crashRecoverTime = 2.5f;

    [Header("Enraged")]
    public float enragedSpeedMultiplier = 1.3f;
    public float enragedFireRateMultiplier = 1.6f;
    public float enragedChargeCooldownMultiplier = 0.5f;
    [Range(0f, 1f)] public float enragedChargeChance = 0.6f;

    [Header("Debug")]
    public bool logStateChanges = true;

    private readonly StateMachine<TankerState> fsm = new StateMachine<TankerState>();
    private TankerSearchState searchState;
    private TankerChargeState chargeState;
    private TankerRecoverState recoverState;

    private bool isDead;
    private float chargeReadyTime;
    private float chargeDecisionTimer;
    private float outOfRangeTime;

    public NavMeshMotor Motor { get; private set; }
    public NpcPerception Perception { get; private set; }
    public Vector3 GuardPosition { get; private set; }
    public Quaternion GuardRotation { get; private set; }
    public bool IsEnraged { get; private set; }
    public float StateTime { get { return fsm.StateTime; } }

    public string Id { get { return npcId; } }
    public string StateName { get { return IsEnraged ? currentState + " (Enraged)" : currentState.ToString(); } }
    public Color StateColor { get { return GetStateColor(currentState); } }
    public float CurrentHealth { get { return currentHealth; } }
    public float MaxHealth { get { return maxHealth; } }

    void Awake()
    {
        npcId = SpatialUtility.ResolveNpcId(this, npcId, "Tanker");
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
        currentHealth = maxHealth;
        obstacleMask = SpatialUtility.ResolveObstacleMask(obstacleMask, npcId);
        GuardPosition = guardPost != null ? guardPost.position : transform.position;
        GuardRotation = guardPost != null ? guardPost.rotation : transform.rotation;

        Motor = new NavMeshMotor(transform, GetComponent<NavMeshAgent>(), GetComponent<Rigidbody>());
        Perception = new NpcPerception(transform);
        Perception.Initialize(target, obstacleMask);
        SyncPerceptionSettings();

        searchState = new TankerSearchState(this);
        chargeState = new TankerChargeState(this);
        recoverState = new TankerRecoverState(this);

        fsm.Register(new TankerGuardState(this));
        fsm.Register(searchState);
        fsm.Register(new TankerChaseState(this));
        fsm.Register(new TankerBombardState(this));
        fsm.Register(chargeState);
        fsm.Register(recoverState);

        if (bulletPrefab == null || firePoint == null)
            Debug.LogWarning(npcId + ": Bullet Prefab or Fire Point is empty, so this bot can't shoot.");

        ChangeState(currentState);
    }

    void Update()
    {
        if (isDead || Motor == null) return;

        chargeDecisionTimer -= Time.deltaTime;
        Motor.BeginFrame(Time.deltaTime, CurrentMoveSpeed());
        SyncPerceptionSettings();

        bool alert = currentState != TankerState.Guard && currentState != TankerState.Search;
        Perception.UpdateVision(!alert);

        // One-way enrage phase below health threshold.
        if (!IsEnraged && currentHealth / maxHealth <= enrageHealthPercent)
        {
            IsEnraged = true;
            chargeReadyTime = Time.time;
            Debug.Log(npcId + " is ENRAGED!");
        }

        fsm.Tick(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (isDead || Motor == null) return;
        fsm.FixedTick(Time.fixedDeltaTime);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isDead && currentState == TankerState.Charge && chargeState != null)
            chargeState.HandleCollision(collision);
    }

    public void ChangeState(TankerState nextState)
    {
        currentState = nextState;
        fsm.ChangeState(nextState, logStateChanges, npcId);
    }

    public void StartSearch(Vector3 point)
    {
        searchState.SearchPoint = point;
        ChangeState(TankerState.Search);
    }

    public void StartRecover(float duration)
    {
        recoverState.Duration = duration;
        ChangeState(TankerState.Recover);
    }

    public void ResetOutOfRangeTime() { outOfRangeTime = 0f; }
    public void AddOutOfRangeTime(float dt) { outOfRangeTime += dt; }

    public void StartChargeCooldown()
    {
        float cooldown = chargeCooldown * (IsEnraged ? enragedChargeCooldownMultiplier : 1f);
        chargeReadyTime = Time.time + windUpTime + cooldown;
    }

    // Evaluates at most once per second whether to charge along a clear NavMesh line.
    public bool TryStartCharge(float distance)
    {
        if (!Perception.SeesPlayer || Time.time < chargeReadyTime) return false;
        if (distance < chargeMinRange || distance > chargeMaxRange) return false;
        if (chargeDecisionTimer > 0f) return false;
        chargeDecisionTimer = 1f;

        bool beingKited = outOfRangeTime >= kiteTimeBeforeCharge;
        float chance = IsEnraged ? enragedChargeChance : chargeChance;
        if (!beingKited && Random.value > chance) return false;
        if (!Motor.HasClearNavMeshLine(Perception.Target.position)) return false;

        ChangeState(TankerState.Charge);
        return true;
    }

    public void FireVolley()
    {
        NpcShooter.FireSpreadVolley(
            bulletPrefab, firePoint, Perception.Target,
            pelletsPerVolley, spreadAngle, bulletSpeed, bulletDamage, damageProfile, npcId);
    }

    public void TakeDamage(int damage, Vector3 sourcePosition)
    {
        if (isDead) return;

        float multiplier = 1f;
        string hitType = "side/back";
        if (currentState == TankerState.Recover)
        {
            multiplier = stunnedDamageMultiplier;
            hitType = "stunned";
        }
        else if (SpatialUtility.IsWithinAngle(transform.position, transform.forward, sourcePosition, frontArmourAngle * 0.5f))
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

        if (!Perception.SeesPlayer)
        {
            Perception.RecordThreat(sourcePosition, true);
            if (Motor != null && (currentState == TankerState.Guard || currentState == TankerState.Search))
                StartSearch(sourcePosition);
        }
    }

    private void HearGunshot(Vector3 shotPosition)
    {
        if (isDead || Motor == null || !Perception.TryHearGunshot(shotPosition)) return;

        if (currentState == TankerState.Guard || currentState == TankerState.Search)
            StartSearch(shotPosition);
    }

    private void SyncPerceptionSettings()
    {
        Perception.ViewDistance = viewDistance;
        Perception.ViewAngle = viewAngle;
        Perception.ProximityRadius = 3f;
        Perception.LoseSightDelay = loseSightDelay;
        Perception.HearingRadius = hearingRadius;
        Perception.WallMuffling = 1f;
    }

    private float CurrentMoveSpeed()
    {
        bool calm = currentState == TankerState.Guard || currentState == TankerState.Search;
        float speed = calm ? guardSpeed : chaseSpeed;
        return IsEnraged ? speed * enragedSpeedMultiplier : speed;
    }

    private void Die()
    {
        isDead = true;
        Debug.Log(npcId + " defeated!");
        Destroy(gameObject);
    }

    private static Color GetStateColor(TankerState state)
    {
        switch (state)
        {
            case TankerState.Guard:   return Color.green;
            case TankerState.Search:  return Color.yellow;
            case TankerState.Chase:   return new Color(1f, 0.5f, 0f);
            case TankerState.Bombard: return Color.red;
            case TankerState.Charge:  return Color.magenta;
            default:                  return new Color(0.5f, 0.8f, 1f);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = GetStateColor(currentState);
        Gizmos.DrawSphere(transform.position + Vector3.up * 2.4f, 0.3f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.5f, 1f);
        Gizmos.DrawWireSphere(transform.position, hearingRadius);

        SpatialUtility.DrawConeGizmo(transform.position, transform.forward, viewAngle, viewDistance, Color.yellow);
        SpatialUtility.DrawConeGizmo(transform.position, transform.forward, frontArmourAngle, 2f, Color.gray);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, bombardRange);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, chargeMaxRange);

        if (!Application.isPlaying) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(GuardPosition, Vector3.one * 0.8f);

        if (currentState == TankerState.Charge && chargeState != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, transform.position + chargeState.GizmoDirection * chargeMaxRange);
        }
    }
}
