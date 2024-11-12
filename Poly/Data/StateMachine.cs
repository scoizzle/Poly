using CommunityToolkit.HighPerformance;

namespace Poly.Stupid;

public sealed class StateMachineConfigurationBuilder<TState, TTrigger>
    where TTrigger : notnull;

public interface IStateConfigurationBuilder<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
{
    public IStateConfigurationBuilder<TState, TTrigger> Permit(TTrigger trigger, TState destinationState);
}

public interface IStateMachineConfigurationBuilder<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
{

}

public sealed class AbstractStateMachine<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
{
    private sealed class TriggerConfiguration();
    private sealed class StateConfiguration();

    private sealed class TriggerConfigurationBuilder()
    {
        private Action<TState>? _mutateState;

        public IStateConfigurationBuilder<TState, TTrigger> OnEntry(TState state)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class StateConfigurationBuilder() : IStateConfigurationBuilder<TState, TTrigger>
    {
        readonly Dictionary<TTrigger, TriggerConfigurationBuilder> _triggerConfigurations = new();

        private TriggerConfigurationBuilder GetTriggerConfigurationBuilder(TTrigger trigger)
        {
            if (!_triggerConfigurations.TryGetValue(trigger, out var triggerConfigurationBuilder))
            {
                _triggerConfigurations[trigger] = triggerConfigurationBuilder = new();
            }
            return triggerConfigurationBuilder;
        }

        public IStateConfigurationBuilder<TState, TTrigger> Permit(TTrigger trigger, TState destinationState)
        {
            TriggerConfigurationBuilder triggerConfigurationBuilder = GetTriggerConfigurationBuilder(trigger);
            return this;
        }
    }

    private sealed class ConfigurationBuilder
    {
        readonly Dictionary<TState, StateConfigurationBuilder> _stateConfigurations = new();

        public IStateConfigurationBuilder<TState, TTrigger> Configure(TState state)
        {
            if (!_stateConfigurations.TryGetValue(state, out var stateConfigurationBuilder))
            {
                _stateConfigurations[state] = stateConfigurationBuilder = new();
            }

            return stateConfigurationBuilder;
        }
    }
}