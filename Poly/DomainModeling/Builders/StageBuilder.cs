using Poly.DomainModeling.Builders;

namespace Poly.DomainModeling;

/// <summary>
/// Mutable builder for constructing a <see cref="Stage"/> during domain assembly.
/// </summary>
public sealed class StageBuilder {
    private readonly string _name;
    private StageReference? _parent;
    private readonly List<Action> _actions = new();
    private readonly List<Policy> _policies = new();
    private readonly List<Effect> _onEntryEffects = new();
    private readonly List<Effect> _onExitEffects = new();

    internal StageBuilder(string name) {
        _name = Guard.ThrowIfNullOrEmpty(name);
    }

    public StageBuilder Parent(string stageName) {
        _parent = new StageReference(Guard.ThrowIfNullOrEmpty(stageName));
        return this;
    }

    public StageBuilder Policy(string name, DomainExpression expression) {
        _policies.Add(new Policy(Guard.ThrowIfNullOrEmpty(name), expression));
        return this;
    }

    public StageBuilder Policy(Policy policy) {
        _policies.Add(policy);
        return this;
    }

    /// <summary>
    /// Adds a stage invariant / guard. This is a semantic alias for Policy to stay closer
    /// to the original Ugh sketch's .Requires(...) style.
    /// </summary>
    public StageBuilder Requires(DomainExpression expression) {
        // We give it a generated name for now. In a fuller version we could support named requires.
        _policies.Add(new Policy($"Guard_{_policies.Count + 1}", expression));
        return this;
    }

    public StageBuilder Requires(DomainExpression expression, string name) {
        _policies.Add(new Policy(Guard.ThrowIfNullOrEmpty(name), expression));
        return this;
    }

    public ActionBuilder Action(string name) {
        return new ActionBuilder(Guard.ThrowIfNullOrEmpty(name));
    }

    public StageBuilder Action(string name, Action<ActionBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var actionBuilder = Action(name);
        configure(actionBuilder);
        _actions.Add(actionBuilder.Build());
        return this;
    }

    public StageBuilder OnEntry(Effect effect) {
        _onEntryEffects.Add(effect);
        return this;
    }

    public StageBuilder OnExit(Effect effect) {
        _onExitEffects.Add(effect);
        return this;
    }

    public StageBuilder OnEntryPublish(string eventName, Action<PublishEventBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var publishBuilder = new PublishEventBuilder(Guard.ThrowIfNullOrEmpty(eventName));
        configure(publishBuilder);
        _onEntryEffects.Add(publishBuilder.Build());
        return this;
    }

    /// <summary>
    /// Supports a style closer to the original sketch: .OnEntry(Publish("Event", p => p.Bind(...)))
    /// </summary>
    public StageBuilder OnEntry(Action<OnEntryBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var onEntryBuilder = new OnEntryBuilder();
        configure(onEntryBuilder);
        _onEntryEffects.AddRange(onEntryBuilder.BuildEffects());
        return this;
    }

    internal Stage Build() {
        return new Stage(
            _name,
            _parent,
            _actions,
            _policies,
            _onEntryEffects,
            _onExitEffects
        );
    }
}