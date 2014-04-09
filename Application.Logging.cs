using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Poly {
    public class Logging {
        public bool Active = false;
        public int Level = 0;

        public class Levels {
            public const int None = -1;
            public const int Fatal = 0;
            public const int Error = 1;
            public const int Warning = 2;
            public const int Info = 3;
        };

        public void Info(string Message) {
            if (Level >= Levels.Info){
                Log("[INFO] " + Message);
            }
        }

        public void Warning(string Message) {
            if (Level >= Levels.Warning) {
                Log("[WARNING] " + Message);
            }
        }

        public void Error(string Message) {
            if (Level >= Levels.Error) {
                Log("[ERROR] " + Message);
            }
        }

        public void Fatal(string Message) {
            if (Level >= Levels.Fatal) {
                Log("[FATAL] " + Message);
            }
            App.Exit(-1);
        }

        public void Log(string Message) {
            if (!Active)
                return;

            Console.WriteLine(Message);
        }

        public void Benchmark(string Name, int Iterations, Action<int> Todo) {
            int Start = Environment.TickCount;
            for (int i = 0; i < Iterations; i++) {
                Todo(i);
            }
            int Stop = Environment.TickCount;

            Info(Name + ": " + (Stop - Start).ToString());
        }
    }
}
