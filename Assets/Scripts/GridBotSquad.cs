using System.Collections.Generic;
using UnityEngine;

// Coordinates a leader-follower GridBot squad with formations and shared alerts.
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
        CurrentShape = ChooseShape();
    }

    public Vector3 GetSlotPosition(GridBot member)
    {
        RemoveDeadMembers();
        return SlotPosition(members.IndexOf(member), CurrentShape);
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
