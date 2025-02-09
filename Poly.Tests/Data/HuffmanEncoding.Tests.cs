using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Poly.Data;

public class HuffmanEncodingTests
{
    private static readonly char[] s_DataSet = Enumerable
        .Range(1, 1000)
        .Select(_ => (char)Random.Shared.Next('a', 'z'))
        .ToArray();

    private readonly HuffmanEncoding<int, char>.Encoder s_Encoding = new(s_DataSet);

    private readonly ITestOutputHelper output;

    public HuffmanEncodingTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void EncodeSingleCharacter()
    {
        var index = Random.Shared.Next(0, s_DataSet.Length);
        var character = s_DataSet[index];
        var encoded = s_Encoding.Encode(character);
        var path = encoded.ToList();
        var decoded = s_Encoding.Decode(path);

        Assert.Equal(character, decoded);
        output.WriteLine($"Encoded {character} as {path.Count} bits and decoded as {decoded}");
    }

    [Fact]
    public void EncodeDataSet()
    {
        var encoded = s_Encoding.EncodeSet(s_DataSet).ToList();
        var decoded = s_Encoding.DecodeSet(encoded).ToList();

        Debug.Assert(s_DataSet.SequenceEqual(decoded));
        output.WriteLine($"Encoded {s_DataSet.Length} characters as {MathF.Ceiling(encoded.Count / 8f)} bytes instead of {System.Text.Encoding.UTF8.GetByteCount(s_DataSet)} and decoded as {decoded.Count()} characters");
    }
}