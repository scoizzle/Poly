using System;
using System.Linq;
using Poly.Syntax;
using Poly.Syntax.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;

int size = args.Length > 0 ? int.Parse(args[0]) : 128;

// Pure double-precision Mandelbrot via opaque-64-bit ring model.
// Loop counters are doubles; coordinates map directly to the viewport.
// Constants store double bit patterns in long slots; the emitter reinterprets
// via BitConverter.Int64BitsToDouble before arithmetic, then converts back.

var px = new Variable("px"); var py = new Variable("py");
var zx = new Variable("zx"); var zy = new Variable("zy");
var zx2 = new Variable("zx2");
var iter = new Variable("iter"); var total = new Variable("total");

Node MandelPixel(Node cx, Node cy) => new Block([
    new Assignment(zx, new Constant(0.0)),
    new Assignment(zy, new Constant(0.0)),
    new Assignment(iter, new Constant(0L)),
    new WhileLoop(
        new And(
            new LessThan(iter, new Constant(256)),
            new LessThanOrEqual(
                new Add(new Multiply(zx, zx), new Multiply(zy, zy)),
                new Constant(4.0))),
        new Block([
            new Assignment(zx2, new Add(
                new Subtract(new Multiply(zx, zx), new Multiply(zy, zy)),
                cx)),
            new Assignment(zy, new Add(
                new Multiply(new Multiply(zx, new Constant(2.0)), zy),
                cy)),
            new Assignment(zx, zx2),
            new Assignment(iter, new Add(iter, new Constant(1L)))
        ])),
    iter
]);

// Pixel centers match fixed-point version: cx = -2.0 + px * (4.0 / size)
// where px ∈ [0, size).  Same for cy.  Uses px < limit loop guard.
var step = new Constant(4.0 / size);
var limit = new Constant(2.0 - 4.0 / size / 2.0);  // safe upper bound

var body = new Invoke(new Lambda([], new Block(
    [new Assignment(total, new Constant(0L)),
     new Assignment(py, new Constant(-2.0)),
     new WhileLoop(new LessThan(py, limit),
         new Block([
             new Assignment(px, new Constant(-2.0)),
             new WhileLoop(new LessThan(px, limit),
                 new Block([
                     new Assignment(total, new Add(total,
                         MandelPixel(px, py))),
                     new Assignment(px, new Add(px, step))
                 ])),
             new Assignment(py, new Add(py, step))
         ])),
     total],
    [px, py, zx, zy, zx2, iter, total])));

var prepSw = System.Diagnostics.Stopwatch.StartNew();
var program = Interpreter.Compile(body, CompilationMode.NoDebug);
// Absorb first-call JIT of the Expression.Compile() delegate into prep.
using (var _ = Interpreter.Execute(program)) { }
prepSw.Stop();

var sw = System.Diagnostics.Stopwatch.StartNew();
using var exec = Interpreter.Execute(program);
long result = exec.RawValue;
sw.Stop();
Console.WriteLine($"mandelbrot,Poly VM double,{size},{result},{sw.Elapsed.TotalMicroseconds:F0},{prepSw.ElapsedMilliseconds}");
