using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// IS7d: a local whose value is <c>new T[n]</c> hands the array object to a typed CLR
/// local at its definition, so an element access is <c>_arr3[i]</c> — one bounds check,
/// no <c>heap.UnsafeGet</c>, no <c>castclass</c>. The variable's slot keeps the heap
/// handle, so identity is unchanged; the local is a cache, not a replacement.
/// <para>Every claim here is proven on the emitted tree rather than on source text: a
/// deref that is absent from the tree cannot be hidden by a later pass.</para>
/// </summary>
public class ArrayLocalCacheTests {
    private static AbiEmitter.BuiltVmProgram Build(Node program) =>
        AbiEmitter.Build(program, Interpreter.Analyze(program), CompilationMode.NoDebug);

    private static VmTree.Scan Scan(Node program) => VmTree.Read(Build(program).Expression);

    private static long Run(Node program) {
        using var exec = Interpreter.Execute(Interpreter.Compile(program, CompilationMode.NoDebug));
        return exec.RawValue;
    }

    private static Block ArrayProgram(Node newArray, Variable array, params Node[] rest) {
        var statements = new List<Node> { new Assignment(array, newArray) };
        statements.AddRange(rest);
        return new Block(statements, [array]);
    }

    /// <summary>The row that pays for the change: an element access on a promoted array
    /// is the array object indexed, not a handle dereferenced. The whole body is the
    /// access here, so zero derefs in the body is zero in the access subtree.</summary>
    [Test, Timeout(10_000)]
    public async Task PromotedAccess_HasNoHeapDeref(CancellationToken ct) {
        var cols = new Variable("cols");
        var program = ArrayProgram(
            new NewArray(TypeReference.To<long>(), new Constant(4L)), cols,
            new Assignment(new IndexAccess(cols, new Constant(0L)), new Constant(7L)),
            new Assignment(new IndexAccess(cols, new Constant(1L)), new Constant(5L)),
            new IndexAccess(cols, new Constant(1L)));

        var scan = Scan(program);

        var caches = scan.ArrayCacheLocals;
        await Assert.That(caches.Count).IsEqualTo(1)
            .Because("the array's definition binds one typed local: "
                + string.Join(", ", caches.Select(local => $"{local.Name}:{local.Type.Name}")));
        await Assert.That(caches[0].Type).IsEqualTo(typeof(long[]))
            .Because("a long element is one slot word, so the cache is the long[] the heap holds");

        var accesses = scan.ArrayCacheAccesses;
        await Assert.That(accesses.Count).IsEqualTo(3)
            .Because("two element stores and one read go through the cache local: "
                + string.Join(", ", accesses.Select(a => a.ToString())));

        var derefs = scan.HeapDerefs.Select(call => call.ToString()).ToList();
        await Assert.That(derefs).IsEmpty()
            .Because("a promoted access is the array object indexed; no heap deref remains: "
                + string.Join(", ", derefs));

        await Assert.That(Run(program)).IsEqualTo(5L)
            .Because("the cached access reads the element the store wrote");
    }

    /// <summary>One definition is one array: the local and the slot are bound to the same
    /// object, so promotion must not allocate a second one.</summary>
    [Test, Timeout(10_000)]
    public async Task Promotion_AllocatesOnce(CancellationToken ct) {
        var cols = new Variable("cols");
        var program = ArrayProgram(
            new NewArray(TypeReference.To<long>(), new Constant(4L)), cols,
            new Assignment(new IndexAccess(cols, new Constant(0L)), new Constant(7L)),
            new IndexAccess(cols, new Constant(0L)));

        var scan = Scan(program);

        var allocations = scan.HeapAllocations;
        await Assert.That(allocations.Count).IsEqualTo(1)
            .Because("the definition allocates the array once and binds it to both the local "
                + "and the slot: " + string.Join(", ", allocations.Select(call => call.ToString())));

        await Assert.That(scan.ArrayCacheStores.Count(store => store.Value == "array")).IsEqualTo(1)
            .Because("the array object is stored to the cache exactly once, at its definition");
        await Assert.That(scan.HeapDerefs).IsEmpty()
            .Because("binding the local adds no deref");
    }

