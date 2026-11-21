using UnityEngine;

// Returns to the guard post and sweeps its vision cone sinusoidally left and right.
public class TankerGuardState : AgentState<TankerBot, TankerBot.TankerState>
{
    public override TankerBot.TankerState Id { get { return TankerBot.TankerState.Guard; } }

    public TankerGuardState(TankerBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        owner.ResetOutOfRangeTime();
    }

    public override void Tick(float dt)
    {
        if (owner.Perception.SeesPlayer)
        {
            owner.ChangeState(TankerBot.TankerState.Chase);
            return;
        }

        if (SpatialUtility.FlatDistance(owner.transform.position, owner.GuardPosition) >
            owner.Motor.StoppingDistance + 0.5f)
        {
            owner.Motor.MoveTo(owner.GuardPosition, owner.pathUpdateInterval);
            return;
        }

        float phase = Time.time * owner.scanSpeed / Mathf.Max(1f, owner.scanAngle);
        float angle = owner.scanAngle * Mathf.Sin(phase);
        Vector3 lookDirection = owner.GuardRotation * Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        owner.Motor.FacePoint(owner.transform.position + lookDirection * 5f);
    }

    public override void FixedTick(float fixedDt)
    {
        owner.Motor.ApplyNavMeshPhysics(fixedDt, owner.turnSpeed);
    }
}
