using UnityEngine;

// Pursues the player via NavMesh, tracking kiting duration to trigger an anti-kite charge.
public class TankerChaseState : AgentState<TankerBot, TankerBot.TankerState>
{
    public override TankerBot.TankerState Id { get { return TankerBot.TankerState.Chase; } }

    public TankerChaseState(TankerBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        owner.ResetOutOfRangeTime();
    }

    public override void Tick(float dt)
    {
        NpcPerception senses = owner.Perception;
        if (senses.HasLostSight)
        {
            owner.StartSearch(senses.LastKnownPosition);
            return;
        }

        float distance = senses.SeesPlayer
            ? SpatialUtility.FlatDistance(owner.transform.position, senses.Target.position)
            : float.MaxValue;

        if (senses.SeesPlayer && distance <= owner.bombardRange)
        {
            owner.ChangeState(TankerBot.TankerState.Bombard);
            return;
        }

        if (senses.SeesPlayer) owner.AddOutOfRangeTime(dt);
        if (owner.TryStartCharge(distance)) return;

        owner.Motor.MoveTo(senses.SeesPlayer ? senses.Target.position : senses.LastKnownPosition, owner.pathUpdateInterval);
    }

    public override void FixedTick(float fixedDt)
    {
        owner.Motor.ApplyNavMeshPhysics(fixedDt, owner.turnSpeed);
    }
}
