using System;
using Xunit;
using Xunit.Abstractions;

namespace Poly.UnitTests {
    public class Matching {
        [Fact]
        public void JSON() {
            var matcher = new String.Matcher("{a}={b}");

            Assert.True(matcher.Compare("test=true"));
            Assert.True(matcher.Extract("test=true", out Data.JSON json));

            Assert.Equal("test", json["a"]);
            Assert.Equal("true", json["b"]);
        }
    }
}