using UnityEngine;

// Telegraphs a wind-up aim, then rushes in a locked straight line until impact, wall crash, or timeout.
public class TankerChargeState : AgentState<TankerBot, TankerBot.TankerState>
{
    private bool isWindingUp;
    private Vector3 aimPoint;
    private Vector3 chargeDirection;
    private float rushTime;

    public override TankerBot.TankerState Id { get { return TankerBot.TankerState.Charge; } }

    public Vector3 GizmoDirection
    {
        get
        {
            Vector3 dir = isWindingUp ? (aimPoint - owner.transform.position) : chargeDirection;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.001f ? dir.normalized : owner.transform.forward;
        }
    }

    public TankerChargeState(TankerBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        isWindingUp = true;
        rushTime = 0f;
        aimPoint = owner.Perception.SeesPlayer
            ? owner.Perception.Target.position
            : owner.Perception.LastKnownPosition;
        owner.StartChargeCooldown();
    }

    public override void Tick(float dt)
    {
        if (!isWindingUp) return;

        NpcPerception senses = owner.Perception;
        aimPoint = senses.SeesPlayer ? senses.Target.position : senses.LastKnownPosition;
        owner.Motor.FacePoint(aimPoint);

        if (owner.StateTime >= owner.windUpTime)
        {
            chargeDirection = aimPoint - owner.transform.position;
            chargeDirection.y = 0f;
            chargeDirection = chargeDirection.sqrMagnitude > 0.01f ? chargeDirection.normalized : owner.transform.forward;
            isWindingUp = false;
            rushTime = 0f;
        }
    }

    public override void FixedTick(float fixedDt)
    {
        if (isWindingUp)
        {
            owner.Motor.ApplyNavMeshPhysics(fixedDt, owner.turnSpeed);
            return;
        }

        rushTime += fixedDt;

        // Sharp drop in forward speed indicates a wall impact.
        if (rushTime > 0.2f && owner.Motor.HorizontalSpeed < owner.chargeSpeed * 0.3f)
        {
            if (owner.logStateChanges) Debug.Log(owner.npcId + " crashed into a wall!");
            owner.Motor.StopHorizontal();
            owner.StartRecover(owner.crashRecoverTime);
            return;
        }

        if (rushTime >= owner.chargeMaxTime)
        {
            owner.Motor.StopHorizontal();
            owner.StartRecover(owner.recoverTime);
            return;
        }

        owner.Motor.SetHorizontalVelocity(chargeDirection * owner.chargeSpeed);
    }

    public void HandleCollision(Collision collision)
    {
        if (isWindingUp) return;

        PlayerController player = collision.collider.GetComponentInParent<PlayerController>();
        if (player == null) return;

        string hitType;
        int rolledDamage = owner.damageProfile.Roll(owner.ramDamage, owner.npcId + " ram", out hitType);
        player.TakeDamage(rolledDamage, hitType == "HIT" ? "RAM" : "RAM " + hitType);
        player.ApplyKnockback(chargeDirection * owner.ramKnockback, 0.35f);

        if (owner.logStateChanges) Debug.Log(owner.npcId + " rammed the player!");
        owner.StartRecover(owner.recoverTime);
    }
}
