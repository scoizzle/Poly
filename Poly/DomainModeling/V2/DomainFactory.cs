namespace Poly.DomainModeling.V2;

public static class DomainFactory {
    public static Domain Create(string name, global::System.Action<DomainDsl> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DomainBuilder(name);
        configure(new DomainDsl(builder));
        var domain = builder.Build();
        var validation = DomainValidator.Validate(domain);
        if (!validation.IsValid) {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Issues.Select(issue => issue.Message)));
        }

        return domain;
    }
}

public sealed class DomainDsl {
    private readonly DomainBuilder _builder;

    internal DomainDsl(DomainBuilder builder)
    {
        _builder = builder;
    }

    public DomainDsl Entity(string name, global::System.Action<EntityDsl> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        _builder.Entity(name, entityBuilder => configure(new EntityDsl(entityBuilder)));
        return this;
    }

    public DomainDsl Relationship(string name, string sourceEntity, string targetEntity, RelationshipKind kind)
    {
        _builder.Relationship(name, sourceEntity, targetEntity, kind);
        return this;
    }
}

public sealed class EntityDsl {
    private readonly EntityBuilder _builder;

    internal EntityDsl(EntityBuilder builder)
    {
        _builder = builder;
    }

    public EntityDsl Property(string name, string type, bool isRequired = false, string? defaultValue = null)
    {
        _builder.Property(name, type, isRequired, defaultValue);
        return this;
    }

    public EntityDsl Stage(string name, bool isInitial = false)
    {
        _builder.Stage(name, isInitial);
        return this;
    }

    public EntityDsl Action(string name, global::System.Action<ActionDsl> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _builder.Action(name, actionBuilder => configure(new ActionDsl(actionBuilder)));
        return this;
    }
}

public sealed class ActionDsl {
    private readonly ActionBuilder _builder;

    internal ActionDsl(ActionBuilder builder)
    {
        _builder = builder;
    }

    public ActionDsl Parameter(string name, string type, bool isRequired = true, string? defaultValue = null)
    {
        _builder.Parameter(name, type, isRequired, defaultValue);
        return this;
    }

    public ActionDsl Effect(IEffect effect)
    {
        _builder.Effect(effect);
        return this;
    }
}
