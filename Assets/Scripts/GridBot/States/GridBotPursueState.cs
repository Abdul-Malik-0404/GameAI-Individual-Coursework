using UnityEngine;

// Chases the player via A* (followers take flanking angles), or investigates predicted position if lost.
public class GridBotPursueState : AgentState<GridBot, GridBot.BotState>
{
    public override GridBot.BotState Id { get { return GridBot.BotState.Pursue; } }

    public GridBotPursueState(GridBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        if (owner.squad != null)
            owner.squad.ReportPlayer(owner, owner.Perception.LastKnownPosition);
        if (GridBotSquadCoordinator.Instance != null)
            GridBotSquadCoordinator.Instance.BroadcastDistressCall(owner, owner.Perception.LastKnownPosition);
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

        Vector3 targetPos = senses.SeesPlayer ? senses.Target.position : senses.LastKnownPosition;
        bool isFollower = owner.squad != null && owner.squad.Leader != null && owner.squad.Leader != owner;
        Vector3 destination = isFollower
            ? owner.squad.GetFlankPosition(owner, targetPos, owner.attackRange)
            : targetPos;

        owner.Motor.MoveTowards(destination, owner.chaseSpeed, owner.pathUpdateInterval);

        if (owner.Motor.PathFailed)
            owner.Motor.FacePoint(senses.LastKnownPosition);
    }
}
