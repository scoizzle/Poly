using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Meaning;

namespace Poly.Tests.TestHelpers;

/// <summary>Session Meaning/forms as TemporalLibrary.Register fills them — for tests that lower IR without a DomainSession.</summary>
public static class TemporalMeaningHarness {
    public static ExpressionMeaning Create() {
        var meaning = new ExpressionMeaning();
        TemporalMeaning.Register(meaning);
        return meaning;
    }

    public static ExpressionFormRegistry Forms() {
        var forms = new ExpressionFormRegistry();
        TemporalExpressionPrintBinders.RegisterFolds(forms);
        return forms;
    }
}