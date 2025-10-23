using BenchmarkDotNet.Attributes;

namespace Poly.Text.Matching.Benchmarks {

    public class UrlMatcherBenchmarks {
        private Expression expression;
        private TryCompareDelegate compare;

        [GlobalSetup]
        public void Setup() {
            _ = Parser.TryParse(new StringView(UrlMatchString), out expression);
            compare = expression.Compare();
        }

        [Benchmark]
        public void GeneratedViewDelegateChain() {
            compare(new StringView(UrlTestString));
        }

        public const string UrlTestString = "https://www.youtube.com/watch?v=6QHG6uxJYLo#test";

        public const string UrlMatchString =
            "{Scheme}" +
            "://" +
            "(" +
                "{Username}" +
                "(" +
                    ":" +
                    "{Password}" +
                ")?" +
                "@" +
            ")?" +
            "{Hostname}" +
            "(" +
                ":" +
                "{Port}" +
            ")?" +
            "(" +
                "/" +
                "{Path}" +
            ")?" +
            "(" +
                "\\?" +
                "{Query}" +
            ")?" +
            "(" +
                "#" +
                "{Fragment}" +
            ")?";
    }
}