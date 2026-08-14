using Poly.DomainModeling.Parsing;

namespace Poly.DomainModeling.Packs.Temporal;

/// <summary>
/// Language library for clock dates. Product <see cref="DomainHostBuilder.Create"/>
/// loads this; <see cref="EnsureLanguage"/> is the only place meaning (dispatch,
/// defaults, type checks) is registered — never a module initializer.
/// </summary>
public sealed class TemporalLibrary : IDomainLibrary {
    public string Id => "temporal";

    /// <summary>
    /// Registers temporal meaning into the process language tables. Idempotent.
    /// Parse/print still require <see cref="Register"/> on a host.
    /// </summary>
    public static void EnsureLanguage() => TemporalDispatchRegistration.EnsureRegistered();

    public void Register(HostSurfaces surfaces) {
        ArgumentNullException.ThrowIfNull(surfaces);
        EnsureLanguage();
        surfaces.ExpressionForms.Register(new NowForm());
        surfaces.ExpressionForms.Register(new DurationForm());
        surfaces.ExpressionForms.RegisterBinaryFold(new DateOperationFold());
        TemporalExpressionPrintBinders.Register(surfaces.ExpressionForms);
        surfaces.ExpressionForms.RegisterGrammarContributor(TemporalExpressionPrintBinders.ContributeGrammarPatterns);
    }
}