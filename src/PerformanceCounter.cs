using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Poly {
    public class PerformanceTimer : Dictionary<string, Stopwatch> {
        public Stopwatch Start(string name) {
            if (!TryGetValue(name, out Stopwatch watch)) {
                watch = new Stopwatch();
                Add(name, watch);
            }

            watch.Start();
            return watch;
        }

        public void Stop(string name) {
            if (TryGetValue(name, out Stopwatch watch))
                watch.Stop();
        }

        public TimeSpan TotalTime =>
            new TimeSpan(Values.Sum(ts => ts.ElapsedTicks));
    }
}