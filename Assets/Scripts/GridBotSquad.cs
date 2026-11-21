using System.Collections.Generic;
using UnityEngine;

// Coordinates a leader-follower GridBot squad: patrol formations, shared alerts, flanking, and separation disband.
public class GridBotSquad : MonoBehaviour
{
    public enum FormationShape { Wedge, Line, Column }

    [Header("Members")]
    [Tooltip("First alive member is the leader; the next takes over if it dies.")]
    public List<GridBot> members = new List<GridBot>();

    [Header("Formation")]
    public FormationShape shape = FormationShape.Wedge;
    public float spacing = 2f;
    [Tooltip("Falls back to single-file Column whenever a formation slot hits a wall.")]
    public bool singleFileWhenBlocked = true;

    [Header("Keeping Together")]
    public float arriveDistance = 1f;
    public float catchUpDistance = 2f;
    public float maxStraggleDistance = 5f;
    public float maxWaitTime = 4f;

    [Header("Combat Flanking")]
    [Tooltip("Angle offset (degrees) followers take around the player relative to the leader.")]
    public float flankAngle = 50f;
    [Tooltip("Minimum distance followers keep from squadmates while attacking.")]
    public float combatSpreadDistance = 2.5f;

    [Header("Dynamic Disband")]
    public bool isDynamic;
    public float disbandDistance = 16f;
    public float disbandDelay = 5f;
    public float rejoinCooldown = 6f;

    [Header("Shared Alerts")]
    public bool shareAlerts = true;

    public FormationShape CurrentShape { get; private set; }

    public GridBot Leader
    {
        get
        {
            foreach (GridBot member in members)
            {
                if (member != null) return member;
            }
            return null;
        }
    }

    private GridManager grid;
    private float leaderWaitTimer;
    private readonly Dictionary<GridBot, float> separationTimers = new Dictionary<GridBot, float>();

    void Awake()
    {
        foreach (GridBot member in members)
        {
            if (member != null) member.squad = this;
        }
        CurrentShape = shape;
    }

    void Start()
    {
        if (Pathfinding.Instance != null)
            grid = Pathfinding.Instance.GetComponent<GridManager>();
    }

    void Update()
    {
        RemoveDeadMembers();
        if (isDynamic)
        {
            UpdateSeparationDisband();
            if (members.Count <= 1)
            {
                Disband();
                return;
            }
        }
        CurrentShape = ChooseShape();
    }

    public void AddMember(GridBot bot)
    {
        if (bot == null || members.Contains(bot)) return;
        members.Add(bot);
        bot.squad = this;
        separationTimers[bot] = 0f;
    }

    public void RemoveMember(GridBot bot, bool applyCooldown)
    {
        if (bot == null) return;
        members.Remove(bot);
        separationTimers.Remove(bot);
        if (bot.squad == this) bot.squad = null;
        if (applyCooldown) bot.SetRejoinCooldown(rejoinCooldown);
    }

    public Vector3 GetSlotPosition(GridBot member)
    {
        RemoveDeadMembers();
        return SlotPosition(members.IndexOf(member), CurrentShape);
    }

    // Computes a walkable flanking position around the player offset from the leader's angle of approach.
    public Vector3 GetFlankPosition(GridBot member, Vector3 targetPos, float attackRange)
    {
        RemoveDeadMembers();
        GridBot leader = Leader;
        int slotNumber = members.IndexOf(member);
        if (leader == null || slotNumber <= 0) return targetPos;

        Vector3 toLeader = leader.transform.position - targetPos;
        toLeader.y = 0f;
        if (toLeader.sqrMagnitude < 0.01f) toLeader = -leader.transform.forward;
        toLeader.Normalize();

        int row = (slotNumber + 1) / 2;
        float side = (slotNumber % 2 == 1) ? -1f : 1f;
        Vector3 dir = Quaternion.Euler(0f, side * flankAngle * row, 0f) * toLeader;
        Vector3 desired = targetPos + dir * (attackRange * 0.85f);

        if (grid != null && grid.IsReady)
        {
            Node node = grid.GetClosestWalkableNode(grid.NodeFromWorldPoint(desired), desired);
            if (node != null) return node.worldPosition;
        }
        return desired;
    }

