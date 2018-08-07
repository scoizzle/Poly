using System;
using Xunit;

namespace Poly.UnitTests {
    using String;

    public class String_Int32_Parser {
        [Theory]
        [InlineData("1234567", 1234567)]
        [InlineData("-1337", -1337)]
        [InlineData("2147483647", int.MaxValue)]
        [InlineData("-2147483648", int.MinValue)]
        public void True(string text, int value) {
            Assert.True(StringInt32Parser.TryParse(text, out int result), $"Int32 TryParse fails for input \"{text}\"");
            Assert.True(value == result, "Int32 TryParse fails for input \"{text}\" expected {value}; returned {result}");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1")]
        [InlineData("1e5")]
        [InlineData("21474836477")]
        [InlineData("-21474836488")]
        public void False(string text) {
            Assert.False(StringInt32Parser.TryParse(text, out int result), $"Int32 TryParse fails for input \"{text}\"");        
        }
    }
}