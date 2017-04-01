using System.Text.RegularExpressions;

namespace Poly.Test {
    public partial class Tests {

        public static void MatchingPerformance() {
            var Data = "This is a interesting test.";
            string[] MatchStrings = new string[]{
                        "*",
                        "This*test.",
                        "This{0}test.",
                        "This{0} a {1} test."
                     },
                     RegexStrings = new string[]{
                         ".+",
                         "This.+test\\.",
                         "This(.+)test\\.",
                         "This(.+) a (.+) test\\."
                     };

            for (int i = 0; i < MatchStrings.Length; i++) {
                RunBench(Data, MatchStrings[i], RegexStrings[i], 1000000);
            }
        }

        private static void RunBench(string Data, string MatchString, string RegexString, int Iterations) {
            var Test = new Matcher(MatchString);
            var Rege = new Regex(RegexString, RegexOptions.Compiled);

            Log.Info("Input: {0}", Data);
            Log.Info("Template: {0}", Test.Template(Test.Match(Data)));

            Log.Benchmark(string.Format("Poly: {0}", MatchString), Iterations, () => {
                Test.Match(Data);
            }).Wait();

            Log.Benchmark(string.Format("Regex: {0}", RegexString), Iterations, () => {
                Rege.Match(Data);
            }).Wait();
        }
    }
}