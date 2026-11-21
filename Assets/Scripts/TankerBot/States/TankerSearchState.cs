using UnityEngine;

// Investigates a gunshot or last-seen location, sweeps on arrival, then returns to Guard.
public class TankerSearchState : AgentState<TankerBot, TankerBot.TankerState>
{
    private bool isLooking;
    private float lookTimer;

    public Vector3 SearchPoint { get; set; }
    public override TankerBot.TankerState Id { get { return TankerBot.TankerState.Search; } }

    public TankerSearchState(TankerBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        isLooking = false;
    }

    public override void Tick(float dt)
    {
        if (owner.Perception.SeesPlayer)
        {
            owner.ChangeState(TankerBot.TankerState.Chase);
            return;
        }

        if (owner.StateTime > owner.searchTimeout)
        {
            owner.ChangeState(TankerBot.TankerState.Guard);
            return;
        }

        if (!isLooking)
        {
            owner.Motor.MoveTo(SearchPoint, owner.pathUpdateInterval);
            if (owner.Motor.HasArrived || owner.Motor.PathFailed)
            {
                isLooking = true;
                lookTimer = owner.searchLookDuration;
            }
            return;
        }

        owner.Motor.FacePoint(owner.transform.position + Quaternion.Euler(0f, Time.time * 90f, 0f) * Vector3.forward * 5f);
        lookTimer -= dt;
        if (lookTimer <= 0f)
            owner.ChangeState(TankerBot.TankerState.Guard);
    }

    public override void FixedTick(float fixedDt)
    {
        owner.Motor.ApplyNavMeshPhysics(fixedDt, owner.turnSpeed);
    }
}
