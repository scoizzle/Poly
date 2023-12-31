using System;
using System.Buffers;
using System.Diagnostics;

using Poly.Parsing;
using Poly.Parsing.Json;
using Poly.Serialization;
using Poly.Reflection;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Diagnostics;

namespace Poly.Parsing.Benchmarks;

[MemoryDiagnoser, MinColumn, MaxColumn]
public class JsonGrammarBenchmarks 
{
    static readonly ITypeInterface typeInterface = TypeInterfaceRegistry.Get<Major>();
    private readonly Consumer consumer = new();

    string test;
    ReadOnlySequence<char> seq;
    ReadOnlyMemory<char> mem;

    [Params(100)]
    public int MinorInstances;

    [GlobalSetup]
    public void Setup()
    {
        test = GetTestString(MinorInstances);
        seq = new ReadOnlySequence<char>(test.AsMemory());
        mem = test.AsMemory();
    }

    // [Benchmark]
    // public void Get_String_As_Memory()
    // {
    //     var _ = test.AsMemory();
    // }

    // [Benchmark]
    // public void Get_String_As_Span()
    // {
    //     var _ = test.AsSpan();
    // }

    // [Benchmark]
    // public void Get_Memory_As_Span()
    // {
    //     var _ = mem.Span;
    // }

    // [Benchmark]
    // public void Allocate_ReadOnlySequence()
    // {
    //     var _ = new ReadOnlySequence<char>(mem);
    // }

    // [Benchmark]
    // public void Allocate_SequenceReader()
    // {
    //     var _ = new SequenceReader<char>(seq);
    // }

    // [Benchmark]
    // public void Activity_StartActivity()
    // {
    //     using var _ = Instrumentation.StartActivity();
    // }

    // [Benchmark]
    // public void Activity_Current_AddEvent()
    // {
    //     using var _ = Instrumentation.AddEvent();
    // }

    [Benchmark]
    public void ITokenReader_Read_All_Tokens()
    {
        var tokenReader = new JsonStringTokenReader(test);

        consumer.Consume(tokenReader);

        Console.WriteLine(tokenReader);
    }

    [Benchmark]
    public void ITokenParser_Parse()
    {
        var tokenReader = new JsonStringTokenReader(test);
        var tokenParser = new JsonStringTokenParser(tokenReader);

        while (tokenParser.TryParseExpression(out _));
    }


    // [Benchmark]
    // public void System_Text_Json() {
    //     System.Text.Json.JsonSerializer.Deserialize<Major>(test);
    // }

    // [Benchmark]
    // public void Poly_Deserialize() {
    //     JsonSerializer.Deserialize<Major>(test);
    // }

    [Benchmark]
    public void Poly_Json_Grammar() {
        var sequence = new ReadOnlySequence<char>(mem);
        var tokens = JsonGrammar.Definition.ParseAllTokens(in sequence);

        consumer.Consume(tokens);
    }

    // [Benchmark]
    // public void Poly_Json_Deserialize() {
    //     var sequence = new ReadOnlySequence<char>(mem);
    //     var reader = new JsonReaderPipelines(sequence);

    //     typeInterface.DeserializeObject(reader, out _);
    // }

    class Minor {
        public bool True { get; set; }
        public float PI { get; set; }
    }

    class Major {
        public Minor[] Test { get; set; }
    }

    static string GetTestString(int minorInstances)
    {
        var minors = Enumerable
            .Range(0, minorInstances)
            .Select(i => new Minor { True = true, PI = MathF.PI })
            .ToArray();

        
        var m = new Major { Test = minors };
        
        return System.Text.Json.JsonSerializer.Serialize(m);
    }
}