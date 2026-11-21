using UnityEngine;

// Holds ground within bombard range firing spread bursts with reload windows.
public class TankerBombardState : AgentState<TankerBot, TankerBot.TankerState>
{
    private int volleysFired;
    private float volleyTimer;

    public override TankerBot.TankerState Id { get { return TankerBot.TankerState.Bombard; } }

    public TankerBombardState(TankerBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        volleysFired = 0;
        volleyTimer = Random.Range(0.3f, 0.8f);
    }

    public override void Tick(float dt)
    {
        volleyTimer -= dt;

        NpcPerception senses = owner.Perception;
        if (!senses.SeesPlayer)
        {
            if (senses.HasLostSight)
                owner.StartSearch(senses.LastKnownPosition);
            else
                owner.Motor.FacePoint(senses.LastKnownPosition);
            return;
        }

        float distance = SpatialUtility.FlatDistance(owner.transform.position, senses.Target.position);
        if (distance > owner.bombardRange + owner.bombardRangeBuffer)
        {
            owner.ChangeState(TankerBot.TankerState.Chase);
            return;
        }

        owner.Motor.FacePoint(senses.Target.position);
        if (owner.TryStartCharge(distance)) return;

        if (volleyTimer <= 0f &&
            SpatialUtility.IsWithinAngle(owner.transform.position, owner.transform.forward, senses.Target.position, 20f))
        {
            owner.FireVolley();
            volleysFired++;

            float fireRate = owner.IsEnraged ? owner.enragedFireRateMultiplier : 1f;
            if (volleysFired >= owner.volleysPerBurst)
            {
                volleysFired = 0;
                volleyTimer = owner.reloadTime / fireRate;
            }
            else
            {
                volleyTimer = owner.timeBetweenVolleys / fireRate;
            }
        }
    }

    public override void FixedTick(float fixedDt)
    {
        owner.Motor.ApplyNavMeshPhysics(fixedDt, owner.turnSpeed);
    }
}
