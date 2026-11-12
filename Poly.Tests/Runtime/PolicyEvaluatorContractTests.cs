using RuntimeDomainFailure = Poly.Runtime.DomainFailure;
using RuntimeDomainFailureKind = Poly.Runtime.DomainFailureKind;
using RuntimeDomainResultOfBool = Poly.Runtime.DomainResult<bool>;
using RuntimeIPolicyEvaluator = Poly.Runtime.IPolicyEvaluator;
using RuntimePolicyRequest = Poly.Runtime.PolicyRequest;
using RuntimePrincipal = Poly.Runtime.Principal;

namespace Poly.Tests.Runtime;

/// <summary>
/// R-09: the actor/principal contract in <c>Poly.Runtime</c>. These types ship before any
/// call site binds them (F-26), so the suite pins the ruled shape and its fail-closed
/// edges alone.
/// <para>
/// Tests 1–4 exercise the contract through a test double. They prove the types are usable
/// as a contract; they do not test evaluator behaviour, because R-09 ships no evaluator.
/// </para>
/// <para>
/// The file never adds <c>using Poly.Runtime;</c>: the namespace
/// <c>Poly.Tests.Runtime</c> shadows the <c>Runtime</c> segment, so the runtime types
/// are aliased or fully qualified.
/// </para>
/// </summary>
public class PolicyEvaluatorContractTests {
    [Test]
    public async Task Evaluate_PrincipalWithApproverRole_ReturnsSuccessTrue() {
        var evaluator = new RoleEvaluator();
        var request = new RuntimePolicyRequest(
            new RuntimePrincipal("alice", ["approver"]), "Order", new object(), "CanSubmit");

        var result = evaluator.Evaluate(request);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsTrue();
    }

    [Test]
    public async Task Evaluate_PrincipalWithoutApproverRole_ReturnsSuccessFalse() {
        var evaluator = new RoleEvaluator();
        var request = new RuntimePolicyRequest(
            new RuntimePrincipal("bob", ["reader"]), "Order", new object(), "CanSubmit");

        var result = evaluator.Evaluate(request);

        // A denial is a decision, not a failure.
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsFalse();
    }

    [Test]
    public async Task Evaluate_NullPrincipal_ReachesEvaluatorAndDenies() {
        var evaluator = new RoleEvaluator();
        var request = new RuntimePolicyRequest(null, "Order", new object(), "CanSubmit");

        var result = evaluator.Evaluate(request);

        await Assert.That(evaluator.LastRequest).IsSameReferenceAs(request);
        await Assert.That(evaluator.LastRequest!.Principal).IsNull();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsFalse();
    }

