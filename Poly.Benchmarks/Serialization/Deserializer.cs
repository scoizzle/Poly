using System;
using System.Collections.Generic;

using Newtonsoft.Json;

using BenchmarkDotNet.Attributes;

using Poly.Reflection;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Diagnostics;

namespace Poly.Serialization.Benchmarks.Deserializer
{
    public class Address
    {
        public Address() { }

        public string Street { get; set; }

        public string Phone { get; set; }

        public DateTime Entered { get; set; }
    }

    public class TestClass
    {
        public string Name { get; set; }

        public DateTime Now { get; set; }

        public decimal BigNumber { get; set; }

        public Address Address { get; set; }

        public List<Address> Addresses { get; set; }

        public List<string> Strings { get; set; }

        public Dictionary<string, int> Dictionary { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(TestClass))]
    internal partial class DeserializerSourceGenerationContext : JsonSerializerContext
    {
    }

    [MemoryDiagnoser]
    [BaselineColumn]
    public class DeserializationBenchmarks
    {
        public static readonly TestClass test = CreateSerializationObject();
        public static readonly string JsonText = System.Text.Json.JsonSerializer.Serialize(test);

        public static readonly ISystemTypeAdapter<int> integerSystemTypeAdapter = TypeAdapterRegistry.Get<int>();

        public static readonly ISystemTypeAdapter<TestClass> typeInterface = TypeAdapterRegistry.Get<TestClass>();

        private static TestClass CreateSerializationObject()
        {
            var test = new TestClass
            {
                BigNumber = 34123123123.121M,
                Now = DateTime.Now.AddHours(1),
                Dictionary = new Dictionary<string, int> { { "Val & asd1", 1 }, { "Val2 & asd1", 3 }, { "Val3 & asd1", 4 } },
                Strings = new List<string>() { null, "Markus egger ]><[, (2nd)", null },
                Address = new Address { Street = "fff Street", Entered = DateTime.Now.AddDays(20) },
                Addresses = Enumerable
                    .Range(0, 1000)
                    .Select(i => new Address { Entered = DateTime.Now.AddDays(i), Street = $"{i} address" })
                    .ToList()
            };
            return test;
        }

        [Benchmark]
        public void Newtonsoft_Deserialize()
        {
            TestClass result = JsonConvert.DeserializeObject<TestClass>(JsonText);
            Guard.IsNotNull(result);
        }

        [Benchmark(Baseline = true)]
        public void SystemTextJson_Deserialize()
        {
            TestClass result = System.Text.Json.JsonSerializer.Deserialize<TestClass>(JsonText);
            Guard.IsNotNull(result);
        }

        [Benchmark]
        public void SystemTextJson_Deserialize_SourceGeneration()
        {
            TestClass result = System.Text.Json.JsonSerializer.Deserialize(JsonText, DeserializerSourceGenerationContext.Default.TestClass);
            Guard.IsNotNull(result);
        }

        [Benchmark]
        public void Poly_TypeInterface_Deserialize()
        {
            TestClass result = JsonSerializer.Deserialize<TestClass>(JsonText);
            Guard.IsNotNull(result);
        }

        [Benchmark]
        public void Poly_CachedTypeInterface_Deserialize()
        {
            var reader = new JsonReader(JsonText);
            typeInterface.Deserialize(reader, out TestClass result);
            Guard.IsNotNull(result);
        }
    }
}