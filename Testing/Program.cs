namespace Poly.Testing {
    public class Program : App {
        public static void Main(string[] args) {
            Init(args);

			Commands.Register("json", p => {
				Tests.JsonTest();
				return null;
			});

            Commands.Register("match", (p) => {
                Tests.MatchingPerformance();
                return null;
            });

			// These commands have been deprecated for now
            // App.Commands.Register("comp", (p) => {
            //     Tests.CompilerTest();
            //     return null;
            // });

            // App.Commands.Register("script", (p) => {
            //     Tests.ScriptTest();
            //     return null;
            // });

            Commands.Register("launch", (p) => {
                Tests.Launcher();
                return null;
            });

            WaitforExit();
        }
    }
}
