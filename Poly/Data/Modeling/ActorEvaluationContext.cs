namespace Poly.Data.Modeling;

/// <summary>
/// Provides the runtime actor node to the policy lowering engine.
/// The host supplies this context so that actor-aware rules (<see cref="ActorTypeRule"/>,
/// <see cref="ActorRoleRule"/>, <see cref="ActorPropertyRule"/>) can be lowered into
/// executable AST expressions.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ActorSubject"/> is a Poly.Syntax <see cref="Node"/> that resolves to the
/// current principal at evaluation time — typically a <see cref="Poly.Syntax.Nodes.Member"/>
/// off a generated runtime context parameter. <see cref="ExecutionContext"/> resolves to that
/// runtime context so actor-role checks can target runtime helpers rather than assuming members
/// exist on every actor type.
/// </para>
/// <para>
/// Actor-rule lowering conventions:
/// <list type="bullet">
///   <item><see cref="ActorTypeRule"/> lowers to <c>TypeIs(actorSubject, TypeReference(actorType.Name))</c>.</item>
///   <item><see cref="ActorRoleRule"/> lowers to <c>Invoke(Member(executionContext, "IsInRole"), Constant(role))</c>.</item>
///   <item><see cref="ActorPropertyRule"/> lowers to a constraint expression over <c>Member(actorSubject, property.Name)</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed record ActorEvaluationContext(Node ActorSubject, Node ExecutionContext);