    /// <summary>The scope-exit clear, on the emitted block: the last thing a promoted
    /// definition's local sees is <c>null</c>, never the array it held.</summary>
    [Test, Timeout(10_000)]
    public async Task ScopeExit_NullsTheCache(CancellationToken ct) {
        var cols = new Variable("cols");
        var program = ArrayProgram(
            new NewArray(TypeReference.To<long>(), new Constant(4L)), cols,
            new Assignment(new IndexAccess(cols, new Constant(0L)), new Constant(7L)),
            new IndexAccess(cols, new Constant(0L)));

        var scan = Scan(program);
        var cache = scan.ArrayCacheLocals.Single();

        var stores = scan.ArrayCacheStores;
        await Assert.That(stores.Select(store => store.Value).ToList())
            .IsEquivalentTo(new[] { "array", "null" })
            .Because("definition then scope exit, in tree order: "
                + string.Join(", ", stores.Select(store => store.Value)));
        await Assert.That(stores[^1].Local).IsSameReferenceAs(cache)
            .Because("the scope exit clears the same local the definition bound");
        await Assert.That(stores[^1].Value).IsEqualTo("null")
            .Because("the block assigns null after PopScope, so the local cannot outlive its scope");
    }

    /// <summary>The row that justifies the null. Slots are released when a block ends and
    /// the next sibling block's variable takes the same one, so both blocks' arrays are
    /// cached in the <em>same</em> local. The first block's exit clears it before the
    /// second block's definition runs, so a read of that local before the second
    /// definition fails closed on <c>null</c> instead of reading the first block's array.
    /// <para>The clear is defensive against a stale read that the promotion predicate
    /// already forbids (a promoted definition precedes every use), and it becomes
    /// load-bearing the moment the predicate is relaxed or the heap gains recycling.</para>
    /// </summary>
    [Test, Timeout(10_000)]
    public async Task SiblingBlock_SlotReuse_ReadsNullNotStaleArray(CancellationToken ct) {
        var first = new Variable("first");
        var second = new Variable("second");
        var program = new Block([
            ArrayProgram(new NewArray(TypeReference.To<long>(), new Constant(3L)), first,
                new Assignment(new IndexAccess(first, new Constant(0L)), new Constant(11L)),
                new IndexAccess(first, new Constant(0L))),
            ArrayProgram(new NewArray(TypeReference.To<long>(), new Constant(3L)), second,
                new Assignment(new IndexAccess(second, new Constant(0L)), new Constant(22L)),
                new IndexAccess(second, new Constant(0L))),
        ]);

        var scan = Scan(program);

        var caches = scan.ArrayCacheLocals;
        await Assert.That(caches.Count).IsEqualTo(1)
            .Because("a slot is reused across sibling scopes, and the cache is keyed by "
                + "(slot, element type), so both blocks share one local: "
                + string.Join(", ", caches.Select(local => local.Name)));

        var registers = scan.Registers.Select(local => local.Name ?? string.Empty).Distinct().ToList();
        await Assert.That(registers).IsEquivalentTo(new[] { "_reg0" })
            .Because("both sibling blocks' arrays live in register 0 — the reuse the null "
                + "makes safe: " + string.Join(", ", registers));

        var stores = scan.ArrayCacheStores.Select(store => store.Value).ToList();
        await Assert.That(stores).IsEquivalentTo(new[] { "array", "null", "array", "null" })
            .Because("each block binds the shared local at its definition and clears it at "
                + "its exit, so the first block's array cannot reach the second: "
                + string.Join(", ", stores));

        await Assert.That(Run(program)).IsEqualTo(22L)
            .Because("each block reads its own array, not the sibling's");
    }

