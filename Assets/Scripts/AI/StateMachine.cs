using System.Collections.Generic;
using UnityEngine;

// Generic finite state machine runner with elapsed-time tracking and transition logging.
public class StateMachine<TStateId> where TStateId : struct, System.Enum
{
    private readonly Dictionary<TStateId, IAgentState<TStateId>> states = new Dictionary<TStateId, IAgentState<TStateId>>();

    public IAgentState<TStateId> Current { get; private set; }
    public TStateId CurrentId { get; private set; }
    public float StateTime { get; private set; }
    public bool HasStarted { get; private set; }

    public void Register(IAgentState<TStateId> state)
    {
        states[state.Id] = state;
    }

    public void ChangeState(TStateId nextId, bool log = false, string ownerName = "")
    {
        if (log && HasStarted && !EqualityComparer<TStateId>.Default.Equals(CurrentId, nextId))
            Debug.Log(ownerName + ": " + CurrentId + " -> " + nextId);

        if (Current != null) Current.Exit();
        Current = states[nextId];
        CurrentId = nextId;
        StateTime = 0f;
        HasStarted = true;
        Current.Enter();
    }

    public void Tick(float dt)
    {
        StateTime += dt;
        if (Current != null) Current.Tick(dt);
    }

    public void FixedTick(float fixedDt)
    {
        if (Current != null) Current.FixedTick(fixedDt);
    }
}
