using System;
using Xunit;

namespace Poly.UnitTests {
    using String;

    public class String_Int16_Parser {
        [Theory]
        [InlineData("12345", 12345)]
        [InlineData("-1337", -1337)]
        [InlineData("32767", short.MaxValue)]
        [InlineData("-32768", short.MinValue)]
        public void True(string text, short value) {
            Assert.True(StringInt16Parser.TryParse(text, out short result), $"Int16 TryParse fails for input \"{text}\"");
            Assert.True(value == result, "Int16 TryParse fails for input \"{text}\" expected {value}; returned {result}");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1")]
        [InlineData("1e5")]
        [InlineData("21474836477")]
        [InlineData("-21474836488")]
        public void False(string text) {
            Assert.False(StringInt16Parser.TryParse(text, out short result), $"Int16 TryParse fails for input \"{text}\"");        
        }
    }
}