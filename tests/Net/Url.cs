using System;
using Xunit;
using Xunit.Abstractions;

namespace Poly.UnitTests {
    public class Url {
        [Fact]
        public void Parse() {
            var text = "https://www.youtube.com/watch?v=Jr0Z1v4xSGQ";
            var try_parse = Net.Url.TryParse(text, out Net.Url url);

            Assert.True(try_parse);
            Assert.Equal("www.youtube.com", url.Hostname);
            Assert.Equal("https", url.Scheme);
            Assert.Equal("watch", url.Path);
            Assert.Equal("Jr0Z1v4xSGQ", url.Query["v"]);
        }
    }
}