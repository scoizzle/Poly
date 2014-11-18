using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly {
    public partial class Event {
        public class TaskQueue<Args> : Queue<Args> {
            object SyncRoot;
            Action<Args> Handler;
            Thread[] Workers;

            public TaskQueue(Action<Args> Function) {
                SyncRoot = new object();
                Handler = Function;
                Workers = new Thread[Environment.ProcessorCount];

                for (int i = 0; i < Workers.Length; i++) {
                    Workers[i] = new Thread(Worker);
                    Workers[i].Start();
                }
            }

            ~TaskQueue() {
                foreach (var T in Workers) {
                    T.Abort();
                }
                Workers = null;
            }

            private void Worker() {
                while (Thread.CurrentThread.ThreadState != ThreadState.AbortRequested) {
                    Args Work;
                    lock (SyncRoot) {
                        while (Count == 0)
                            Thread.Sleep(1);

                        Work = Dequeue();
                    }

                    Handler(Work);
                }
            }
        }
    }
}
