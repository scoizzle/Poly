using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The producer table is a claim; these tests are what make it a check. Every concrete
/// <see cref="Node"/> kind has a fact, every claim is backed by a corpus case the producer
/// honours, and every deliberate rejection carries the message the emitter throws.
/// </summary>
public class NodeFactsTotalityTests {
    private static IEnumerable<KeyValuePair<Type, NodeFact>> Ordered() =>
        NodeFacts.All.OrderBy(kv => kv.Value.Label, StringComparer.Ordinal);

    private static KeyValuePair<Type, NodeFact>[] Claiming(Producers producer) =>
        [.. Ordered().Where(kv => kv.Value.Producers.HasFlag(producer))];

    private static string[] Names(IEnumerable<KeyValuePair<Type, NodeFact>> facts) =>
        [.. facts.Select(kv => kv.Value.Label)];

    private static string Explain(string headline, IEnumerable<string> failures) =>
        $"{headline}: {string.Join("; ", failures)}";

    [Test]
    public async Task EveryConcreteNodeType_HasAFact() {
        var kinds = ConcreteKinds();
        var missing = kinds.Where(t => !NodeFacts.All.ContainsKey(t)).Select(t => t.Name).ToArray();
        await Assert.That(missing).IsEmpty()
            .Because(Explain("every concrete Node kind needs one fact", missing));

        var stale = NodeFacts.All.Keys.Where(t => !kinds.Contains(t)).Select(t => t.Name).ToArray();
        await Assert.That(stale).IsEmpty()
            .Because(Explain("a fact for a type that is not a concrete Node is stale", stale));
    }

    /// <summary>
    /// The effects column is an independent statement of what each kind does, so a new row or a
    /// reclassification cannot slip through as a local decision on one consumer. Every kind here can
    /// reach user code or a runtime exception; the operator kinds are deliberate over-approximations,
    /// because the same node reaches a decimal or user-defined operator and the table sees kinds.
    /// </summary>
    [Test]
    public async Task ThrowingKinds_AreTheExpectedSet() {
        string[] expected = [
            nameof(Add), nameof(Await), nameof(BitwiseAnd), nameof(BitwiseNot), nameof(BitwiseOr),
            nameof(BitwiseXor), nameof(Divide), nameof(Equal), nameof(GreaterThan),
            nameof(GreaterThanOrEqual), nameof(IndexAccess), nameof(InterpolatedString),
            nameof(Invoke), nameof(LessThan), nameof(LessThanOrEqual), nameof(Member), nameof(Modulo),
            nameof(Multiply), nameof(New), nameof(NewArray), nameof(Not), nameof(NotEqual),
            nameof(ShiftLeft), nameof(ShiftRight), nameof(StridedSetBits), nameof(Subtract),
            nameof(ThrowExpression), nameof(ThrowStatement), nameof(TypeCast), nameof(UnaryMinus),
            nameof(UsingStatement), nameof(ForEachLoop),
        ];
        var actual = Ordered().Where(kv => (kv.Value.Effects & NodeEffect.Throws) != 0).Select(kv => kv.Value.Label);
        await Assert.That(string.Join(", ", actual.OrderBy(n => n, StringComparer.Ordinal)))
            .IsEqualTo(string.Join(", ", expected.OrderBy(n => n, StringComparer.Ordinal)))
            .Because("a kind's throwing decision is part of the table's contract, not a local choice");
    }

    /// <summary>The state half of the column, asserted the same way: it is what a consumer reads to
    /// decide that a condition can change, and a new kind defaulting to "no state effect" is the
    /// direction that silently deletes code.</summary>
    [Test]
    public async Task StateEffectKinds_AreTheExpectedSet() {
        string[] expected = [
            nameof(Assignment), nameof(Return), nameof(WhileLoop), nameof(DoWhileLoop), nameof(ForLoop),
            nameof(BreakStatement), nameof(ContinueStatement), nameof(GotoStatement),
            nameof(LabelDeclaration), nameof(IndexAccess), nameof(Member), nameof(New),
            nameof(NewArray), nameof(StridedSetBits), nameof(ForEachLoop), nameof(Invoke),
            nameof(Await), nameof(UsingStatement), nameof(IfStatement),
            nameof(SwitchStatement), nameof(SuspendNode), nameof(YieldStatement),
        ];
        var actual = Ordered()
            .Where(kv => (kv.Value.Effects & ~NodeEffect.Throws) != NodeEffect.None)
            .Select(kv => kv.Value.Label);
        await Assert.That(string.Join(", ", actual.OrderBy(n => n, StringComparer.Ordinal)))
            .IsEqualTo(string.Join(", ", expected.OrderBy(n => n, StringComparer.Ordinal)))
            .Because("the state half of the column is a claim too");
    }

    /// <summary>A claim with no corpus case is either a missing case or a false claim.</summary>
    [Test]
    public async Task EveryClaim_HasCorpusEvidence() {
        var unbacked = Ordered()
            .Where(kv => (kv.Value.Producers != Producers.None || kv.Value.RejectReason is not null)
                && !NodeFactCorpus.Cases.Any(c => c.Kind == kv.Key))
            .ToArray();
        await Assert.That(Names(unbacked)).IsEmpty()
            .Because(Explain("a claim with no corpus case is a missing case or a false claim", Names(unbacked)));
    }

    [Test]
    public async Task CorpusKind_ClaimedByVm_Compiles() {
        var unproven = Claiming(Producers.Vm)
            .Where(kv => !NodeFactCorpus.CompilingProgramKinds.Contains(kv.Key))
            .ToArray();
        await Assert.That(Names(unproven)).IsEmpty()
            .Because(Explain("a Vm-claimed kind needs a corpus program the VM accepts", Names(unproven)));
    }

