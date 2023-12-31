namespace Poly.Data.Tests;

using Xunit;

public class BitArrayTests {
    static readonly byte[] TestArray = { 0xDE, 0xAD, 0xBE, 0xEF };

    [Fact]
    public void FromArray()
    {
        var array = new BitArray(TestArray);

        Assert.Equal(array.BitCapacity, TestArray.Length * 8);
    }

    [Fact]
    public void FromCapacity()
    {
        var array = new BitArray(bitCapacity: 4);

        Assert.Equal(8, array.BitCapacity);
    }

    [Fact]
    public void GetAndSetValue()
    {
        var array = new BitArray(TestArray);

        var bit = array[7];

        array[7] = !bit;

        Assert.NotEqual(bit, array[7]);

        array.Toggle(7);

        Assert.Equal(bit, array[7]);
    }
}