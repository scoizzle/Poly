using System;
using Xunit;

namespace Poly.UnitTests {
    using String;

    public class String_UInt16_Parser {
        [Theory]
        [InlineData("12345", 12345)]
        [InlineData("65535", ushort.MaxValue)]
        [InlineData("0", ushort.MinValue)]
        public void True(string text, ushort value) {
            Assert.True(StringInt16Parser.TryParse(text, out ushort result), $"UInt16 TryParse fails for input \"{text}\"");
            Assert.True(value == result, "UInt16 TryParse fails for input \"{text}\" expected {value}; returned {result}");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1")]
        [InlineData("1e5")]
        [InlineData("21474836477")]
        [InlineData("-1")]
        public void False(string text) {
            Assert.False(StringInt16Parser.TryParse(text, out ushort result), $"UInt16 TryParse fails for input \"{text}\"");        
        }
    }
}