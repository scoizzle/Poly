using System;
using System.Linq;
using Poly.Syntax;
using Poly.Syntax.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;

int size = args.Length > 0 ? int.Parse(args[0]) : 128;

var x = new Variable("x"); var y = new Variable("y");
var zx = new Variable("zx"); var zy = new Variable("zy");
var zx2 = new Variable("zx2");
var iter = new Variable("iter"); var total = new Variable("total");

// Double-precision mandelbrot: no fixed-point scaling, no shifts.
// cx = (x - 64) / 32.0,  cy = (y - 64) / 32.0  → viewport [-2, 2]
Node Cx(Node xv) => new Divide(new Subtract(new Convert(xv, typeof(double)), new Constant(64.0)), new Constant(32.0));
Node Cy(Node yv) => Cx(yv);

Node mandelPixel = new Block([
    new Assignment(zx, new Constant(0.0)),
    new Assignment(zy, new Constant(0.0)),
    new Assignment(iter, new Constant(0L)),
    new WhileLoop(
        new And(
            new LessThan(iter, new Constant(256)),
            // Bailout: |z|^2 = zx*zx + zy*zy > 4.0
            new LessThanOrEqual(
                new Add(new Multiply(zx, zx), new Multiply(zy, zy)),
                new Constant(4.0))),
        new Block([
            // zx2 = zx*zx - zy*zy + cx
            new Assignment(zx2, new Add(
                new Subtract(new Multiply(zx, zx), new Multiply(zy, zy)),
                Cx(x))),
            // zy = 2*zx*zy + cy
            new Assignment(zy, new Add(
                new Multiply(new Multiply(zx, new Constant(2.0)), zy),
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
    [x, y, zx, zy, zx2, iter, total])));

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var program = Interpreter.Compile(body, CompilationMode.NoDebug);
// Absorb first-call JIT of the Expression.Compile() delegate into prep.
using (var _ = Interpreter.Execute(program)) { }
prepSw.Stop();

var sw = System.Diagnostics.Stopwatch.StartNew();
using var exec = Interpreter.Execute(program);
long result = exec.RawValue;
sw.Stop();
// Same field layout as other Poly benches: language,size,result,us,prep_ms
Console.WriteLine($"Poly VM double,{size},{result},{sw.Elapsed.TotalMicroseconds:F0},{prepSw.ElapsedMilliseconds}");
