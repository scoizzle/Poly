// namespace Poly.Test {
//     public partial class Tests {
//         public static void ScriptTest() {
//             var psx = new Script.Engine();
//             var js = new Data.JSON();

//             psx.Parse(@"
//                 class T1 {
//                     static func f1(a, b) {
//                         Static.Yolo = a * b;
//                         return Static.Yolo;
//                     }
//                 }
                
//                 return T1.f1(2, 2);");

//             Log.Benchmark("Script", 1000000, () => psx.Evaluate(js)).Wait();
//         }
// }}