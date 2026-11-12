using Poly.Ast.Nodes;
using Poly.Runtime;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// The only AST-facing constructor of a typed failure. Lowering never builds a
/// <c>DomainResult.Failure("…")</c> string by hand: it asks this seam for the
/// <c>Poly.Runtime.DomainFailure.&lt;Kind&gt;(message[, path[, guard]])</c> call, so the kind,
/// the element path and the guard travel with the message.
/// The receiver is a <see cref="ClrTypeReference"/>, so it prints fully qualified and
/// needs no using or reserved-name row. Trailing null arguments are omitted: the VM and
/// printed C# both fill the optional parameters.
/// </summary>
internal static class FailureLowering {
    /// <summary>One failure before it is placed in a body. <see cref="Kind"/> is the closed <c>Poly.Runtime</c> vocabulary.</summary>
    internal readonly record struct Spec(
        DomainFailureKind Kind, Node Message, string? ElementPath = null, string? Guard = null);

    /// <summary><c>Poly.Runtime.DomainFailure.&lt;Kind&gt;(message[, path[, guard]])</c>.</summary>
    internal static Node Call(Spec spec) {
        var factory = new Member(new ClrTypeReference(typeof(DomainFailure)), spec.Kind.ToString());
        if (spec.Guard is not null)
            return new Invoke(factory, spec.Message, new Constant(spec.ElementPath), new Constant(spec.Guard));
        if (spec.ElementPath is not null)
            return new Invoke(factory, spec.Message, new Constant(spec.ElementPath));
        return new Invoke(factory, spec.Message);
    }

    /// <summary><c>return resultType.Failure(&lt;call&gt;)</c>.</summary>
    internal static Return Return(Node resultType, Spec spec) =>
        new(new Invoke(new Member(resultType, "Failure"), Call(spec)));

    /// <summary>
    /// <c>return resultType.Failure(child.Error!)</c>, or <c>child.Error!.At(prefix)</c> when
    /// <paramref name="callerPath"/> is given: kind, message and guard survive the rewrap.
    /// </summary>
    internal static Return Propagate(Node resultType, Node resultVar, string? callerPath = null) {
        Node error = new NullForgiving(new Member(resultVar, "Error"));
        if (callerPath is not null)
            error = new Invoke(new Member(error, "At"), new Constant(callerPath));
        return new(new Invoke(new Member(resultType, "Failure"), error));
    }

    /// <summary>
    /// <c>throw new ConstraintFailureException(&lt;message&gt;)</c>, for void export bodies only
    /// (constructor, OnEntry, handler). The exception carries text, so the kind is dropped
    /// here; one call per site keeps the throw's removal a one-line diff.
    /// </summary>
    internal static Node Throw(Spec spec) => ThrowMessage(spec.Message);

    /// <summary><c>throw new ConstraintFailureException(child.ErrorMessage ?? "")</c>: a callee's failure in a void body.</summary>
    internal static Node ThrowFrom(Node resultVar) =>
        ThrowMessage(new Coalesce(new Member(resultVar, "ErrorMessage"), new Constant("")));

    private static ThrowStatement ThrowMessage(Node message) =>
        new(new New(new NamedTypeReference("ConstraintFailureException"), message));
}