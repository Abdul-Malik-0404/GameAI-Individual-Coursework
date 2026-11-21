// State Pattern contract for modular NPC FSM states.
public interface IAgentState<TStateId> where TStateId : struct, System.Enum
{
    TStateId Id { get; }
    void Enter();
    void Tick(float dt);
    void FixedTick(float fixedDt);
    void Exit();
}

// Convenience base class giving states access to their owning agent.
public abstract class AgentState<TOwner, TStateId> : IAgentState<TStateId>
    where TStateId : struct, System.Enum
{
    protected readonly TOwner owner;

    public abstract TStateId Id { get; }

    protected AgentState(TOwner owner)
    {
        this.owner = owner;
    }

    public virtual void Enter() { }
    public abstract void Tick(float dt);
    public virtual void FixedTick(float fixedDt) { }
    public virtual void Exit() { }
}
