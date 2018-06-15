using System;
using Xunit;

namespace Poly.UnitTests {
    using String;

    public class String_UInt64_Parser {
        [Theory]
        [InlineData("123456789", 123456789)]
        [InlineData("18446744073709551615", ulong.MaxValue)]
        [InlineData("0", ulong.MinValue)]
        public void True(string text, ulong value) {
            Assert.True(StringInt64Parser.TryParse(text, out ulong result), $"UInt64 TryParse fails for input \"{text}\"");
            Assert.True(value == result, "UInt64 TryParse fails for input \"{text}\" expected {value}; returned {result}");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1")]
        [InlineData("1e5")]
        [InlineData("922337203685477580799")]
        [InlineData("-922337203685477580854")]
        public void False(string text) {
            Assert.False(StringInt64Parser.TryParse(text, out ulong result), $"UInt64 TryParse fails for input \"{text}\"");        
        }
    }
}