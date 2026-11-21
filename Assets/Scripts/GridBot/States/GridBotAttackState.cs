using UnityEngine;

// Holds position within range (followers fan out to flank positions) and fires velocity-leading shots.
public class GridBotAttackState : AgentState<GridBot, GridBot.BotState>
{
    public override GridBot.BotState Id { get { return GridBot.BotState.Attack; } }

    public GridBotAttackState(GridBot owner) : base(owner) { }

    public override void Enter()
    {
        owner.Motor.ClearDestination();
        owner.ResetFireTimer(Random.Range(0.2f, owner.fireCooldown));
    }

    public override void Tick(float dt)
    {
        NpcPerception senses = owner.Perception;
        if (!senses.SeesPlayer)
        {
            if (senses.HasLostSight)
                owner.StartInvestigating(senses.PredictedTargetPosition());
            else
                owner.Motor.FacePoint(senses.LastKnownPosition);
            return;
        }

        if (SpatialUtility.FlatDistance(owner.transform.position, senses.Target.position) >
            owner.attackRange + owner.attackRangeBuffer)
        {
            owner.ChangeState(GridBot.BotState.Pursue);
            return;
        }

        // Squad followers sidestep toward their flank angle if bunched up with a teammate.
        if (owner.squad != null && owner.squad.Leader != null && owner.squad.Leader != owner)
        {
            Vector3 flankPos = owner.squad.GetFlankPosition(owner, senses.Target.position, owner.attackRange);
            if (owner.squad.IsCrowdedInCombat(owner) ||
                SpatialUtility.FlatDistance(owner.transform.position, flankPos) > 1.5f)
            {
                owner.Motor.MoveTowards(flankPos, owner.patrolSpeed, owner.pathUpdateInterval);
            }
        }

        owner.Motor.FacePoint(senses.Target.position);

        if (owner.CanFire &&
            SpatialUtility.IsWithinAngle(owner.transform.position, owner.transform.forward, senses.Target.position, 15f))
        {
            owner.ShootAtTarget();
        }
    }
}
