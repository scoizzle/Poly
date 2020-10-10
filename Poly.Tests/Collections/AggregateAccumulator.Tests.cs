using Xunit;

namespace Poly.Collections {
    public class AggregateAccumulatorTests {
        readonly AggregateAccumulator<int> accumulator;

        public AggregateAccumulatorTests() {
            accumulator = new AggregateAccumulator<int>(default);
        }

        [Fact]
        public void Add() {
            accumulator.Clear();
            accumulator.Add(_ => _ + 1);
            Assert.Equal(1, accumulator.Result);
        }

        [Fact]
        public void Subtract() {
            accumulator.Clear();
            accumulator.Add(_ => _ - 1);
            Assert.Equal(-1, accumulator.Result);
        }
    }
}