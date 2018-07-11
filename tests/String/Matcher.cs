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

        class Route {
            public string controller;
            public string method;
        }

        [Fact]
        public void CustomClass() {
            var fmt = "/{controller}/({method}/?)?";
            var matcher = new String.Matcher<Route>(fmt);

            Assert.True(matcher.Compare("/user/"));
            Assert.True(matcher.Extract("/user/login/", out Route obj));

            Assert.Equal("user", obj.controller);
            Assert.Equal("login", obj.method);

            Assert.False(matcher.Compare("/user"));
            Assert.False(matcher.Compare("user/"));
            Assert.True(matcher.Compare("/user/logout")); // Returns true, but method isn't set
        }
    }
}