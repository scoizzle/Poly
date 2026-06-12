using System.Diagnostics;
using System.Text;

namespace Poly.Tests.TestHelpers;

/// <summary>Routes writes to <see cref="Debug.Write(string)"/>.
/// In Debug builds, output goes to debug listeners (captured by TUnit).
/// In Release builds, calls are elided by <c>[Conditional]</c> on Debug
/// methods, making the body a no-op — ~1 ns virtual call overhead.</summary>
public sealed class DebugTextWriter : TextWriter {
    public override void WriteLine(string? value) => Debug.WriteLine(value);
    public override void Write(string? value) => Debug.Write(value);
    public override Encoding Encoding => Encoding.Unicode;
}