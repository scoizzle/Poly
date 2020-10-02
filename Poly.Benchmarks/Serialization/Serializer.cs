using System;
using System.Globalization;
using System.Collections.Generic;

using Newtonsoft.Json;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

using Poly.Reflection;


namespace Poly.Serialization.Benchmarks {
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

        public Dictionary<string, int> Dictionary  { get; set; }
    }

    public class SerializationBenchmarks 
    { 
        public static readonly string JsonText = JsonConvert.SerializeObject(test);

        public static readonly TypeInterface typeInterface = TypeInterface<TestClass>.Get();

        public static readonly TestClass test = CreateSerializationObject();

        private static TestClass CreateSerializationObject()
        {
            TestClass test = new TestClass
            {
                BigNumber = 34123123123.121M,
                Now = DateTime.Now.AddHours(1),
                Dictionary = new Dictionary<string, int> { { "Val & asd1", 1 }, { "Val2 & asd1", 3 }, { "Val3 & asd1", 4 } },
                Strings = new List<string>() { null, "Markus egger ]><[, (2nd)", null },
                Address = new Address { Street = "fff Street", Entered = DateTime.Now.AddDays(20) },
                Addresses = new List<Address>
                {
                    new Address { Entered = DateTime.Now.AddDays(-1), Street = "\u001farray\u003caddress" },
                    new Address { Entered = DateTime.Now.AddDays(-2), Street = "array 2 address" }
                }
            };
            return test;
        }

        [Benchmark]
        public void Newtonsoft_Serialize() {
            _ = JsonConvert.SerializeObject(test);
        }

        [Benchmark]
        public void Newtonsoft_Deserialize() {
            _ = JsonConvert.DeserializeObject<TestClass>(JsonText);
        }

        [Benchmark]
        public void SystemTextJson_Serialize() {
            _ = System.Text.Json.JsonSerializer.Serialize<TestClass>(test);
        }

        [Benchmark]
        public void SystemTextJson_Deserialize() {
            _ = System.Text.Json.JsonSerializer.Deserialize<TestClass>(JsonText);
        }


        [Benchmark]
        public void Poly_TypeInterface_Serialize() {
            _ = JsonSerializer.Serialize<TestClass>(test);
        }
        
        [Benchmark]
        public void Poly_TypeInterface_Deserialize() {
            _ = JsonSerializer.Deserialize<TestClass>(JsonText);
        }

        [Benchmark]
        public void Poly_CachedTypeInterface_Serialize()
        {
            var writer = new JsonWriter();
            typeInterface.Serialize(writer, test);
        }

        [Benchmark]
        public void Poly_CachedTypeInterface_Deserialize()
        {
            var reader = new JsonReader(JsonText);
            typeInterface.Deserialize(reader, out _);
        }
    }
}