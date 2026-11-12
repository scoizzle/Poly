using System.Reflection;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// L-22 (#604): generated C# honours DSL visibility, so an unmarked entity, action or navigation prints
/// <c>internal</c>. A reflection oracle over a printed assembly looks members up with these flags, which
/// includes non-public ones; the oracle's assertions are unchanged.
/// </summary>
internal static class PrintedMembers {
    public const BindingFlags Any =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
}