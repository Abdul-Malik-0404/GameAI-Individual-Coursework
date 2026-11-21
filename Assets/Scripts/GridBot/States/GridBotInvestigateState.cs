using UnityEngine;

// Moves to a sound/sighting clue, scans 360°, and probabilistically searches nearby spots.
public class GridBotInvestigateState : AgentState<GridBot, GridBot.BotState>
{
    private bool isLookingAround;
    private float lookAroundTimer;
    private int searchesDone;

    public Vector3 TargetPoint { get; set; }
    public override GridBot.BotState Id { get { return GridBot.BotState.Investigate; } }

    public GridBotInvestigateState(GridBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        isLookingAround = false;
        searchesDone = 0;
    }

    public override void Tick(float dt)
    {
        if (owner.Perception.SeesPlayer)
        {
            owner.ChangeState(GridBot.BotState.Pursue);
            return;
        }

        if (owner.StateTime > owner.investigateTimeout)
        {
            owner.ChangeState(GridBot.BotState.Patrol);
            return;
        }

        if (!isLookingAround)
        {
            owner.Motor.MoveTowards(TargetPoint, owner.patrolSpeed, owner.pathUpdateInterval);
            if (owner.Motor.HasArrived || owner.Motor.PathFailed)
            {
                isLookingAround = true;
                lookAroundTimer = owner.lookAroundDuration;
            }
            return;
        }

        owner.Motor.Spin(owner.lookAroundSpeed);
        lookAroundTimer -= dt;
        if (lookAroundTimer > 0f) return;

        if (searchesDone < owner.maxExtraSearches && Random.value < owner.searchAgainChance)
        {
            searchesDone++;
            TargetPoint = GridCoverFinder.RandomWalkablePointNear(owner.Grid, TargetPoint, owner.searchRadius);
            isLookingAround = false;
            owner.Motor.ClearDestination();
        }
        else
        {
            owner.ChangeState(GridBot.BotState.Patrol);
        }
    }
}
