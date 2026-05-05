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
/// current principal at evaluation time — typically a <see cref="Poly.Syntax.Nodes.Variable"/>
/// or <see cref="Poly.Syntax.Nodes.Parameter"/> node that the host binds before execution.
/// </para>
/// <para>
/// Actor-rule lowering conventions:
/// <list type="bullet">
///   <item><see cref="ActorTypeRule"/> lowers to <c>TypeIs(actorSubject, TypeReference(actorType.Name))</c>.</item>
///   <item><see cref="ActorRoleRule"/> lowers to <c>Invoke(Member(actorSubject, "IsInRole"), Constant(role))</c>.
///     The host must expose an <c>IsInRole(string role)</c> member on the actor subject.</item>
///   <item><see cref="ActorPropertyRule"/> lowers to a constraint expression over <c>Member(actorSubject, property.Name)</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed record ActorEvaluationContext(Node ActorSubject);