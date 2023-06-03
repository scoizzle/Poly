using System;
using BenchmarkDotNet.Attributes;

namespace Poly.Text.Matching.Benchmarks
{

    public class IrcMatcherBenchmarks {
        private Expression expression;
        private TryCompareDelegate compare;

        [GlobalSetup]
        public void Setup()
        {
            _ = Parser.TryParse(new StringView(IRCMatchString), out expression);
            compare = expression.Compare();
        }

        [Benchmark]
        public void GeneratedViewDelegateChain() {
            compare(new StringView(IRCTestString));
        }

        public const string IRCTestString = ":server 304 client ? random arguments that dont suck :a message for the client";

        public const string IRCMatchString = ":{Sender} {Type} ({Receiver} )?({Args:![:]} )?:{Message}?";
    }
}