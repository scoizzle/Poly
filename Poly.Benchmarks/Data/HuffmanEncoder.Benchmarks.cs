using System;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Poly.Data;

namespace Poly.Benchmarks.Data.HuffmanEncoderBenchmarks;

[MinColumn, MaxColumn, MeanColumn]
public class EncodingPerformance
{
    static readonly char[] s_DataSet = Enumerable
        .Range(1, 160_000)
        .Select(_ => (char)Random.Shared.Next('a', 'z'))
        .ToArray();

    static readonly HuffmanEncoding<int, char>.Encoder s_Encoding = new(s_DataSet);

    static readonly bool[] s_EncodedDataSet = s_Encoding.EncodeSet(s_DataSet).ToArray();

    static readonly Consumer s_Consumer = new();

    static readonly char m_TargetCharacter = s_DataSet[Random.Shared.Next(0, s_DataSet.Length)];
    static readonly bool[] m_TargetCharacterEncoded = s_Encoding.Encode(m_TargetCharacter).ToArray();

    [Benchmark]
    public void EncodeSingleCharacter()
    {
        s_Encoding.Encode(m_TargetCharacter).Consume(s_Consumer);
    }

    [Benchmark]
    public void DecodeSingleCharacter()
    {
        s_Encoding.Decode(m_TargetCharacterEncoded);
    }

    [Benchmark]
    public void EncodeEntireDataSet()
    {
        s_Encoding.EncodeSet(s_DataSet).Consume(s_Consumer);
    }


    [Benchmark]
    public void DecodeEntireDataSet()
    {
        s_Encoding.DecodeSet(s_EncodedDataSet).Consume(s_Consumer);
    }
}
