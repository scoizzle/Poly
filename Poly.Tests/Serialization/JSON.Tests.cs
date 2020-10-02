using System;
using System.Collections.Generic;

using Xunit;
using Xunit.Abstractions;

using Poly.Reflection;

namespace Poly.Serialization.JSON
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

    public class JsonSerializationTests
    {
        private readonly ITestOutputHelper output;

        public static readonly TypeInterface<TestClass> typeInterface = TypeInterface<TestClass>.Get();

        public static readonly TestClass test = CreateSerializationObject();

        public static readonly string JsonText = Newtonsoft.Json.JsonConvert.SerializeObject(test);


        public JsonSerializationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

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

        [Fact]
        public void Serialize()
        {
            var result = JsonSerializer.Serialize<TestClass>(test);

            Assert.NotNull(result);
            output.WriteLine(result);
        }

        [Fact]
        public void Deserialize()
        {
            var result = JsonSerializer.Deserialize<TestClass>(JsonText);

            Assert.NotNull(result);
        }
        
        [Fact]
        public void Poly_CachedTypeInterface_Serialize()
        {
            var writer = new JsonWriter();
            typeInterface.Serialize(writer, test);
            output.WriteLine(writer.Text.ToString());
        }

        [Fact]
        public void Poly_CachedTypeInterface_Deserialize()
        {
            var reader = new JsonReader(JsonText);
            typeInterface.Deserialize(reader, out var result);

            Assert.Equal(test.Name, result.Name);
            Assert.Equal(test.Strings, result.Strings);
            Assert.Equal(test.Dictionary, result.Dictionary);
        }
    }
}