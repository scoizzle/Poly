using System.Globalization;

using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// The one writer for constraint-validation guards shared by the Create factory
/// and the assign composer. It owns the condition, message, presence predicate,
/// ordering, and type gates for required, range, length, pattern, equality, and
/// the enum slot/member checks. Call sites own only the failure node and whether
/// length checks sit in a Block.
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
        EnumRequiredSlot,
        EnumMember,
    }

    /// <summary>
    /// One guard. <see cref="Condition"/> is the violation condition without the
    /// applicability guard; <see cref="Presence"/> is the guard that makes the
    /// check applicable (create optional <c>value != null</c>, assign length
    /// <c>!IsNullOrEmpty(value)</c>, assign pattern <c>value != null</c>), or null
    /// when the check always applies.
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

        /// <summary>Assign: length groups under <c>!IsNullOrEmpty</c>, pattern under <c>value != null</c>.</summary>
        AssignValue,
    }

    /// <summary>The order a property's checks take in the emitted tree.</summary>
    internal enum CheckOrder {
        /// <summary>Declaration order, then the enum guards (create).</summary>
        Declaration,

        /// <summary>Required, range, length, pattern, equality, enum (assign's historical order).</summary>
        Category,
    }

    /// <summary>
    /// A named output dialect, not a consumer-named flag. Create prints
    /// <c>string.Is…</c> and names the offending enum value; assign prints
    /// <c>System.String.Is…</c>, names the property only, and carries no enum slot guard.
    /// Presence and order are data on the dialect, so a third style cannot silently
    /// inherit the other one's rules.
    /// </summary>
    internal sealed record ConstraintCheckStyle(
        Func<Node> StringStatic,
        Func<Node> RegexStatic,
        bool NamesEnumValue,
        bool GuardsEnumSlot,
        PresencePolicy Presence,
        CheckOrder Order) {
        internal static readonly ConstraintCheckStyle Create = new(
            () => new PrimitiveTypeReference(PrimType.String),
            () => new NamedTypeReference("System.Text.RegularExpressions.Regex"),
            NamesEnumValue: true,
            GuardsEnumSlot: true,
            Presence: PresencePolicy.CreateSlot,
            Order: CheckOrder.Declaration);

        internal static readonly ConstraintCheckStyle Assign = new(
            () => TypeReference.To<string>(),
            () => TypeReference.To<System.Text.RegularExpressions.Regex>(),
            NamesEnumValue: false,
            GuardsEnumSlot: false,
            Presence: PresencePolicy.AssignValue,
            Order: CheckOrder.Category);
    }

    /// <summary>
    /// Requiredness is the one published fact: a property is required at create
    /// unless it carries <c>optional</c>, then the text/nullable type gate decides
    /// whether a value check applies. Reads <see cref="EntityStructureMetadata.RequiredPropertyNames"/>
    /// with the <see cref="EntityStructureAnalyzer.ComputeRequiredPropertyNames"/>
    /// fallback; does not read <c>DefaultValueConstraint</c> or <c>RequiredPropertiesMetadata</c>.
    /// </summary>
    internal static bool RequiredApplies(
        Entity entity, Property prop, Domain? domain, INodeMetadataProvider? analysis) {
        var required = analysis?.GetStructure(entity)?.RequiredPropertyNames
            ?? EntityStructureAnalyzer.ComputeRequiredPropertyNames(entity);
        if (!required.Contains(prop.Name))
            return false;
        if (IsText(prop))
            return true;
        return IsNullableDomainType(prop.Type.TypeName, domain);
    }

    /// <summary>
    /// All constraint checks for one property. The style selects the order: create
    /// uses declaration order (required, then constraints, then enum); assign uses
    /// its historical category order (required, range, length, pattern, equality, enum).
    /// <paramref name="nullableValue"/> is the create slot's optionality;
    /// <paramref name="valueHasDefault"/> suppresses the create enum slot guard;
    /// <paramref name="includeRequired"/> is create's <c>!default</c> gate (assign
    /// always passes true).
    /// </summary>
    internal static IReadOnlyList<Check> ChecksFor(
        Entity entity,
        Property prop,
        Domain? domain,
        INodeMetadataProvider? analysis,
        ConstraintCheckStyle style,
        Node value,
        bool nullableValue,
        bool valueHasDefault,
        bool includeRequired) {
        var checks = new List<Check>();
        var isText = IsText(prop);
        var isNumber = IsNumber(prop);
        var isAssign = style.Presence == PresencePolicy.AssignValue;

        // Create optional slot: value constraints only apply when a value was supplied.
        Node? CreatePresence() =>
            !isAssign && nullableValue ? new NotEqual(value, new Constant(null)) : null;

        if (includeRequired && RequiredApplies(entity, prop, domain, analysis)) {
            checks.Add(isText
                ? new Check(Kind.Required,
                    new Invoke(new Member(style.StringStatic(), "IsNullOrEmpty"), [value]),
                    null,
                    new Constant($"'{prop.Name}' is required."))
                : new Check(Kind.Required,
                    new Equal(value, new Constant(null)),
                    null,
                    new Constant($"'{prop.Name}' is required.")));
        }

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
                    // Assign optional length groups under one !IsNullOrEmpty guard; create
                    // carries per-check presence instead.
                    var lengthPresence = isAssign
                        ? (RequiredApplies(entity, prop, domain, analysis)
                            ? null
                            : new Syntactic.Not(
                                new Invoke(new Member(style.StringStatic(), "IsNullOrEmpty"), [value])))
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
                                [value, new Constant(p.Pattern)])),
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
            if (style.GuardsEnumSlot && !valueHasDefault) {
                checks.Add(new Check(Kind.EnumRequiredSlot,
                    new Invoke(
                        new Member(new ClrTypeReference(typeof(object)), "ReferenceEquals"),
                        [value, new Constant(null)]),
                    null,
                    new Constant($"'{prop.Name}' is required.")));
            }

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

    /// <summary>Assign's historical pass order: required, range, length, pattern, equality, enum.</summary>
    private static int CategoryRank(Kind kind) => kind switch {
        Kind.Required => 0,
        Kind.RangeMinimum or Kind.RangeMaximum => 1,
        Kind.LengthMinimum or Kind.LengthMaximum => 2,
        Kind.Pattern => 3,
        Kind.Equality => 4,
        Kind.EnumRequiredSlot => 5,
        Kind.EnumMember => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unranked constraint check kind.")
    };

    /// <summary>Converts a constraint bound to a Syntax <see cref="Constant"/> (merged numeric converters).</summary>
    internal static Constant? ToConstant(object? value) {
        if (value is null) return null;
        if (value is long l) return new Constant(l);
        if (value is int i) return new Constant((long)i);
        if (value is double d) return d == Math.Floor(d) ? new Constant((long)d) : new Constant(d);
        if (value is decimal m) return new Constant((double)m);
        if (value is string s) return new Constant(s);
        if (value is bool b) return new Constant(b);
        return new Constant(value.ToString());
    }

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
        || string.Equals(prop.Type.TypeName, "Int", StringComparison.Ordinal);

    /// <summary>True when the domain type maps to a nullable CLR type (string or reference type).</summary>
    private static bool IsNullableDomainType(string typeName, Domain? domain) {
        if (domain is not null
            && domain.Types.OfType<EnumType>().Any(e => string.Equals(e.Name, typeName, StringComparison.Ordinal)))
            return false;
        return !DomainTypeMapping.IsNonNullableClrValueType(
            RuntimeAnalysisCache.ClrTypeName(domain, typeName));
    }
}