using System;
using System.Linq;
using Poly.Syntax;
using Poly.Syntax.Nodes;
using Poly.Interpretation;
using Prim = Poly.Syntax.Primitives;
using Poly.Interpretation.Vm;

int size = args.Length > 0 ? int.Parse(args[0]) : 128;
bool debug = args.Length > 1 && args[1] == "--debug";
const int S = 8;

var x = new Variable("x"); var y = new Variable("y");
var zx = new Variable("zx"); var zy = new Variable("zy");
var zx2 = new Variable("zx2"); var zy2 = new Variable("zy2");
var iter = new Variable("iter"); var total = new Variable("total");

Node Cx(Node xv) => new Subtract(new Multiply(xv, new Constant(8L)), new Constant(size * 4L));
Node Cy(Node yv) => Cx(yv);

Node mandelPixel = new Block([
    new Assignment(zx, new Constant(0L)),
    new Assignment(zy, new Constant(0L)),
    new Assignment(iter, new Constant(0L)),
    new WhileLoop(
        new And(
            new LessThan(iter, new Constant(256)),
            new LessThanOrEqual(
                new Add(
                    new ShiftRight(new Multiply(zx, zx), new Constant(S)),
                    new ShiftRight(new Multiply(zy, zy), new Constant(S))),
                new Constant(4 << S))),
        new Block([
            new Assignment(zx2, new Add(
                new Subtract(
                    new ShiftRight(new Multiply(zx, zx), new Constant(S)),
                    new ShiftRight(new Multiply(zy, zy), new Constant(S))),
                Cx(x))),
            new Assignment(zy, new Add(
                new ShiftRight(new Multiply(
                    new Multiply(zx, new Constant(2L)), zy), new Constant(S)),
                Cy(y))),
            new Assignment(zx, zx2),
            new Assignment(iter, new Add(iter, new Constant(1L)))
        ])),
    iter
]);

var body = new Invoke(new Lambda([], new Block(
    [new Assignment(total, new Constant(0L)),
     new Assignment(y, new Constant(0L)),
     new WhileLoop(new LessThan(y, new Constant(size)),
         new Block([
             new Assignment(x, new Constant(0L)),
             new WhileLoop(new LessThan(x, new Constant(size)),
                 new Block([
                     new Assignment(total, new Add(total, mandelPixel)),
                     new Assignment(x, new Add(x, new Constant(1L)))
                 ])),
             new Assignment(y, new Add(y, new Constant(1L)))
         ])),
     total],
    [x, y, zx, zy, zx2, zy2, iter, total])));

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var program = Interpreter.Compile(body, CompilationMode.NoDebug);
prepSw.Stop();

var sw = System.Diagnostics.Stopwatch.StartNew();
using var exec = Vm.Execute(program);
long result = exec.RawValue;
sw.Stop();
Console.WriteLine($"Poly VM,{size},{result},{sw.ElapsedMilliseconds},{prepSw.ElapsedMilliseconds}");
