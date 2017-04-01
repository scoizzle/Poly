// using System;
// using System.Reflection;
// using MAttr = System.Reflection.MethodAttributes;
// using FAttr = System.Reflection.FieldAttributes;

// namespace Poly.Test {
//     using Compilation;
//     public partial class Tests {
//         public static void CompilerTest() {
//             var mod = new Compilation.Module("Test");

//             var t1 = mod.DeclareType("T1");

//             var f1 = t1.DeclareField(
//                 "yolo",
//                 typeof(long),
//                 FAttr.Public | FAttr.Static
//             );

//             var m1 = t1.DeclareMethod("f1", 
//                 Params: new Parameter[] {
//                     new Parameter("a", typeof(int), 0),
//                     new Parameter("b", typeof(int), 1)
//                 }, 
//                 returns: typeof(int), 
//                 attr: MAttr.Public | MAttr.Static);

//             m1.Emit.LoadParameter("a")
//                    .LoadParameter("b")
//                    .Multiply()
//                    .Dup()
//                    .StoreField(f1)
//                    .Return();

//             var t2 = mod.DeclareType("T2");
//             var f2 = t2.DeclareMethod("f2", attr: MAttr.Public | MAttr.Static);

//             f2.Emit.Int(2)
//                    .Int(2)
//                    .Call(m1)
//                    .Pop()
//                    .Return();

//             t1.Finish();
//             var info = t2.Finish();

//             var M1 = info.GetMethod("f2");
//             var D1 = (Action)M1.CreateDelegate(typeof(Action));

//             Log.Benchmark("Compiler", 1000000, D1).Wait();
//         }
// }}