using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Builders;

/// <summary>
/// Mutable builder for constructing an <see cref="Action"/>.
/// </summary>
public sealed class ActionBuilder {
    private readonly string _name;
    private readonly List<Property> _parameters = new();
    private readonly List<Effect> _effects = new();
    private readonly List<Policy> _policies = new();
    private InvocationResult? _result;

    internal ActionBuilder(string name) {
        _name = Guard.ThrowIfNullOrEmpty(name);
    }

    public ActionBuilder Parameter(string name, string typeName) {
        _parameters.Add(new Property(
            Guard.ThrowIfNullOrEmpty(name),
            new DomainTypeReference(Guard.ThrowIfNullOrEmpty(typeName)),
            []
        ));
        return this;
    }

    public ActionBuilder Result(string name, string typeName) {
        // Simple single-result support for now
        _result = new InvocationResult([
            new InvocationResult.Member(
                Guard.ThrowIfNullOrEmpty(name),
                new DomainTypeReference(Guard.ThrowIfNullOrEmpty(typeName)),
                []
            )
        ]);
        return this;
    }

    public ActionBuilder Effect(Effect effect) {
        _effects.Add(effect);
        return this;
    }

    /// <summary>
    /// Adds a stage transition effect.
    /// </summary>
    public ActionBuilder TransitionTo(string stageName) {
        _effects.Add(new StageTransitionEffect(new StageReference(Guard.ThrowIfNullOrEmpty(stageName))));
        return this;
    }

    /// <summary>
    /// Adds a create instance effect with property initializers.
    /// </summary>
    public ActionBuilder Create(string typeName, Action<CreateEffectBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var createBuilder = new CreateEffectBuilder(Guard.ThrowIfNullOrEmpty(typeName));
        configure(createBuilder);
        _effects.Add(createBuilder.Build());
        return this;
    }

    /// <summary>
    /// Adds a publish event effect.
    /// </summary>
    public ActionBuilder Publish(string eventName, Action<PublishEventBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var publishBuilder = new PublishEventBuilder(Guard.ThrowIfNullOrEmpty(eventName));
        configure(publishBuilder);
        _effects.Add(publishBuilder.Build());
        return this;
    }

    public ActionBuilder Policy(string name, DomainExpression expression) {
        _policies.Add(new Policy(Guard.ThrowIfNullOrEmpty(name), expression));
        return this;
    }

    internal Action Build() {
        return new Action(
            _name,
            _result ?? new InvocationResult([]),
            _parameters,
            _effects,
            _policies
        );
    }
}