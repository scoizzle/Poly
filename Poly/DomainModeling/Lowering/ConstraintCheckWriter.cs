using System.Globalization;

using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Runtime;

using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// The one writer for constraint-validation guards shared by the Create factory
/// and the assign composer. It owns the condition, message, presence predicate,
/// ordering, and type gates for required, range, length, pattern, equality, and
/// the enum slot/member checks. Call sites own only the failure node and whether
/// length checks sit in a Block. <c>unique</c> stays storage-owned (create in
/// <c>UniqueConstraintCompiler.Compile</c>, assign via its <c>AssignObligations</c>
/// and <c>AfterApply</c>); <see cref="CoverageByConstraint"/> names that owner so
/// it cannot bypass the one-writer claim silently.
/// </summary>
internal static class ConstraintCheckWriter {
    internal enum Kind {
        Required,
        RangeMinimum,
        RangeMaximum,
        LengthMinimum,
        LengthMaximum,
        Pattern,
        Equality,
        EnumMember,
    }

    /// <summary>
    /// One guard. <see cref="Condition"/> is the violation condition without the
    /// applicability guard; <see cref="Presence"/> is the guard that makes the
    /// check applicable (create optional <c>value != null</c>, assign optional length
    /// and pattern <c>value != null</c>), or null when the check always applies.
    /// </summary>
    internal sealed record Check(Kind Kind, Node Condition, Node? Presence, Node Message) {
        /// <summary><c>Presence &amp;&amp; Condition</c>, or <c>Condition</c> alone.</summary>
        internal Node GuardedCondition =>
            Presence is null ? Condition : new Syntactic.And(Presence, Condition);
    }

    /// <summary>How a value check gets its applicability guard.</summary>
    internal enum PresencePolicy {
        /// <summary>Create optional slot: every value check is <c>value != null &amp;&amp; …</c>.</summary>
        CreateSlot,

        /// <summary>Assign: an optional value's length checks group under <c>value != null</c>, pattern under <c>value != null</c>.</summary>
        AssignValue,
    }

    /// <summary>The order a property's checks take in the emitted tree.</summary>
    internal enum CheckOrder {
        /// <summary>Declaration order, then the enum member guard (create).</summary>
        Declaration,

        /// <summary>Required, range, length, pattern, equality, enum (assign's historical order).</summary>
        Category,
    }

    /// <summary>
    /// A named output dialect, not a consumer-named flag. Create prints
    /// <c>string.…</c> and names the offending enum value; assign prints
    /// <c>System.String.…</c> and names the property only.
    /// Presence and order are data on the dialect, so a third style cannot silently
    /// inherit the other one's rules.
    /// </summary>
    internal sealed record ConstraintCheckStyle(
        Func<Node> StringStatic,
        Func<Node> RegexStatic,
        bool NamesEnumValue,
        PresencePolicy Presence,
        CheckOrder Order) {
        internal static readonly ConstraintCheckStyle Create = new(
            () => new PrimitiveTypeReference(PrimType.String),
            () => new NamedTypeReference("System.Text.RegularExpressions.Regex"),
            NamesEnumValue: true,
            Presence: PresencePolicy.CreateSlot,
            Order: CheckOrder.Declaration);

        internal static readonly ConstraintCheckStyle Assign = new(
            () => TypeReference.To<string>(),
            () => TypeReference.To<System.Text.RegularExpressions.Regex>(),
            NamesEnumValue: false,
            Presence: PresencePolicy.AssignValue,
            Order: CheckOrder.Category);
    }

    /// <summary>
    /// Requiredness is the one published fact: a property is required unless it
    /// carries <c>optional</c>, and required means non-null for every type. An assigned
    /// value of a non-nullable CLR value type has no null to refuse, so the gate is
    /// whether the value can be null. Reads <see cref="EntityStructureMetadata.RequiredPropertyNames"/>;
    /// a missing structure bag throws — it does not recompute. Does not read
    /// <c>DefaultValueConstraint</c> or <c>RequiredPropertiesMetadata</c>.
    /// </summary>
    internal static bool RequiredApplies(
        Entity entity, Property prop, Domain domain, INodeMetadataProvider analysis) {
        var structure = analysis.GetStructure(entity)
            ?? throw new InvalidOperationException(
                $"EntityStructureMetadata is required to decide requiredness of '{entity.Name}.{prop.Name}'; analysis published none.");
        var required = structure.RequiredPropertyNames;
        if (!required.Contains(prop.Name))
            return false;
        if (IsText(prop))
            return true;
        return IsNullableDomainType(prop.Type.TypeName, domain);
    }

