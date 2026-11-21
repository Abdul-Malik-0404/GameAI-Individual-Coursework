using UnityEngine;

// Chases the player via A* until in firing range, or investigates their predicted path if lost.
public class GridBotPursueState : AgentState<GridBot, GridBot.BotState>
{
    public override GridBot.BotState Id { get { return GridBot.BotState.Pursue; } }

    public GridBotPursueState(GridBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        if (owner.squad != null)
            owner.squad.ReportPlayer(owner, owner.Perception.LastKnownPosition);
    }

    public override void Tick(float dt)
    {
        NpcPerception senses = owner.Perception;
        if (senses.HasLostSight)
        {
            owner.StartInvestigating(senses.PredictedTargetPosition());
            return;
        }

        if (senses.SeesPlayer &&
            SpatialUtility.FlatDistance(owner.transform.position, senses.Target.position) <= owner.attackRange)
        {
            owner.ChangeState(GridBot.BotState.Attack);
            return;
        }

        Vector3 destination = senses.SeesPlayer ? senses.Target.position : senses.LastKnownPosition;
        owner.Motor.MoveTowards(destination, owner.chaseSpeed, owner.pathUpdateInterval);

        if (owner.Motor.PathFailed)
            owner.Motor.FacePoint(senses.LastKnownPosition);
    }
}
