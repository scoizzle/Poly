namespace Poly.Data.Tests;

public class BitArrayTests
{
    static readonly byte[] TestArray = { 0xDE, 0xAD, 0xBE, 0xEF };

    [Test]
    public async Task FromArray()
    {
        var array = new BitArray(TestArray);

        await Assert.That(array.BitCapacity).EqualTo(TestArray.Length * 8);
    }

    [Test]
    public async Task FromCapacity()
    {
        var array = new BitArray(bitCapacity: 4);

        await Assert.That(array.BitCapacity).EqualTo(8);
    }

    [Test]
    public async Task GetAndSetValue()
    {
        var array = new BitArray(TestArray);

        var bit = array[7];

        array[7] = !bit;

        await Assert.That(array[7]).IsNotEqualTo(bit);

        array.Toggle(7);

        await Assert.That(array[7]).EqualTo(bit);
    }
}