    /// <summary>
    /// All constraint checks for one property. The style selects the order: create
    /// uses declaration order (constraints, then enum membership); assign uses
    /// its historical category order (required, range, length, pattern, equality, enum).
    /// <paramref name="nullableValue"/> is the create slot's optionality;
    /// <paramref name="includeRequired"/> is false on create, where <see cref="RequiredSlotCheck"/>
    /// guards every Required slot before its value is read (assign passes true).
    /// </summary>
    internal static IReadOnlyList<Check> ChecksFor(
        Entity entity,
        Property prop,
        Domain domain,
        INodeMetadataProvider analysis,
        ConstraintCheckStyle style,
        Node value,
        bool nullableValue,
        bool includeRequired) {
        var checks = new List<Check>();
        var isText = IsText(prop);
        var isNumber = IsNumber(prop);
        var isAssign = style.Presence == PresencePolicy.AssignValue;

        // Create optional slot: value constraints only apply when a value was supplied.
        Node? CreatePresence() =>
            !isAssign && nullableValue ? new NotEqual(value, new Constant(null)) : null;

        if (includeRequired && RequiredApplies(entity, prop, domain, analysis))
            checks.Add(RequiredCheck(prop.Name, value));

        foreach (var constraint in prop.Constraints) {
            switch (constraint) {
                case RangeConstraint r when isNumber:
                    if (r.Minimum is not null && ToConstant(r.Minimum) is { } minVal)
                        checks.Add(new Check(Kind.RangeMinimum,
                            new LessThan(value, minVal),
                            CreatePresence(),
                            new Constant($"'{prop.Name}' must be >= {FormatConstraintValue(r.Minimum)}.")));
                    if (r.Maximum is not null && ToConstant(r.Maximum) is { } maxVal)
                        checks.Add(new Check(Kind.RangeMaximum,
                            new GreaterThan(value, maxVal),
                            CreatePresence(),
                            new Constant($"'{prop.Name}' must be <= {FormatConstraintValue(r.Maximum)}.")));
                    break;

                case LengthConstraint l when isText:
                    var lenAccess = new Member(value, "Length");
                    // Assign optional length groups under one `value != null` guard (an empty
                    // string is a value, so its length is checked); create carries per-check
                    // presence instead.
                    var lengthPresence = isAssign
                        ? (RequiredApplies(entity, prop, domain, analysis)
                            ? null
                            : new NotEqual(value, new Constant(null)))
                        : CreatePresence();
                    if (l.MinLength > 0)
                        checks.Add(new Check(Kind.LengthMinimum,
                            new LessThan(lenAccess, new Constant((long)l.MinLength)),
                            lengthPresence,
                            new Constant($"'{prop.Name}' must be at least {l.MinLength} characters.")));
                    if (l.MaxLength < int.MaxValue)
                        checks.Add(new Check(Kind.LengthMaximum,
                            new GreaterThan(lenAccess, new Constant((long)l.MaxLength)),
                            lengthPresence,
                            new Constant($"'{prop.Name}' must be at most {l.MaxLength} characters.")));
                    break;

                case PatternConstraint p when isText:
                    // Assign null-guards IsMatch unconditionally; create only for optional slots.
                    var patternPresence = isAssign
                        ? new NotEqual(value, new Constant(null))
                        : CreatePresence();
                    checks.Add(new Check(Kind.Pattern,
                        new Syntactic.Not(
                            new Invoke(
                                new Member(style.RegexStatic(), "IsMatch"),
                                [value, new Constant(AnchorEnd(p.Pattern))])),
                        patternPresence,
                        new Constant($"'{prop.Name}' does not match the required pattern.")));
                    break;

                case EqualityConstraint eq:
                    if (eq.ExpectedValue is not null)
                        checks.Add(new Check(Kind.Equality,
                            new NotEqual(value, new Constant(eq.ExpectedValue)),
                            CreatePresence(),
                            new Constant($"'{prop.Name}' must equal {eq.ExpectedValue}.")));
                    break;
            }
        }

        if (DomainToCSharpExporter.TryResolveEnumType(domain, analysis, prop.Type.TypeName, out var enumType)
            && enumType is { MemberNames.Count: > 0 }) {
            Node? notMember = null;
            var enumTypeRef = new NamedTypeReference(enumType.Name);
            foreach (var member in enumType.MemberNames) {
                var ne = new NotEqual(value, new Member(enumTypeRef, member));
                notMember = notMember is null ? ne : new Syntactic.And(notMember, ne);
            }
            var message = style.NamesEnumValue
                ? (Node)new Invoke(new Member(style.StringStatic(), "Concat"), [
                    new Constant("'"),
                    value,
                    new Constant($"' is not a valid member of {enumType.Name} for property '{prop.Name}'.")])
                : new Constant($"'{prop.Name}' is not a valid member of {enumType.Name}.");
            checks.Add(new Check(Kind.EnumMember, notMember!, CreatePresence(), message));
        }

        if (style.Order == CheckOrder.Category)
            checks = [.. checks.OrderBy(c => CategoryRank(c.Kind))];
        return checks;
    }

