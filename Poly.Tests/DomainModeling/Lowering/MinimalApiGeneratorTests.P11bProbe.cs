using System.Text;

using Poly.DomainModeling.Ontology;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Lowering;

// SCAFFOLDING (P-11b step 0): deleted in the last commit.
public partial class MinimalApiGeneratorTests {
    [Test]
    public void P11bProbe_Dump() {
        var sb = new StringBuilder();
        void Dump(string label, string poly) {
            sb.AppendLine($"===== {label}");
            try { sb.AppendLine(HostGenerator(CompileForHost(poly)).Generate("Ctx")); }
            catch (Exception ex) { sb.AppendLine("FAILED: " + ex.Message); }
        }
        Dump("section", """
            domain Uni
            Course: entity { Code: Text unique  Title: Text }
            Section: entity { Cap: Number  Code: Text unique  course: Course }
            """);
        Dump("collection", """
            domain Lib
            Patron: entity {
              Email: Text unique
              loans: many Loan via borrower
            }
            Loan: entity {
              Code: Text unique
              borrower: Patron
            }
            """);
        File.WriteAllText(Environment.GetEnvironmentVariable("P11B_OUT") ?? "/tmp/p11b_dump.txt", sb.ToString());
    }
}
