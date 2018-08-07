using System;
using Xunit;

namespace Poly.UnitTests {
    using Collections;

    public class PriorityQueue {
        private readonly PriorityQueue<int, string> priority_queue;

        public PriorityQueue() {
            priority_queue = new PriorityQueue<int, string>();
        }

        [Fact]
        public void Enqueue_Dequeue() {
            priority_queue.Enqueue(1, "a");
            priority_queue.Enqueue(2, "b");
            priority_queue.Enqueue(3, "c");

            Assert.True(priority_queue.Count == 3, "Queue should contain 3 items.");

            Assert.True(priority_queue.Dequeue() == "a", "Items with lower priority value should dequeue first.");
            Assert.True(priority_queue.Dequeue() == "b", "Items with lower priority value should dequeue first.");
            Assert.True(priority_queue.Dequeue() == "c", "Items with lower priority value should dequeue first.");
        }
    }
}