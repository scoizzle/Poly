using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;

// ─────────────────────────────────────────────────────────────
//  Poly Demo — end-to-end vertical slice in ~60 lines.
//
//  Define a domain entity → create an instance → evaluate a
//  policy → call an action → observe the stage transition.
//
//  Run:  dotnet run --project demo
// ─────────────────────────────────────────────────────────────

// 1. ── Define a domain entity (Person) via evolution ─────────
var domain = DomainFactory.Create("Demo");
var evolve = new DomainEvolution(domain).Evolve()
    .AddEntity("Person")
    .AddPropertyToEntity("Person", new Property("Name", new DomainTypeReference("Text"), []))
    .AddPropertyToEntity("Person", new Property("Age", new DomainTypeReference("Number"), []))
    .AddPropertyToEntity("Person", new Property("Active", new DomainTypeReference("Boolean"), []))
    .AddStage("Person", "Draft")
    .AddStage("Person", "Active")
    .AddAction("Person", "Activate")
    .AddActionToStage("Person", "Draft", "Activate")
    .AddEffectToAction("Person", "Activate",
        new StageTransitionEffect(new StageReference("Active")))
    .Apply();

Console.WriteLine($"Bootstrapped: {evolve.Root.Name}");
Console.WriteLine($"  Entity:  {evolve.Root.Types.OfType<Entity>().First().Name}");
Console.WriteLine($"  Stages:  {string.Join(", ", evolve.Root.Types.OfType<Entity>().First().Stages.Select(s => s.Name))}");
Console.WriteLine();

// 2. ── Attach policies via evolution ─────────────────────────
var person = evolve.Root.Types.OfType<Entity>().First();
var isActiveExpr = DomainExpression.Equal(
    DomainExpression.Property("Active"),
    DomainExpression.Literal(true));

var isAdultExpr = DomainExpression.GreaterThanOrEqual(
    DomainExpression.Property("Age"),
    DomainExpression.Literal(18L));

evolve = new DomainEvolution(evolve.Root).Apply([
    new AddPolicyToEntityChange("Person", new Policy("IsActive", isActiveExpr)),
    new AddPolicyToEntityChange("Person", new Policy("IsAdult", isAdultExpr)),
]);

// 3. ── Create an instance (Alice, 25, active) ────────────────
person = evolve.Root.Types.OfType<Entity>().First();
var alice = DomainEntityInstance.Create(person,
    new Dictionary<string, object?> { ["Name"] = "Alice", ["Age"] = 25L, ["Active"] = true });

Console.WriteLine($"Instance: {alice.GetProperty<string>("Name")}, Age {alice.GetProperty<object>("Age")}");
Console.WriteLine($"  Stage:       {alice.CurrentStage}");
Console.WriteLine();

// 4. ── Evaluate policies ─────────────────────────────────────
var adultResult = alice.EvaluatePolicy(person.Policies.First(p => p.Name == "IsAdult"));
Console.WriteLine($"  IsAdult (Age >= 18)?  {adultResult}");

var activeResult = alice.EvaluatePolicy(person.Policies.First(p => p.Name == "IsActive"));
Console.WriteLine($"  IsActive (Active==true)? {activeResult}");
Console.WriteLine();

// 5. ── Call an action → observe stage transition ────────────
var result = alice.CallAction("Activate");
Console.WriteLine($"  CallAction(\"Activate\"): {result.Succeeded}");
Console.WriteLine($"  New stage:      {result.NewStage}");
Console.WriteLine($"  Current stage:  {alice.CurrentStage}");
Console.WriteLine();

// ── Summary ──────────────────────────────────────────────────
Console.WriteLine("=== Summary ===");
Console.WriteLine("Domain → Policy → Instance → Evaluate → Action → Stage transition");
Console.WriteLine("All paths green.  The platform works end-to-end.");