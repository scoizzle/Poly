using System.Text;

namespace Poly.Tests.TestHelpers;

/// <summary>Routes µop trace output to <c>Console.Error</c> (stderr),
/// which TUnit captures and displays per-test.  Active in all build
/// configurations — Debug and Release.</summary>
public sealed class TestTraceWriter : TextWriter {
    public override void WriteLine(string? value) => Console.Error.WriteLine(value);
    public override void Write(string? value) => Console.Error.Write(value);
    public override Encoding Encoding => Encoding.Unicode;
}