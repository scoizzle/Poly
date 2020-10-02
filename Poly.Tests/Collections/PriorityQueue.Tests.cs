using Xunit;

namespace Poly.Collections {
    public class PriorityQueueTests {
        struct Priority {
            public const byte High = 0;
            public const byte Medium = 1;
            public const byte Low = 2;
        }

        enum Tasks : byte {
            Up, Down, Left, Right
        }

        readonly PriorityQueue<byte, Tasks> queue;

        public PriorityQueueTests() {
            queue = new PriorityQueue<byte, Tasks>();

            queue.Enqueue(Priority.High, Tasks.Down);
            queue.Enqueue(Priority.Low, Tasks.Left);
            queue.Enqueue(Priority.Medium, Tasks.Up);
            queue.Enqueue(Priority.High, Tasks.Right);
        }

        [Fact]
        public void TryDequeue() {
            Assert.True(queue.TryDequeue(out var priority, out var task));
            Assert.Equal(Priority.High, priority);
            Assert.Equal(Tasks.Down, task);

            Assert.True(queue.TryDequeue(out priority, out task));
            Assert.Equal(Priority.High, priority);
            Assert.Equal(Tasks.Right, task);

            Assert.True(queue.TryDequeue(out priority, out task));
            Assert.Equal(Priority.Medium, priority);
            Assert.Equal(Tasks.Up, task);

            Assert.True(queue.TryDequeue(out priority, out task));
            Assert.Equal(Priority.Low, priority);
            Assert.Equal(Tasks.Left, task);
            
            Assert.False(queue.TryDequeue(out _, out _));
        } 
    }
}