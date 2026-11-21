using UnityEngine;

// Patrols waypoints/wander area, or holds a squad formation slot behind the squad leader.
public class GridBotPatrolState : AgentState<GridBot, GridBot.BotState>
{
    private Vector3 destination;
    private int patrolIndex = -1;
    private float waitTimer;
    private float legTimer;

    public override GridBot.BotState Id { get { return GridBot.BotState.Patrol; } }

    public GridBotPatrolState(GridBot owner) : base(owner) { }

    public override void Enter()
    {
        PickNextPoint();
    }

    public override void Tick(float dt)
    {
        if (owner.Perception.SeesPlayer)
        {
            owner.ChangeState(GridBot.BotState.Pursue);
            return;
        }

        if (owner.squad != null && owner.squad.Leader != null)
        {
            if (owner.squad.Leader != owner)
            {
                UpdateFormationSlot();
                return;
            }
            if (owner.squad.LeaderShouldWait()) return;
        }

        if (waitTimer > 0f)
        {
            waitTimer -= dt;
            if (waitTimer <= 0f) PickNextPoint();
            return;
        }

        owner.Motor.MoveTowards(destination, owner.patrolSpeed, owner.pathUpdateInterval);
        legTimer += dt;

        if (owner.Motor.HasArrived || owner.Motor.PathFailed || legTimer > owner.patrolLegTimeout)
            waitTimer = Mathf.Max(0.1f, Random.Range(owner.minPatrolWait, owner.maxPatrolWait));
    }

    private void UpdateFormationSlot()
    {
        Vector3 slot = owner.squad.GetSlotPosition(owner);
        float distance = SpatialUtility.FlatDistance(owner.transform.position, slot);

        if (distance > owner.squad.arriveDistance)
        {
            float speed = distance > owner.squad.catchUpDistance ? owner.chaseSpeed : owner.patrolSpeed;
            owner.Motor.MoveTowards(slot, speed, owner.pathUpdateInterval);
        }
        else
        {
            owner.Motor.FacePoint(owner.transform.position + owner.squad.Leader.transform.forward * 5f);
        }
    }

    private void PickNextPoint()
    {
        owner.Motor.ClearDestination();
        legTimer = 0f;
        waitTimer = 0f;

        Transform[] points = owner.patrolPoints;
        if (points != null && points.Length > 0)
        {
            int index = Random.Range(0, points.Length);
            if (points.Length > 1 && index == patrolIndex)
                index = (index + Random.Range(1, points.Length)) % points.Length;

            patrolIndex = index;
            if (points[index] != null)
            {
                destination = points[index].position;
                return;
            }
        }

        destination = GridCoverFinder.RandomWalkablePointNear(owner.Grid, owner.HomePosition, owner.wanderRadius);
    }
}
