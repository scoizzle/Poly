using Poly.Data;
using System;
using System.Text;
using System.Threading;

namespace Poly {

    public partial class App {
        public static bool Running;

        public static Encoding Encoding = Encoding.UTF8;

        public static void Init(params string[] Commands) {
            Log.Info("Application initializing...");
            Running = true;
            Log.Info("Application running...");
        }

        public static void WaitforExit() {
            while (Running) {
                var input = Console.ReadLine();

                Thread.Sleep(200);
            }
        }

        public static void Exit(int Status = 0) {
            Log.Info("Applcation Exiting...");

            Running = false;
        }
    }
}