    /// <summary>An escaping use keeps the heap path: the local is a cache for element
    /// access only, and a value that leaves the variable cannot be served from it.</summary>
    [Test, Timeout(10_000)]
    public async Task EscapingUse_KeepsTheHeapPath(CancellationToken ct) {
        var cols = new Variable("cols");
        var xs = new Parameter("xs", TypeReference.To<long[]>());
        var program = new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(3L))),
            new Invoke(new Lambda([xs], new Member(xs, "Length")), cols),
        ], [cols]);

        var scan = Scan(program);

        await Assert.That(scan.ArrayCacheLocals).IsEmpty()
            .Because("the array is passed as an argument, so nothing may be cached for it");
        await Assert.That(scan.HeapDerefs.Count).IsGreaterThan(0)
            .Because("the argument read keeps the heap path");
        await Assert.That(Run(program)).IsEqualTo(3L);
    }

    /// <summary>A capture keeps the heap path: the closure body is a separate delegate
    /// and cannot see the emitter's CLR local.</summary>
    [Test, Timeout(10_000)]
    public async Task CapturedArray_KeepsTheHeapPath(CancellationToken ct) {
        var cols = new Variable("cols");
        var fn = new Variable("fn");
        var program = new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(3L))),
            new Assignment(new IndexAccess(cols, new Constant(0L)), new Constant(9L)),
            new Assignment(fn, new Lambda([], new IndexAccess(cols, new Constant(0L)))),
            new Invoke(fn),
        ], [cols, fn]);

        var scan = Scan(program);

        await Assert.That(scan.ArrayCacheLocals).IsEmpty()
            .Because("the array is captured by a lambda, whose body is a separate delegate");
        await Assert.That(scan.HeapDerefs.Count).IsGreaterThan(0)
            .Because("the captured read goes through the heap");
        await Assert.That(Run(program)).IsEqualTo(9L);
    }

    /// <summary>A second assignment means the local would disagree with the slot after the
    /// rebind, so the variable keeps the heap path.</summary>
    [Test, Timeout(10_000)]
    public async Task ReassignedArray_KeepsTheHeapPath(CancellationToken ct) {
        var cols = new Variable("cols");
        var program = new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(3L))),
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(5L))),
            new Assignment(new IndexAccess(cols, new Constant(4L)), new Constant(9L)),
            new IndexAccess(cols, new Constant(4L)),
        ], [cols]);

        var scan = Scan(program);

        await Assert.That(scan.ArrayCacheLocals).IsEmpty()
            .Because("a reassigned array is two definitions, so no one local is the truth");
        await Assert.That(Run(program)).IsEqualTo(9L);
    }

    /// <summary><c>.Length</c> reads the array object, which the heap path already serves;
    /// rather than split one variable across two representations, promotion bails.</summary>
    [Test, Timeout(10_000)]
    public async Task LengthUse_KeepsTheHeapPath(CancellationToken ct) {
        var cols = new Variable("cols");
        var program = ArrayProgram(
            new NewArray(TypeReference.To<long>(), new Constant(3L)), cols,
            new Member(cols, "Length"));

        var scan = Scan(program);

        await Assert.That(scan.ArrayCacheLocals).IsEmpty()
            .Because("a .Length use is not an element access, so the variable keeps the heap path");
        await Assert.That(Run(program)).IsEqualTo(3L);
    }

    /// <summary>A double element is still one slot word, so the cache is the same
    /// <c>long[]</c> and the IEEE value decodes once on the way out rather than
    /// round-tripping through the heap.</summary>
    [Test, Timeout(10_000)]
    public async Task DoublesArray_ReadsIeeeElements(CancellationToken ct) {
        var values = new Variable("values");
        var program = ArrayProgram(
            new NewArray(TypeReference.To<double>(), new Constant(3L)), values,
            new Assignment(new IndexAccess(values, new Constant(0L)), new Constant(1.5)),
            new IndexAccess(values, new Constant(0L)));

        var scan = Scan(program);

        var caches = scan.ArrayCacheLocals;
        await Assert.That(caches.Count).IsEqualTo(1)
            .Because("a double element is a slot word, so the cache is the heap's long[]");
        await Assert.That(caches[0].Type).IsEqualTo(typeof(long[]));
        await Assert.That(scan.ArrayCacheAccesses.Count).IsEqualTo(2)
            .Because("the store and the read both index the cache local");
        await Assert.That(scan.HeapDerefs).IsEmpty()
            .Because("no deref remains between a double element and the ring slot");

        using var exec = Interpreter.Execute(Interpreter.Compile(program, CompilationMode.NoDebug));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(1.5);
    }

    /// <summary>An out-of-range index on a promoted array throws the same exception the
    /// heap path throws: the CLR's own bounds check, not a heap deref failure.</summary>
    [Test, Timeout(10_000)]
    public async Task PromotedOutOfRange_ThrowsIndexOutOfRange(CancellationToken ct) {
        var cols = new Variable("cols");
        var program = ArrayProgram(
            new NewArray(TypeReference.To<long>(), new Constant(3L)), cols,
            new IndexAccess(cols, new Constant(999L)));

        await Assert.That(Scan(program).ArrayCacheAccesses.Count).IsEqualTo(1)
            .Because("the failing access is the promoted one");
        await Assert.That(() => Run(program)).Throws<IndexOutOfRangeException>();
    }
}