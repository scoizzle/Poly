using System;
using Xunit;

namespace Poly.UnitTests {
    using String;

    public class String_UInt32_Parser {
        [Theory]
        [InlineData("1234567", 1234567)]
        [InlineData("4294967295", uint.MaxValue)]
        [InlineData("0", uint.MinValue)]
        public void True(string text, uint value) {
            Assert.True(StringInt32Parser.TryParse(text, out uint result), $"UInt32 TryParse fails for input \"{text}\"");
            Assert.True(value == result, "UInt32 TryParse fails for input \"{text}\" expected {value}; returned {result}");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1")]
        [InlineData("1e5")]
        [InlineData("2147483647788")]
        [InlineData("-1")]
        public void False(string text) {
            Assert.False(StringInt32Parser.TryParse(text, out uint result), $"UInt32 TryParse fails for input \"{text}\"");        
        }
    }
}