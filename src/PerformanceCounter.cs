using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Poly {
    public class PerformanceCounter : Dictionary<string, Stopwatch> {
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
    }
}