    /// <summary>
    /// The create guard for a Required slot of any shape (scalar, navigation or collection):
    /// <c>value == null</c> fails <c>Required</c> with <c>'{Name}' is required.</c> on <c>Entity.{Name}</c>.
    /// It runs before the slot's value is read, so the slot's constraint checks see a non-null value.
    /// A slot kind decides whether it applies; the slot's type and shape never do.
    /// </summary>
    internal static Check RequiredSlotCheck(CreateSlot slot, Node value) => RequiredCheck(slot.Name, value);

    private static Check RequiredCheck(string name, Node value) =>
        new(Kind.Required, new Equal(value, new Constant(null)), null, new Constant($"'{name}' is required."));

    /// <summary>
    /// The writer's coverage of <see cref="Constraint"/> records. <see cref="Coverage.Kinds"/>
    /// are the checks <see cref="ChecksFor"/> can emit for that record; an empty list means
    /// this writer emits no guard, and <see cref="Coverage.Owner"/> must name the writer
    /// that does (or, with a <c>marker:</c> prefix, say why no guard exists). A record with
    /// neither fails <c>ConstraintWriterCoverageTests</c>, so a new constraint cannot bypass
    /// this writer by adding another switch arm. A <see cref="Coverage.Marker"/> row is a
    /// fact no guard can express, so it needs no library compiler; every other row with no
    /// kinds names a writer whose library must be loaded (<c>DMLIB001</c> otherwise).
    /// </summary>
    internal static readonly IReadOnlyDictionary<Type, Coverage> CoverageByConstraint =
        new Dictionary<Type, Coverage> {
            [typeof(RequiredConstraint)] = new([Kind.Required], null),
            [typeof(RangeConstraint)] = new([Kind.RangeMinimum, Kind.RangeMaximum], null),
            [typeof(LengthConstraint)] = new([Kind.LengthMinimum, Kind.LengthMaximum], null),
            [typeof(PatternConstraint)] = new([Kind.Pattern], null),
            [typeof(EqualityConstraint)] = new([Kind.Equality], null),
            [typeof(UniqueConstraint)] = new([],
                "storage port: create guard in UniqueConstraintCompiler.Compile, assign via AssignObligations + AfterApply"),
            [typeof(OptionalConstraint)] = new([],
                "marker: optionality is a requiredness fact, not a guard", Marker: true),
            [typeof(DefaultValueConstraint)] = new([],
                "marker: the default feeds requiredness and default lowering, not a guard", Marker: true),
            [typeof(RelationalConstraint)] = new([],
                "marker: an analysis-derived relation between two properties (#367 A-6); policies lower the guard, not this fact",
                Marker: true),
        };

    /// <summary>
    /// One constraint record's check kinds, or the writer that owns its guard instead.
    /// <see cref="Marker"/> says the record is a fact no guard can express (optionality,
    /// a default, a derived relation), so no library has to compile it; a row with no
    /// kinds and no marker names a writer, and needs that writer's library loaded.
    /// </summary>
    internal sealed record Coverage(IReadOnlyList<Kind> Kinds, string? Owner, bool Marker = false);

