namespace Poly.DomainModeling.Runtime;

/// <summary>
/// A domain rule failed where no <see cref="DomainResult"/> can be returned:
/// constructors, stage entry and exit, and subscription handlers. Printed C# and
/// the simulator throw the same type with the same message. Host fail-loud errors
/// stay plain <see cref="InvalidOperationException"/>s, so a caller can tell them apart.
/// </summary>
public sealed class DomainFailureException(string message) : InvalidOperationException(message);
