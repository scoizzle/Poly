using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Poly.Reflection;
using Poly.Serialization;

namespace Poly.Benchmarks.Serialization;

class TestResponse
{
    public long Id { get; set; }
    public string JsonRPC { get; set; }
    public long total { get; set; }
    public List<TestResponseResult> Result { get; set; }
}

class TestResponseResult
{
    public long id { get; set; }
    public Guid guid { get; set; }
    public string picture { get; set; }
    public int age { get; set; }
    public string name { get; set; }
    public string gender { get; set; }
    public string company { get; set; }
    public string phone { get; set; }
    public string email { get; set; }
    public string address { get; set; }
    public string about { get; set; }
    public string registered { get; set; }
    public List<string> tags { get; set; }
    public List<TestResponseResultFriend> Friends { get; set; }
}

public class TestResponseResultFriend
{
    public long Id { get; set; }
    public string Name { get; set; }
}


[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TestResponse))]
internal partial class TestResponseDeserializerSourceGenerationContext : JsonSerializerContext
{
}

[MemoryDiagnoser, BaselineColumn, MinColumn, MaxColumn, MeanColumn]
public class DeserializeFromFileBenchmarks
{
    readonly ISystemTypeAdapter<TestResponse> _typeInterface = TypeAdapterRegistry.Get<TestResponse>();
    readonly Grammar.Ugh ugh = new();
    readonly Consumer _consumer = new Consumer();

    string JsonText { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (File.Exists("input.big.json"))
            JsonText = File.ReadAllText("input.big.json");
    }

    [Benchmark(Baseline = true)]
    public void SystemTextJson_Deserialize()
    {
        TestResponse response = System.Text.Json.JsonSerializer.Deserialize<TestResponse>(JsonText);
        if (response is null)
            throw new Exception("Error");
    }

    [Benchmark]
    public void Poly_CachedTypeInterface_Deserialize()
    {
        var reader = new JsonReader(JsonText);
        if (!_typeInterface.Deserialize(reader, out TestResponse response))
            throw new Exception("Error");
    }

    [Benchmark]
    public void Ugh()
    {
        ugh.GetJsonTokens(JsonText).Consume(_consumer);
    }
}