    /// <summary>The checks this writer emits for <paramref name="constraint"/>, empty when another writer owns it.</summary>
    internal static IReadOnlyList<Kind> KindsFor(Constraint constraint) =>
        CoverageByConstraint.TryGetValue(constraint.GetType(), out var coverage)
            ? coverage.Kinds
            : [];

    /// <summary>The closed <see cref="DomainFailureKind"/> a failing check reports; equality is a <c>Constraint</c>.</summary>
    internal static DomainFailureKind FailureKindOf(Kind kind) => kind switch {
        Kind.Required => DomainFailureKind.Required,
        Kind.RangeMinimum or Kind.RangeMaximum => DomainFailureKind.Range,
        Kind.LengthMinimum or Kind.LengthMaximum => DomainFailureKind.Length,
        Kind.Pattern => DomainFailureKind.Pattern,
        Kind.Equality => DomainFailureKind.Constraint,
        Kind.EnumMember => DomainFailureKind.Enum,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Constraint check kind has no failure kind.")
    };

    /// <summary>The failure <paramref name="check"/> reports, on <c>Entity.Property</c>; no guard (a constraint check carries none).</summary>
    internal static FailureLowering.Spec FailureSpec(Check check, string entityName, string propertyName) =>
        new(FailureKindOf(check.Kind), check.Message, $"{entityName}.{propertyName}");

    /// <summary>Assign's historical pass order: required, range, length, pattern, equality, enum.</summary>
    private static int CategoryRank(Kind kind) => kind switch {
        Kind.Required => 0,
        Kind.RangeMinimum or Kind.RangeMaximum => 1,
        Kind.LengthMinimum or Kind.LengthMaximum => 2,
        Kind.Pattern => 3,
        Kind.Equality => 4,
        Kind.EnumMember => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unranked constraint check kind.")
    };

    /// <summary>Converts a constraint bound to a Syntax <see cref="Constant"/> (merged numeric converters).</summary>
    internal static Constant? ToConstant(object? value) {
        if (value is null) return null;
        if (value is long l) return new Constant(l);
        if (value is int i) return new Constant((long)i);
        if (value is double d) return d == Math.Floor(d) ? new Constant((long)d) : new Constant(d);
        if (value is decimal m) return new Constant(m);
        if (value is string s) return new Constant(s);
        if (value is bool b) return new Constant(b);
        return new Constant(value.ToString());
    }

    /// <summary>
    /// Anchors a pattern constraint at the absolute end of the input. Delegates to
    /// <see cref="PatternSemantics.EndAnchored"/>, the one end-anchor semantic shared
    /// by Analyze, Lower and the contract boundary check.
    /// </summary>
    internal static string AnchorEnd(string pattern) => PatternSemantics.EndAnchored(pattern);

    /// <summary>Formats a constraint bound for a validation message. Invariant, so the message
    /// text is the same whatever the culture that simulates or emits it.</summary>
    internal static string FormatConstraintValue(object? value) => value switch {
        null => "?",
        double d => d == Math.Floor(d) ? d.ToString("F0", CultureInfo.InvariantCulture) : d.ToString("G", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?"
    };

    private static bool IsText(Property prop) =>
        string.Equals(prop.Type.TypeName, "Text", StringComparison.Ordinal)
        || string.Equals(prop.Type.TypeName, "String", StringComparison.Ordinal);

    private static bool IsNumber(Property prop) =>
        string.Equals(prop.Type.TypeName, "Number", StringComparison.Ordinal)
        || string.Equals(prop.Type.TypeName, "Int", StringComparison.Ordinal)
        || string.Equals(prop.Type.TypeName, "Decimal", StringComparison.Ordinal);

    /// <summary>True when the domain type maps to a nullable CLR type (string or reference type).</summary>
    private static bool IsNullableDomainType(string typeName, Domain domain) {
        if (domain.Types.OfType<EnumType>().Any(e => string.Equals(e.Name, typeName, StringComparison.Ordinal)))
            return false;
        return !DomainTypeMapping.IsNonNullableClrValueType(
            RuntimeAnalysisCache.ClrTypeName(domain, typeName));
    }
}