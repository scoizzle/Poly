using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.DomainModeling.V2;

/// <summary>
/// Internal fluent builder for constructing <see cref="DomainSession"/> instances in tests.
/// Bypasses the <see cref="DomainSessionStore"/> lifecycle for isolated unit testing.
/// </summary>
internal sealed class DomainSessionBuilder {
    private string _domainName = "Test Domain";
    private bool _seedBuiltIns = true;
    private readonly List<DomainMutationIntent> _intents = [];

    internal DomainSessionBuilder WithName(string name) {
        _domainName = name;
        return this;
    }

    internal DomainSessionBuilder WithoutBuiltIns() {
        _seedBuiltIns = false;
        return this;
    }

    internal DomainSessionBuilder WithIntent(DomainMutationIntent intent) {
        _intents.Add(intent);
        return this;
    }

    internal DomainSessionBuilder WithEntity(string name, string? parentName = null) =>
        WithIntent(new AddEntityTypeIntent(name, parentName is null ? null : new DomainNodeReference(parentName)));

    internal DomainSessionBuilder WithPrimitive(string name, TypeCategory category) =>
        WithIntent(new AddPrimitiveTypeIntent(name, category));

    internal DomainSessionBuilder WithProperty(string entityName, string propertyName, string typeName) =>
        WithIntent(new AddPropertyToEntityIntent(entityName, propertyName, typeName));

    internal DomainSessionBuilder WithStage(string entityName, string stageName, string? parentStageName = null) =>
        WithIntent(new AddStageToEntityIntent(entityName, stageName, parentStageName));

    internal DomainSessionBuilder WithAction(string entityName, string actionName) =>
        WithIntent(new AddActionToEntityIntent(entityName, actionName));

    internal DomainSession Build() {
        var domain = new Domain(_domainName);
        var analyzer = new DomainModelAnalyzer();

        if (_seedBuiltIns) {
            var bootstrap = domain.CreateMutation(analyzer);
            CanonicalBuiltInTypeCatalog.AddToMutation(bootstrap);
            bootstrap.Apply(preMutationAnalysis: null);
        }

        var session = new DomainSession(domain, analyzer, initialAnalysis: null, initialRevision: 0);

        // Apply each intent individually to ensure referenced objects exist before they are used
        foreach (var intent in _intents) {
            session.Apply(intent);
        }

        return session;
    }
}
