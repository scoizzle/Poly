using System;
using Xunit;

namespace Poly.UnitTests {
    using Collections;

    public class ExecutionChain {
        private readonly ExecutionChain<int> execution_chain;

        public ExecutionChain() {
            execution_chain = new ExecutionChain<int>(0);

            execution_chain.Add(_ => _ + 1);
            execution_chain.Add(_ => _ + 2);
        }

        [Fact]
        public void Build() {
            Assert.True(execution_chain.Build() == 3, "Execution chain should yield 3.");
        }
    }
}