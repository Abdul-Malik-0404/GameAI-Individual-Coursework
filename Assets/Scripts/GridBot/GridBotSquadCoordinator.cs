using System.Collections.Generic;
using UnityEngine;

// Dynamically forms and disbands GridBotSquads via proximity+LOS patrol links and local distress calls.
public class GridBotSquadCoordinator : MonoBehaviour
{
    public static GridBotSquadCoordinator Instance { get; private set; }

    [Header("Proximity Patrol Grouping")]
    public float joinRadius = 8f;
    public float checkInterval = 0.5f;
    public int maxSquadSize = 3;

    [Header("Local Distress Rally")]
    public float rallyRadius = 12f;
    [Range(0f, 1f)] public float rallyWallMuffling = 0.5f;

    [Header("Dynamic Squad Defaults")]
    public GridBotSquad.FormationShape defaultShape = GridBotSquad.FormationShape.Wedge;
    public float disbandDistance = 16f;
    public float disbandDelay = 5f;
    public float rejoinCooldown = 6f;

    private readonly List<GridBot> bots = new List<GridBot>();
    private float checkTimer;
    private int squadCounter;

    // Ensures a coordinator exists even if none was placed in the scene.
    public static GridBotSquadCoordinator EnsureInstance()
    {
        if (Instance != null) return Instance;
        GridBotSquadCoordinator existing = FindFirstObjectByType<GridBotSquadCoordinator>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }
        GameObject go = new GameObject("GridBotSquadCoordinator");
        return go.AddComponent<GridBotSquadCoordinator>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Register(GridBot bot)
    {
        if (bot != null && !bots.Contains(bot)) bots.Add(bot);
    }

    public void Unregister(GridBot bot)
    {
        bots.Remove(bot);
    }

    void Update()
    {
        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        checkTimer = checkInterval;

        PruneNullBots();
        CheckProximityGrouping();
    }

    // Forms or expands a squad when two calm bots meet within joinRadius with clear line of sight.
    void CheckProximityGrouping()
    {
        for (int i = 0; i < bots.Count; i++)
        {
            GridBot a = bots[i];
            if (!IsEligibleForProximity(a)) continue;

            for (int j = i + 1; j < bots.Count; j++)
            {
                GridBot b = bots[j];
                if (!IsEligibleForProximity(b)) continue;
                if (a.squad != null && b.squad != null) continue;

                if (SpatialUtility.FlatDistance(a.transform.position, b.transform.position) > joinRadius) continue;
                if (!a.Perception.HasLineOfSight(a.transform.position, b.transform.position)) continue;

                if (a.squad == null && b.squad == null)
                {
                    GridBot leader = a.currentHealth >= b.currentHealth ? a : b;
                    GridBot follower = leader == a ? b : a;
                    CreateDynamicSquad(leader, follower, "proximity");
                }
                else
                {
                    GridBotSquad squad = a.squad != null ? a.squad : b.squad;
                    GridBot joiner = a.squad == null ? a : b;
                    if (squad.members.Count < maxSquadSize)
                    {
                        squad.AddMember(joiner);
                        Debug.Log("[GridBotSquad] " + joiner.npcId + " joined " + squad.name + " (proximity).");
                    }
                }
            }
        }
    }

    // Rallies nearby calm solo bots within rallyRadius (muffled by walls) when a bot enters combat or takes damage.
    public void BroadcastDistressCall(GridBot caller, Vector3 threatPosition)
    {
        if (caller == null || !caller.IsReady || caller.IsOnRejoinCooldown) return;
        PruneNullBots();

        foreach (GridBot candidate in bots)
        {
            if (candidate == caller || !candidate.IsReady || candidate.IsOnRejoinCooldown) continue;
            if (candidate.squad != null) continue;
            if (candidate.currentState != GridBot.BotState.Patrol &&
                candidate.currentState != GridBot.BotState.Investigate) continue;

            float range = rallyRadius;
            if (!caller.Perception.HasLineOfSight(caller.transform.position, candidate.transform.position))
                range *= rallyWallMuffling;

            if (SpatialUtility.FlatDistance(caller.transform.position, candidate.transform.position) > range) continue;

            if (caller.squad == null)
            {
                CreateDynamicSquad(caller, candidate, "distress rally");
            }
            else if (caller.squad.members.Count < maxSquadSize)
            {
                caller.squad.AddMember(candidate);
                Debug.Log("[GridBotSquad] " + candidate.npcId + " rallied to " + caller.squad.name + ".");
            }
            else
            {
                break;
            }

            candidate.HearSquadAlert(threatPosition);
        }
    }

    void CreateDynamicSquad(GridBot leader, GridBot follower, string reason)
    {
        squadCounter++;
        GameObject go = new GameObject("DynamicSquad " + squadCounter);
        GridBotSquad squad = go.AddComponent<GridBotSquad>();
        squad.shape = defaultShape;
        squad.isDynamic = true;
        squad.disbandDistance = disbandDistance;
        squad.disbandDelay = disbandDelay;
        squad.rejoinCooldown = rejoinCooldown;
        squad.AddMember(leader);
        squad.AddMember(follower);

        Debug.Log("[GridBotSquad] " + leader.npcId + " & " + follower.npcId +
                  " formed " + squad.name + " (" + reason + ").");
    }

    bool IsEligibleForProximity(GridBot bot)
    {
        if (bot == null || !bot.IsReady || bot.IsOnRejoinCooldown) return false;
        if (bot.squad != null && bot.squad.members.Count >= maxSquadSize) return false;
        return bot.currentState == GridBot.BotState.Patrol || bot.currentState == GridBot.BotState.Investigate;
    }

    void PruneNullBots()
    {
        for (int i = bots.Count - 1; i >= 0; i--)
        {
            if (bots[i] == null) bots.RemoveAt(i);
        }
    }
}
