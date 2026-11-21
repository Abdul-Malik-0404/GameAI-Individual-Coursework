using UnityEngine;

// Post-charge stun window during which TankerBot cannot act and takes bonus damage.
public class TankerRecoverState : AgentState<TankerBot, TankerBot.TankerState>
{
    public float Duration { get; set; }
    public override TankerBot.TankerState Id { get { return TankerBot.TankerState.Recover; } }

    public TankerRecoverState(TankerBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        owner.Motor.StopHorizontal();
    }

    public override void Tick(float dt)
    {
        if (owner.StateTime < Duration) return;

        NpcPerception senses = owner.Perception;
        if (senses.SeesPlayer)
        {
            bool inRange = SpatialUtility.FlatDistance(owner.transform.position, senses.Target.position) <= owner.bombardRange;
            owner.ChangeState(inRange ? TankerBot.TankerState.Bombard : TankerBot.TankerState.Chase);
        }
        else
        {
            owner.StartSearch(senses.LastKnownPosition);
        }
    }

    public override void FixedTick(float fixedDt)
    {
        owner.Motor.ApplyNavMeshPhysics(fixedDt, owner.turnSpeed);
    }
}