    // Returns true if another alive squadmate is too close to the given member.
    public bool IsCrowdedInCombat(GridBot member)
    {
        foreach (GridBot other in members)
        {
            if (other == null || other == member) continue;
            if (SpatialUtility.FlatDistance(member.transform.position, other.transform.position) < combatSpreadDistance)
                return true;
        }
        return false;
    }

    Vector3 SlotPosition(int slotNumber, FormationShape formation)
    {
        GridBot leader = Leader;
        if (leader == null) return transform.position;

        Quaternion leaderFacing = Quaternion.Euler(0f, leader.transform.eulerAngles.y, 0f);
        return leader.transform.position + leaderFacing * SlotOffset(slotNumber, formation);
    }

    Vector3 SlotOffset(int slotNumber, FormationShape formation)
    {
        if (slotNumber <= 0) return Vector3.zero;

        int row = (slotNumber + 1) / 2;
        float side = (slotNumber % 2 == 1) ? -1f : 1f;

        switch (formation)
        {
            case FormationShape.Wedge: return new Vector3(side * spacing * row, 0f, -spacing * row);
            case FormationShape.Line:  return new Vector3(side * spacing * row, 0f, 0f);
            default:                   return new Vector3(0f, 0f, -spacing * slotNumber);
        }
    }

    FormationShape ChooseShape()
    {
        if (!singleFileWhenBlocked || shape == FormationShape.Column) return shape;
        if (Leader == null || grid == null || !grid.IsReady) return shape;

        for (int i = 1; i < members.Count; i++)
        {
            if (!grid.NodeFromWorldPoint(SlotPosition(i, shape)).isWalkable)
                return FormationShape.Column;
        }
        return shape;
    }

    public bool LeaderShouldWait()
    {
        RemoveDeadMembers();

        bool someoneBehind = false;
        for (int i = 1; i < members.Count; i++)
        {
            GridBot follower = members[i];
            if (follower.currentState != GridBot.BotState.Patrol) continue;

            if (SpatialUtility.FlatDistance(follower.transform.position, SlotPosition(i, CurrentShape)) > maxStraggleDistance)
            {
                someoneBehind = true;
                break;
            }
        }

        if (!someoneBehind)
        {
            leaderWaitTimer = 0f;
            return false;
        }

        leaderWaitTimer += Time.deltaTime;
        return leaderWaitTimer < maxWaitTime;
    }

    public void ReportPlayer(GridBot reporter, Vector3 position)
    {
        if (!shareAlerts) return;

        foreach (GridBot member in members)
        {
            if (member != null && member != reporter)
                member.HearSquadAlert(position);
        }
    }

    void UpdateSeparationDisband()
    {
        GridBot leader = Leader;
        if (leader == null) return;

        for (int i = members.Count - 1; i >= 1; i--)
        {
            GridBot follower = members[i];
            float dist = SpatialUtility.FlatDistance(follower.transform.position, leader.transform.position);
            if (dist > disbandDistance)
            {
                separationTimers.TryGetValue(follower, out float elapsed);
                elapsed += Time.deltaTime;
                separationTimers[follower] = elapsed;

                if (elapsed >= disbandDelay)
                {
                    Debug.Log("[GridBotSquad] " + follower.npcId + " left " + name + " (separated).");
                    RemoveMember(follower, true);
                }
            }
            else
            {
                separationTimers[follower] = 0f;
            }
        }
    }

    void Disband()
    {
        for (int i = members.Count - 1; i >= 0; i--)
            RemoveMember(members[i], true);

        Debug.Log("[GridBotSquad] " + name + " disbanded.");
        Destroy(gameObject);
    }

    void RemoveDeadMembers()
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i] == null) members.RemoveAt(i);
        }
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        GridBot leader = Leader;
        if (leader == null) return;

        Gizmos.color = Color.white;
        for (int i = 1; i < members.Count; i++)
        {
            Vector3 slot = SlotPosition(i, CurrentShape);
            Gizmos.DrawLine(leader.transform.position, slot);
            Gizmos.DrawWireSphere(slot, 0.3f);
        }
    }
}
