using System.Runtime.CompilerServices;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Dispatch;

/// <summary>
/// <see cref="EffectDispatch{TResult}.Route"/> ends in a runtime <c>_ =&gt; throw</c>, so a new
/// <see cref="Effect"/> subtype that is not added to the switch fails only when something routes it.
/// This routes one instance of every concrete subtype through a probe and asserts none hits the throw.
/// </summary>
public sealed class EffectDispatchTotalityTests {
    private sealed class Probe : EffectDispatch<string> {
        protected override string Default() => "default";
    }

    [Test]
    public async Task EveryConcreteEffect_HasARouteArm() {
        var unrouted = new List<string>();
        var concrete = typeof(Effect).Assembly.GetTypes()
            .Where(t => typeof(Effect).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(Effect))
            .ToList();

        foreach (var type in concrete) {
            var instance = (Effect)RuntimeHelpers.GetUninitializedObject(type);
            try {
                new Probe().Route(instance);
            }
            catch (NotSupportedException) {
                unrouted.Add(type.Name);
            }
        }

        await Assert.That(concrete.Count).IsGreaterThan(8);
        await Assert.That(unrouted).IsEmpty();
    }
}