    [Test]
    public async Task CorpusKind_ClaimedByWriter_RoundTrips() {
        var lost = new Dictionary<Type, string>();
        var read = new HashSet<Type>();
        var unread = new List<string>();
        foreach (var (source, unit) in NodeFactCorpus.Units.Where(u => NodeFactCorpus.IsWriterSubject(u.Source))) {
            CompilationUnitNode written;
            try {
                written = CSharpLanguage.Read(CSharpLanguage.Write(unit));
            }
            catch (Exception ex) {
                unread.Add($"{source}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            var writtenKinds = NodeFactCorpus.KindsOf(written);
            foreach (var kind in NodeFactCorpus.KindsOf(unit)) {
                read.Add(kind);
                if (!writtenKinds.Contains(kind))
                    lost.TryAdd(kind, source);
            }
        }
        await Assert.That(unread).IsEmpty()
            .Because(Explain("the writer's own output must read back", unread));

        var claimed = Claiming(Producers.Writer);
        var dropped = claimed.Where(kv => lost.ContainsKey(kv.Key))
            .Select(kv => $"{kv.Value.Label} lost in {lost[kv.Key]}").ToArray();
        await Assert.That(dropped).IsEmpty()
            .Because(Explain("a Writer-claimed kind must survive Write then Read", dropped));

        var unevidenced = claimed.Where(kv => !read.Contains(kv.Key)).ToArray();
        await Assert.That(Names(unevidenced)).IsEmpty()
            .Because(Explain("a Writer-claimed kind needs a corpus text that contains it", Names(unevidenced)));
    }

    /// <summary>A kind-preserving print and an expanding print are different claims, and an
    /// expanding one is backed by the Expanded print law rather than by kind survival.</summary>
    [Test]
    public async Task CorpusKind_ClaimedByWriterExpanded_HasTheExpandedPrintLaw() {
        var claimed = Claiming(Producers.WriterExpanded).Select(kv => kv.Key)
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        var sampled = PrintedSubsetVmTests.ExpandedSamples.Keys
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        await Assert.That(sampled).IsEquivalentTo(claimed)
            .Because(Explain("a WriterExpanded claim needs a sample the Expanded print law executes",
                claimed.Select(t => t.Name)));

        var both = Claiming(Producers.WriterExpanded)
            .Where(kv => kv.Value.Producers.HasFlag(Producers.Writer))
            .Select(kv => kv.Value.Label).ToArray();
        await Assert.That(both).IsEmpty()
            .Because(Explain("a kind cannot both keep and lose its kind on print", both));
    }

    [Test]
    public async Task CorpusKind_ClaimedByReader_IsProduced() {
        var unproven = Claiming(Producers.Reader)
            .Where(kv => !NodeFactCorpus.ParsedKinds.Contains(kv.Key))
            .ToArray();
        await Assert.That(Names(unproven)).IsEmpty()
            .Because(Explain("a Reader-claimed kind needs corpus text that produces it", Names(unproven)));
    }

    [Test]
    public async Task RejectReason_MatchesTheThrownMessage() {
        var failures = new List<string>();
        foreach (var (kind, fact) in Ordered().Where(kv => kv.Value.RejectReason is not null)) {
            var corpusCase = NodeFactCorpus.Cases.SingleOrDefault(c => c.Kind == kind);
            if (corpusCase is null) {
                failures.Add($"{fact.Label}: no corpus case");
                continue;
            }
            Exception? thrown = null;
            try {
                Interpreter.Compile(corpusCase.Root);
            }
            catch (Exception ex) {
                thrown = ex;
            }
            if (thrown is null)
                failures.Add($"{fact.Label}: compiled instead of rejecting");
            else if (thrown is not InvalidOperationException
                || !thrown.Message.Contains(fact.RejectReason!, StringComparison.Ordinal))
                failures.Add($"{fact.Label}: rejected with {thrown.GetType().Name}: {thrown.Message}");
        }
        await Assert.That(failures).IsEmpty()
            .Because(Explain("a deliberate rejection must carry its recorded message", failures));
    }

    /// <summary>
    /// A kind the table calls <see cref="Producers.None"/> has no faithful producer: the VM
    /// rejects it, and the writer does not hand it back. An arm added without a claim shows up
    /// here as a writer that carries the kind.
    /// </summary>
    [Test]
    public async Task UnclaimedKind_IsRejectedEverywhere() {
        var failures = new List<string>();
        foreach (var (kind, fact) in Ordered().Where(
            kv => kv.Value.Producers == Producers.None && kv.Value.RejectReason is null)) {
            var corpusCase = NodeFactCorpus.Cases.SingleOrDefault(c => c.Kind == kind);
            if (corpusCase is null) {
                failures.Add($"{fact.Label}: no corpus case");
                continue;
            }
            try {
                Interpreter.Compile(corpusCase.Root);
                failures.Add($"{fact.Label}: the VM executes it");
            }
            catch (Exception) {
            }
            try {
                if (NodeFactCorpus.KindsOf(CSharpLanguage.Read(CSharpLanguage.Write(corpusCase.Root))).Contains(kind))
                    failures.Add($"{fact.Label}: the writer carries it");
            }
            catch (Exception) {
            }
        }
        await Assert.That(failures).IsEmpty()
            .Because(Explain("an unclaimed kind must be rejected by every producer", failures));
    }

    private static Type[] ConcreteKinds() =>
        [.. typeof(Node).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(Node).IsAssignableFrom(t)
                && t.Namespace is not null
                && (t.Namespace == "Poly.Ast" || t.Namespace.StartsWith("Poly.Ast.")))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

}