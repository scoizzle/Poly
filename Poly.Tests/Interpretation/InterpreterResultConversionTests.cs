using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// <see cref="InterpreterResult.GetValue{T}"/> conversions from the VM's uniform
/// <c>long</c> representation that the VM pipeline does not reach on its own
/// (ushort/double/float targets over a boxed long).
/// </summary>
public class InterpreterResultConversionTests {
    [Test]
    public async Task GetValue_LongAsUShort_Converts() {
        await Assert.That(InterpreterResult.FromValue(5L).GetValue<ushort>()).IsEqualTo((ushort)5);
    }

    [Test]
    public async Task GetValue_LongAsDouble_ReinterpretsIeeeBits() {
        var expected = BitConverter.Int64BitsToDouble(5L);
        await Assert.That(InterpreterResult.FromValue(5L).GetValue<double>()).IsEqualTo(expected);
    }

    [Test]
    public async Task GetValue_LongAsFloat_ReinterpretsIeeeBits() {
        var expected = (float)BitConverter.Int64BitsToDouble(5L);
        await Assert.That(InterpreterResult.FromValue(5L).GetValue<float>()).IsEqualTo(expected);
    }
}