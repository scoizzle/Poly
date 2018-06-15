using System;
using Xunit;

namespace Poly.UnitTests {
    using String;

    public class String_Int8_Parser {
        [Theory]
        [InlineData("123", 123)]
        [InlineData("-13", -13)]
        [InlineData("127", sbyte.MaxValue)]
        [InlineData("-128", sbyte.MinValue)]
        public void True(string text, sbyte value) {
            Assert.True(StringInt8Parser.TryParse(text, out sbyte result), $"Int8 TryParse fails for input \"{text}\"");
            Assert.True(value == result, "Int8 TryParse fails for input \"{text}\" expected {value}; returned {result}");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1")]
        [InlineData("1e5")]
        [InlineData("21474836477")]
        [InlineData("-21474836488")]
        public void False(string text) {
            Assert.False(StringInt8Parser.TryParse(text, out sbyte result), $"Int8 TryParse fails for input \"{text}\"");        
        }
    }
}