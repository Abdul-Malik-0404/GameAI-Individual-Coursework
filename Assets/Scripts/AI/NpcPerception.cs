using UnityEngine;

// Shared NPC vision, gunshot hearing, line-of-sight, and target memory system.
public class NpcPerception
{
    private readonly Transform owner;
    private Transform target;
    private Rigidbody targetRb;
    private PlayerController targetPlayer;
    private LayerMask obstacleMask;

    public float ViewDistance { get; set; } = 15f;
    public float ViewAngle { get; set; } = 110f;
    public float ProximityRadius { get; set; } = 2.5f;
    public float LoseSightDelay { get; set; } = 0.5f;
    public float PredictionTime { get; set; } = 1f;
    public float HearingRadius { get; set; } = 15f;
    public float WallMuffling { get; set; } = 1f;

    public Transform Target { get { return target; } }
    public Rigidbody TargetRigidbody { get { return targetRb; } }
    public bool SeesPlayer { get; private set; }
    public bool HasMemory { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }
    public Vector3 LastKnownVelocity { get; private set; }
    public float LastSeenTime { get; private set; } = -999f;
    public bool HasThreat { get; private set; }
    public Vector3 LastThreatPosition { get; private set; }

    public bool TargetIsDead { get { return targetPlayer != null && targetPlayer.IsDead; } }
    public bool HasLostSight { get { return !SeesPlayer && Time.time - LastSeenTime > LoseSightDelay; } }

    public Vector3 ThreatPosition
    {
        get
        {
            if (SeesPlayer && target != null) return target.position;
            if (HasThreat) return LastThreatPosition;
            return owner.position + owner.forward;
        }
    }

    public NpcPerception(Transform owner)
    {
        this.owner = owner;
    }

    public void Initialize(Transform explicitTarget, LayerMask wallMask)
    {
        obstacleMask = wallMask;
        target = explicitTarget;
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
    }

    // Updates vision; calm states enforce the view cone while alert states track 360° with LOS.
    public void UpdateVision(bool useViewCone)
    {
        SeesPlayer = CanSeeTarget(useViewCone);
        if (!SeesPlayer) return;

        LastKnownPosition = target.position;
        LastKnownVelocity = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        LastSeenTime = Time.time;
        HasMemory = true;
        LastThreatPosition = target.position;
        HasThreat = true;
    }

    // Returns true if the gunshot is audible (optionally muffled by walls) and records its origin.
    public bool TryHearGunshot(Vector3 shotPosition)
    {
        if (TargetIsDead) return false;

        float range = HearingRadius;
        if (WallMuffling < 1f && !HasLineOfSight(owner.position, shotPosition))
            range *= WallMuffling;
        if (SpatialUtility.FlatDistance(owner.position, shotPosition) > range) return false;

        RecordThreat(shotPosition, true);
        return true;
    }

    public void RecordThreat(Vector3 position, bool updateMemoryIfUnseen)
    {
        LastThreatPosition = position;
        HasThreat = true;

        if (updateMemoryIfUnseen && !SeesPlayer)
        {
            LastKnownPosition = position;
            LastKnownVelocity = Vector3.zero;
            HasMemory = true;
        }
    }

    public Vector3 PredictedTargetPosition()
    {
        Vector3 velocity = LastKnownVelocity;
        velocity.y = 0f;
        return LastKnownPosition + velocity * PredictionTime;
    }

    public bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        return !Physics.Linecast(from, to, obstacleMask);
    }

    public bool IsExposedToTarget()
    {
        if (target == null || TargetIsDead) return false;
        return SpatialUtility.FlatDistance(owner.position, target.position) <= ViewDistance
            && HasLineOfSight(target.position, owner.position);
    }

    private bool CanSeeTarget(bool useViewCone)
    {
        if (target == null || TargetIsDead) return false;

        float distance = SpatialUtility.FlatDistance(owner.position, target.position);
        if (distance > ViewDistance) return false;

        if (useViewCone && distance > ProximityRadius &&
            !SpatialUtility.IsWithinAngle(owner.position, owner.forward, target.position, ViewAngle * 0.5f))
            return false;

        return HasLineOfSight(owner.position, target.position);
    }
}
