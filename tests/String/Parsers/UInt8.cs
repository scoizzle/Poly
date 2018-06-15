using System;
using Xunit;

namespace Poly.UnitTests {
    using String;

    public class String_UInt8_Parser {
        [Theory]
        [InlineData("123", 123)]
        [InlineData("255", byte.MaxValue)]
        [InlineData("0", byte.MinValue)]
        public void True(string text, byte value) {
            Assert.True(StringInt8Parser.TryParse(text, out byte result), $"UInt8 TryParse fails for input \"{text}\"");
            Assert.True(value == result, "UInt8 TryParse fails for input \"{text}\" expected {value}; returned {result}");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1")]
        [InlineData("1e5")]
        [InlineData("258")]
        [InlineData("-1")]
        public void False(string text) {
            Assert.False(StringInt8Parser.TryParse(text, out byte result), $"UInt8 TryParse fails for input \"{text}\"");        
        }
    }
}