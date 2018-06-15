using System;
using Xunit;

namespace Poly.UnitTests {
    using String;

    public class String_Int64_Parser {
        [Theory]
        [InlineData("123456789", 123456789)]
        [InlineData("-1337", -1337)]
        [InlineData("9223372036854775807", long.MaxValue)]
        [InlineData("-9223372036854775808", long.MinValue)]
        public void True(string text, long value) {
            Assert.True(StringInt64Parser.TryParse(text, out long result), $"Int64 TryParse fails for input \"{text}\"");
            Assert.True(value == result, "Int64 TryParse fails for input \"{text}\" expected {value}; returned {result}");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1")]
        [InlineData("1e5")]
        [InlineData("922337203685477580799")]
        [InlineData("-922337203685477580854")]
        public void False(string text) {
            Assert.False(StringInt64Parser.TryParse(text, out long result), $"Int64 TryParse fails for input \"{text}\"");        
        }
    }
}