namespace Poly.DomainModeling.V2;

public interface IEffect;

public sealed record SetProperty(string PropertyName, string ValueExpression) : IEffect;

public sealed record TransitionStage(string StageName) : IEffect;

public sealed record CreateEntity(string EntityName) : IEffect;

public sealed record InvokeAction(string EntityName, string ActionName) : IEffect;
