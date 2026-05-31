// Experimental spike: Fluent + Ergonomic Evolution API
// Goal: Make the transactional Evolution layer feel as natural and expressive
// as the current DomainBuilder, so that for agent-driven work we may not need
// a separate one-shot builder API at all.
//
// This still goes through the full Evolution machinery:
// - Change recording
// - Analysis gate on Commit()
// - Rich EvolutionTrace
// - Automatic rollback on analysis errors

namespace Poly.DomainModeling.Evolution.Spike;

/*
// Example usage that feels builder-like but is change-oriented:

// Evolving an existing domain
var result = currentDomain.Evolve()
    .AddPrimitive("string", TypeCategory.Text)
    .AddEntity("Order")
        .WithProperty("Id", "string")
        .WithProperty("Status", "OrderStatus")
        .AddAction("PlaceOrder")
            .WithParameter("CustomerId", "string")
            .WithEffect(e => e
                .Create("OrderItem")
                .Set("Quantity", Parameter("qty")))
    .WithPolicy("Order", "StatusMustBeValid")
    .Apply();

// Creating a new domain from scratch (special cased for ergonomics)
var newDomain = Domain.EvolveFromScratch("ECommerce")
    .AddPrimitive("string", TypeCategory.Text)
    .AddEntity("Customer")
        .WithProperty("Email", "string")
        .AddAction("Register")
    .Apply();
*/

// =====================================================
// Fluent Evolution API Sketch
// =====================================================

public static class EvolutionExtensions
{
    public static FluentEvolutionBuilder Evolve(this Domain current)
        => new FluentEvolutionBuilder(current);

    public static FluentEvolutionBuilder EvolveFromScratch(string name)
        => new FluentEvolutionBuilder(null, name);
}

public sealed class FluentEvolutionBuilder
{
    private readonly Domain? _base;
    private readonly string? _domainName;
    private readonly List<DomainChange> _changes = new();

    internal FluentEvolutionBuilder(Domain? baseDomain, string? newName = null)
    {
        _base = baseDomain;
        _domainName = newName;
    }

    // --- Domain level ---

    public FluentEvolutionBuilder AddPrimitive(string name, TypeCategory category)
    {
        _changes.Add(new AddPrimitiveChange(name, category));
        return this;
    }

    public FluentEntityBuilder AddEntity(string name)
    {
        return new FluentEntityBuilder(this, name);
    }

    public FluentEvolutionBuilder WithPolicy(string targetEntity, string policyName)
    {
        // Simplified – real version would take a DomainExpression
        _changes.Add(new AddPolicyChange(targetEntity, policyName));
        return this;
    }

    public EvolutionResult Apply(AnalysisResult? priorAnalysis = null)
    {
        // In real code this would delegate to DomainEvolution.Evolve() builder
        // or directly to the Apply path.
        throw new NotImplementedException("Spike");
    }
}

// =====================================================
// Entity level
// =====================================================

public sealed class FluentEntityBuilder
{
    private readonly FluentEvolutionBuilder _root;
    private readonly string _name;
    private readonly List<object> _entityChanges = new(); // placeholder for properties, actions, etc.

    internal FluentEntityBuilder(FluentEvolutionBuilder root, string name)
    {
        _root = root;
        _name = name;
    }

    public FluentEntityBuilder WithProperty(string name, string typeName)
    {
        _entityChanges.Add(new PropertySpec(name, typeName));
        return this;
    }

    public FluentActionBuilder AddAction(string name)
    {
        return new FluentActionBuilder(this, name);
    }

    public FluentEvolutionBuilder And() => _root;
}

// =====================================================
// Action level
// =====================================================

public sealed class FluentActionBuilder
{
    private readonly FluentEntityBuilder _entity;
    private readonly string _name;

    internal FluentActionBuilder(FluentEntityBuilder entity, string name)
    {
        _entity = entity;
        _name = name;
    }

    public FluentActionBuilder WithParameter(string name, string typeName)
    {
        // accumulate
        return this;
    }

    public FluentActionBuilder WithEffect(Action<FluentEffectBuilder> configure)
    {
        var effectBuilder = new FluentEffectBuilder();
        configure(effectBuilder);
        // accumulate effect change
        return this;
    }

    public FluentEntityBuilder And() => _entity;
}

// =====================================================
// Effect configuration (very rough)
// =====================================================

public sealed class FluentEffectBuilder
{
    public FluentEffectBuilder Create(string entityType)
    {
        // queue a CreateEntityInstance effect
        return this;
    }

    public FluentEffectBuilder Set(string property, object value)
    {
        // for now accept literals or Parameter references
        return this;
    }

    // Publish, StageTransition, etc. would follow similar patterns
}

// Supporting tiny types for the spike only
public record PropertySpec(string Name, string TypeName);
public record AddPrimitiveChange(string Name, TypeCategory Category) : DomainChange;
public record AddPolicyChange(string TargetEntity, string PolicyName) : DomainChange;

// =====================================================
// Realistic(ish) Example — Partial ECommerce Domain
// =====================================================

/*
// This is roughly what a slice of ECommerceDomain.cs could look like
// if written against a fluent Evolution API instead of the old V2 mutation style
// or the current DomainBuilder.

var result = currentDomain.Evolve()
    .AddPrimitive("string", TypeCategory.Text)
    .AddPrimitive("int", TypeCategory.Integer)
    .AddPrimitive("decimal", TypeCategory.HighPrecision)
    .AddPrimitive("instant", TypeCategory.Instant)

    .AddEntity("User")
        .WithProperty("Username", "string")
        .WithProperty("Email", "string")
        .WithProperty("IsActive", "bool")

    .AddEntity("Order")
        .WithProperty("OrderId", "string")
        .WithProperty("OrderDate", "instant")
        .WithProperty("TotalAmount", "decimal")
        .AddAction("PlaceOrder")
            .WithParameter("CustomerId", "string")

    .AddEntity("Payment")
        .WithProperty("Amount", "decimal")
        .WithProperty("PaymentDate", "instant")

    .AddStage("Order", "Pending")
    .AddStage("Order", "Paid")          // real version would support parent
    .AddStage("Order", "Shipped")

    .Apply();
*/
