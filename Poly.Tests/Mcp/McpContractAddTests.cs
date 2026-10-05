using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>MCP <c>add</c> of the contract kinds reports what it did to the session.</summary>
public class McpContractAddTests {
    private static (string SessionId, long Revision) SessionWithContract() {
        var (sessionId, _) = McpSessionStore.Create("Billing");
        var added = EvolveTool.Add(sessionId, "contract", """{"name":"Stripe","source":"stripe","version":"v1"}""");
        if (!added.Success) throw new InvalidOperationException(added.Message);
        return (sessionId, Revision(sessionId));
    }

    private static long Revision(string sessionId) {
        McpSessionStore.TryGet(sessionId, out var state);
        return state.Revision;
    }

    private static ImportedContract Stripe(string sessionId) {
        McpSessionStore.TryGet(sessionId, out var state);
        return state.Domain.ImportedContracts.Single(c => c.Name == "Stripe");
    }

    [Test]
    public async Task Add_Contract_ReportsSuccessAndStoresIt() {
        var (sessionId, _) = McpSessionStore.Create("Billing");
        var before = Revision(sessionId);

        var response = EvolveTool.Add(sessionId, "contract", """{"name":"Stripe","source":"stripe","version":"v1"}""");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Revision).IsEqualTo(before + 1);
        await Assert.That(Revision(sessionId)).IsEqualTo(before + 1);
        await Assert.That(Stripe(sessionId).Version).IsEqualTo("v1");
    }

    [Test]
    public async Task Add_ContractValueType_ReportsSuccessAndStoresIt() {
        var (sessionId, before) = SessionWithContract();

        var response = EvolveTool.Add(sessionId, "contract_value_type", """{"contractName":"Stripe","name":"ChargeRequest"}""");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(Revision(sessionId)).IsEqualTo(before + 1);
        await Assert.That(Stripe(sessionId).Types.Select(t => t.Name)).Contains("ChargeRequest");
    }

    [Test]
    public async Task Add_ContractEndpoint_ReportsSuccessAndStoresIt() {
        var (sessionId, before) = SessionWithContract();

        var response = EvolveTool.Add(sessionId, "contract_endpoint",
            """{"contractName":"Stripe","name":"Charge","payloadType":"Number"}""");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(Revision(sessionId)).IsEqualTo(before + 1);
        await Assert.That(Stripe(sessionId).Endpoints.Select(e => e.Name)).Contains("Charge");
    }

    [Test]
    public async Task Add_ContractBinding_ReportsSuccessAndStoresIt() {
        var sessionId = SessionTool.CreateDomainSession("Billing").SessionId!;
        var applied = DslTool.ApplyDsl(sessionId, """
            domain Billing

            Order: entity {
              Total: Number required
              Pay(amount: Number): action { }
            }

            Stripe: contract external stripe v1 {
              Charge: inbound operation Number
            }
            """);
        await Assert.That(applied.Success).IsTrue();
        var before = Revision(sessionId);

        var response = EvolveTool.Add(sessionId, "contract_binding",
            """{"name":"ChargeOrder","contractName":"Stripe","endpointName":"Charge","actionName":"Pay","parameter":"amount"}""");

        await Assert.That(response.Success).IsTrue();
        McpSessionStore.TryGet(sessionId, out var state);
        await Assert.That(state.Revision).IsEqualTo(before + 1);
        await Assert.That(state.Domain.ContractBindings.Select(b => b.Name)).Contains("ChargeOrder");
    }

    [Test]
    public async Task Add_ContractEndpoint_ToAMissingContract_FailsAndLeavesTheRevision() {
        var (sessionId, before) = SessionWithContract();

        var response = EvolveTool.Add(sessionId, "contract_endpoint",
            """{"contractName":"Nope","name":"Charge","payloadType":"Number"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Contract 'Nope' not found");
        await Assert.That(Revision(sessionId)).IsEqualTo(before);
        await Assert.That(Stripe(sessionId).Endpoints).IsEmpty();
    }

    [Test]
    public async Task Add_ContractValueType_ThatClashes_FailsAndLeavesTheRevision() {
        var (sessionId, _) = SessionWithContract();
        EvolveTool.Add(sessionId, "contract_value_type", """{"contractName":"Stripe","name":"ChargeRequest"}""");
        var before = Revision(sessionId);

        var response = EvolveTool.Add(sessionId, "contract_value_type", """{"contractName":"Stripe","name":"ChargeRequest"}""");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("clashes");
        await Assert.That(Revision(sessionId)).IsEqualTo(before);
        await Assert.That(Stripe(sessionId).Types.Count).IsEqualTo(1);
    }
}