    [Test]
    public async Task Evaluate_CannotDecide_ReturnsFailureCarryingPolicyKind() {
        var evaluator = new CannotDecideEvaluator();
        var request = new RuntimePolicyRequest(
            new RuntimePrincipal("alice", ["approver"]), "Order", new object(), "CanSubmit");

        var result = evaluator.Evaluate(request);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Kind).IsEqualTo(RuntimeDomainFailureKind.Policy);
        await Assert.That(result.Error.Guard).IsEqualTo("CanSubmit");
    }

    [Test]
    public async Task Evaluate_IsSynchronousReturningDomainResultOfBool() {
        var methods = typeof(RuntimeIPolicyEvaluator).GetMethods();

        await Assert.That(methods.Length).IsEqualTo(1);

        var method = methods[0];
        var parameters = method.GetParameters();
        await Assert.That(method.Name).IsEqualTo("Evaluate");
        await Assert.That(parameters.Length).IsEqualTo(1);
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(RuntimePolicyRequest));
        await Assert.That(method.ReturnType).IsEqualTo(typeof(RuntimeDomainResultOfBool));
        await Assert.That(typeof(Task).IsAssignableFrom(method.ReturnType)).IsFalse();
    }

    [Test]
    public async Task PolicyRequest_CarriesPrincipalEntityInstancePolicyAndArguments() {
        var principal = new RuntimePrincipal("alice", ["approver"], new Dictionary<string, string> { ["team"] = "core" });
        var instance = new object();
        var request = new RuntimePolicyRequest(principal, "Order", instance, "CanSubmit", [1, null, "x"]);

        await Assert.That(request.Principal).IsSameReferenceAs(principal);
        await Assert.That(request.Entity).IsEqualTo("Order");
        await Assert.That(request.Instance).IsSameReferenceAs(instance);
        await Assert.That(request.Policy).IsEqualTo("CanSubmit");
        await Assert.That(request.Arguments.Count).IsEqualTo(3);
        await Assert.That(request.Arguments[1]).IsNull();

        // The ruling does not fix a null default for arguments; R-09 makes it an empty list
        // so a zero-parameter policy needs no Array.Empty at the call site.
        var defaulted = new RuntimePolicyRequest(principal, "Order", instance, "CanSubmit");
        await Assert.That(defaulted.Arguments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PolicyRequest_Arguments_AreAReadOnlyCopy() {
        var arguments = new List<object?> { 1, 2 };
        var request = new RuntimePolicyRequest(
            new RuntimePrincipal("alice"), "Order", new object(), "CanSubmit", arguments);

        arguments.Clear();

        await Assert.That(request.Arguments.Count).IsEqualTo(2);
        await Assert.That(request.Arguments is Array).IsFalse();
        await Assert.That(() => { ((IList<object?>)request.Arguments)[0] = 9; }).Throws<NotSupportedException>();
    }

    [Test]
    public async Task PolicyRequest_NullEntityInstanceOrPolicy_Throws() {
        var principal = new RuntimePrincipal("alice");
        var instance = new object();

        await Assert.That(() => new RuntimePolicyRequest(principal, null!, instance, "CanSubmit"))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new RuntimePolicyRequest(principal, "", instance, "CanSubmit"))
            .Throws<ArgumentException>();
        await Assert.That(() => new RuntimePolicyRequest(principal, "  ", instance, "CanSubmit"))
            .Throws<ArgumentException>();
        await Assert.That(() => new RuntimePolicyRequest(principal, "Order", null!, "CanSubmit"))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new RuntimePolicyRequest(principal, "Order", instance, null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new RuntimePolicyRequest(principal, "Order", instance, ""))
            .Throws<ArgumentException>();
        await Assert.That(() => new RuntimePolicyRequest(principal, "Order", instance, "  "))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Principal_NullOrWhitespaceId_Throws() {
        await Assert.That(() => new RuntimePrincipal(null!)).Throws<ArgumentException>();
        await Assert.That(() => new RuntimePrincipal("")).Throws<ArgumentException>();
        await Assert.That(() => new RuntimePrincipal("  ")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Principal_RolesAndClaims_AreReadOnlyCopies() {
        var roles = new List<string> { "reader" };
        var claims = new Dictionary<string, string> { ["team"] = "core" };
        var principal = new RuntimePrincipal("alice", roles, claims);

        roles.Add("admin");
        claims["team"] = "other";

        await Assert.That(principal.Roles.Count).IsEqualTo(1);
        await Assert.That(principal.Claims["team"]).IsEqualTo("core");
        await Assert.That(principal.Roles is Array).IsFalse();
        await Assert.That(() => { ((IList<string>)principal.Roles)[0] = "admin"; }).Throws<NotSupportedException>();
        await Assert.That(() => { ((IDictionary<string, string>)principal.Claims)["team"] = "other"; })
            .Throws<NotSupportedException>();
        await Assert.That(() => { ((IDictionary<string, string>)principal.Claims).Add("role", "admin"); })
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task Principal_Claims_KeysAreOrdinalAndCaseSensitive() {
        var claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["role"] = "approver" };
        var principal = new RuntimePrincipal("alice", null, claims);

        // Copying drops the caller's comparer; the contract's key comparer is ordinal.
        await Assert.That(principal.Claims.ContainsKey("role")).IsTrue();
        await Assert.That(principal.Claims.ContainsKey("ROLE")).IsFalse();
    }

    [Test]
    public async Task Principal_NullRoleOrClaimValue_Throws() {
        await Assert.That(() => new RuntimePrincipal("alice", ["reader", null!])).Throws<ArgumentException>();
        await Assert.That(() => new RuntimePrincipal("alice", null, new Dictionary<string, string> { ["role"] = null! }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PolicyTypes_LiveInPolyRuntimeAssemblyAndNamespace() {
        await Assert.That(typeof(RuntimePrincipal).Assembly.GetName().Name).IsEqualTo("Poly.Runtime");
        await Assert.That(typeof(RuntimePolicyRequest).Assembly.GetName().Name).IsEqualTo("Poly.Runtime");
        await Assert.That(typeof(RuntimeIPolicyEvaluator).Assembly.GetName().Name).IsEqualTo("Poly.Runtime");

        await Assert.That(typeof(RuntimePrincipal).Namespace).IsEqualTo("Poly.Runtime");
        await Assert.That(typeof(RuntimePolicyRequest).Namespace).IsEqualTo("Poly.Runtime");
        await Assert.That(typeof(RuntimeIPolicyEvaluator).Namespace).IsEqualTo("Poly.Runtime");
    }

    /// <summary>A test double: approves iff the principal carries the <c>approver</c> role.</summary>
    sealed class RoleEvaluator : RuntimeIPolicyEvaluator {
        public RuntimePolicyRequest? LastRequest { get; private set; }

        public RuntimeDomainResultOfBool Evaluate(RuntimePolicyRequest request) {
            LastRequest = request;
            var allowed = request.Principal?.Roles.Contains("approver", StringComparer.Ordinal) == true;
            return RuntimeDomainResultOfBool.Success(allowed);
        }
    }

    /// <summary>A test double for cannot-decide: a <c>Policy</c> failure, never a decision.</summary>
    sealed class CannotDecideEvaluator : RuntimeIPolicyEvaluator {
        public RuntimeDomainResultOfBool Evaluate(RuntimePolicyRequest request) =>
            RuntimeDomainResultOfBool.Failure(RuntimeDomainFailure.Policy("cannot decide", null, request.Policy));
    }
}