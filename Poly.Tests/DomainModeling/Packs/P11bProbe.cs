using System.Text;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Packs;

// SCAFFOLDING (P-11b step 0): deleted in the last commit.
public class P11bProbe {
    private static readonly string Out = Environment.GetEnvironmentVariable("P11B_OUT") ?? "/tmp/p11b.txt";

    [Test]
    [Timeout(900_000)]
    public async Task UniversityFlow(CancellationToken cancellationToken) {
        _ = cancellationToken;
        var poly = File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, "docs/probes/dogfood/university.poly"))
            .Replace("uses persistence", "uses sqlite\nuses http");
        var log = new StringBuilder();
        await using var oracle = await PackOracle.BootAsync(new PackOracleDomain("UniversityP11b", poly, "/api/courses"));
        async Task Send(string method, string path, string? body = null) {
            using var response = method == "GET"
                ? await oracle.Client.GetAsync(path)
                : await oracle.Client.PostAsync(path, new StringContent(body ?? "{}", Encoding.UTF8, "application/json"));
            log.AppendLine($"{method} {path} {body} -> {(int)response.StatusCode} loc={response.Headers.Location} {await response.Content.ReadAsStringAsync()}");
        }
        await Send("POST", "/api/courses", """{"Code":"CS101","Title":"Intro"}""");
        await Send("POST", "/api/sections", """{"Cap":10,"OfferingTerm":"Fall"}""");
        await Send("POST", "/api/sections", """{"Cap":10,"OfferingTerm":"Fall","courseId":"CS101"}""");
        await Send("POST", "/api/students", """{"Name":"Ann","Email":"ann@x.io"}""");
        await Send("GET", "/api/sections");
        await Send("GET", "/api/students");
        await Send("POST", "/api/students/ann%40x.io/enroll", """{"OfferingId":"1"}""");
        await Send("POST", "/api/students/ann%40x.io/enroll", """{"OfferingId":"999"}""");
        await Send("POST", "/api/students/ann%40x.io/enroll", """{"OfferingId":"2"}""");
        await Send("POST", "/api/students/ann%40x.io/enroll", """{"OfferingId":"x"}""");
        await Send("POST", "/api/students/ann%40x.io/enroll", """{}""");
        await Send("POST", "/api/students/ann%40x.io/suspend");
        await Send("POST", "/api/students/ann%40x.io/reinstate");
        await Send("GET", "/api/students/ann%40x.io/enrollments");
        await Send("GET", "/api/sections/1/enrollments");
        await Send("GET", "/api/sections/1");
        log.AppendLine("--- stderr tail ---");
        log.AppendLine(oracle.ErrorTail);
        var stdout = typeof(PackOracle).GetField("_stdout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(oracle)!;
        log.AppendLine("--- stdout tail ---");
        log.AppendLine((string)stdout.GetType().GetMethod("Tail")!.Invoke(stdout, null)!);
        File.WriteAllText(Out, log.ToString());
    }
}
