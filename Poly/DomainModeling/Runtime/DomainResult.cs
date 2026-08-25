namespace Poly.DomainModeling.Runtime;

/// <summary>
/// VM target for the shared linked-target guard
/// <c>Return(Invoke(Member(TypeReference("DomainResult"), "Failure"), message))</c>.
/// Generated C# has its own <c>DomainResult</c> that returns a result object;
/// this type throws so the same tree is fail-closed when the VM runs it.
/// Same tree, different <c>DomainResult</c> — like Notify on This.
/// </summary>
public static class DomainResult {
    public static object Failure(string message) =>
        throw new InvalidOperationException(message);
}