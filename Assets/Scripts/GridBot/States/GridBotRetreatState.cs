using UnityEngine;

// Seeks LOS-hidden cover on low health, regenerates while unseen, and re-engages once recovered.
public class GridBotRetreatState : AgentState<GridBot, GridBot.BotState>
{
    private float coverCheckTimer;
    private bool coverCompromised;

    public Vector3 CoverPoint { get; private set; }
    public override GridBot.BotState Id { get { return GridBot.BotState.Retreat; } }

    public GridBotRetreatState(GridBot owner) : base(owner) { }

    public void MarkCoverCompromised()
    {
        coverCompromised = true;
    }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        CoverPoint = FindCover();
        coverCheckTimer = owner.coverCheckInterval;
        coverCompromised = false;
    }

    public override void Tick(float dt)
    {
        NpcPerception senses = owner.Perception;
        if (!senses.IsExposedToTarget())
            owner.RegenerateHealth(dt);

        if (owner.HealthPercent >= owner.recoverHealthPercent)
        {
            if (senses.HasMemory) owner.StartInvestigating(senses.LastKnownPosition);
            else owner.ChangeState(GridBot.BotState.Patrol);
            return;
        }

        coverCheckTimer -= dt;
        if (coverCheckTimer <= 0f)
        {
            coverCheckTimer = owner.coverCheckInterval;
            if (coverCompromised || senses.HasLineOfSight(senses.ThreatPosition, CoverPoint))
            {
                CoverPoint = FindCover();
                coverCompromised = false;
                owner.Motor.ClearDestination();
            }
        }

        owner.Motor.MoveTowards(CoverPoint, owner.chaseSpeed, owner.pathUpdateInterval);

        if (owner.Motor.HasArrived)
            owner.Motor.FacePoint(senses.ThreatPosition);
    }

    private Vector3 FindCover()
    {
        return GridCoverFinder.FindCoverPoint(
            owner.Grid, owner.Perception, owner.transform.position, owner.transform.forward,
            owner.coverSearchRadius, owner.minDistanceFromThreat);
    }
}
