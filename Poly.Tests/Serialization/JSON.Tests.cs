using System;
using System.Buffers;
using System.Collections.Generic;

using Xunit;
using Xunit.Abstractions;

using Poly.Parsing;
using Poly.Reflection;
using Poly.Parsing.Json;
using System.Linq;
using System.Text;

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

        public static readonly ISystemTypeInterface<TestClass> typeInterface = TypeInterfaceRegistry.Get<TestClass>();

        public static readonly TestClass test = CreateSerializationObject();

        public static readonly string JsonText = Newtonsoft.Json.JsonConvert.SerializeObject(test);


        public JsonSerializationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static TestClass CreateSerializationObject()
        {
            TestClass test = new()
            {
                BigNumber = 34123123123.121M,
                Now = DateTime.Now.AddHours(1),
                Dictionary = new Dictionary<string, int> { { "Val & asd1", 1 }, { "Val2 & asd1", 3 }, { "Val3 & asd1", 3 } },
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

        // [Fact]
        // public void ITokenReader_Read_All_Tokens()
        // {
        //     var tabs = 0;
        //     var builder = new StringBuilder();
        //     var tokenReader = new JsonStringTokenReader(JsonText);

        //     output.WriteLine(string.Empty);

        //     while (tokenReader.TryReadToken(out var result)) {
        //         tabs = result.Token switch {
        //             JsonToken.EndObject or JsonToken.EndArray => tabs - 1,
        //             _ => tabs
        //         };

        //         builder
        //             .Append(' ', tabs * 2)
        //             .Append(result.Token.ToString());

        //         output.WriteLine(builder.ToString());

        //         builder.Clear();

        //         tabs = result.Token switch {
        //             JsonToken.BeginObject or JsonToken.BeginArray => tabs + 1,
        //             _ => tabs
        //         };
        //     }

        //     if (!tokenReader.IsDone)
        //         output.WriteLine(tokenReader.ToString());

        //     Assert.True(tokenReader.IsDone, "TokenReader.IsDone should be true after reading all tokens.");
        // }

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
        [Fact]
        public void Poly_Serialize()
        {
            var writer = new JsonWriter();
            typeInterface.Serialize(writer, test);
            output.WriteLine(writer.Text.ToString());
        }

        [Fact]
        public void Poly_Deserialize()
        {
            var reader = new JsonReader(JsonText);
            typeInterface.Deserialize(reader, out var result);

            Assert.Equal(test.Name, result.Name);
            Assert.Equal(test.Strings, result.Strings);
            Assert.Equal(test.Dictionary, result.Dictionary);
        }

        [Fact]
        public void Poly_Serialize_Pipeline()
        {
            var writer = new JsonWriterPipelines();
            typeInterface.Serialize(writer, test);
            output.WriteLine(writer.ToString());
        }

        [Fact]
        public void Poly_Deserialize_Pipeline()
        {
            var sequence = new ReadOnlySequence<char>(JsonText.AsMemory());
            var writer = new JsonReaderPipelines(sequence);

            typeInterface.Deserialize(writer, out var result);

            Assert.Equal(test.Name, result.Name);
            Assert.Equal(test.Strings, result.Strings);
            Assert.Equal(test.Dictionary, result.Dictionary);
        }

        class Minor {
            public bool True { get; set; }
            public string Text { get; set; }
        }

        class Major {
            public Minor[] Test { get; set; }
        }

        static string GetTestString(int minorInstances)
        {
            var minors = Enumerable
                .Range(0, minorInstances)
                .Select(i => new Minor { True = true, Text = "test" })
                .ToArray();

            
            var m = new Major { Test = minors };
            
            return System.Text.Json.JsonSerializer.Serialize(m);
        }


        [Fact]
        public void Ugh() {
            var instance = GetTestString(100);
            var sequence = new ReadOnlySequence<char>(instance.AsMemory());
            var tokens = JsonGrammar.Definition.ParseAllTokens(sequence);

            var builder = new StringBuilder();

            foreach (var result in tokens)
            {
                if (!result.Success)
                    throw new Exception();

                builder.Append(result.Segment);
            }

            output.WriteLine(builder.ToString());
        }
    }
}