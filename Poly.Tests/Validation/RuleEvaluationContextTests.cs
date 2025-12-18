using Poly.Validation;
using Poly.Validation.Rules;

namespace Poly.Tests.Validation;

public class RuleEvaluationContextTests {
    [Test]
    public async Task Constructor_InitializesEmptyErrors() {
        var context = new RuleEvaluationContext();

        await Assert.That(context.Errors.Count()).IsEqualTo(0);
    }

    [Test]
    public async Task AddError_AddsErrorToCollection() {
        var context = new RuleEvaluationContext();
        var error = new ValidationError("Name", "required", "Name is required");

        context.AddError(error);

        await Assert.That(context.Errors.Count()).IsEqualTo(1);
        await Assert.That(context.Errors.First()).IsEqualTo(error);
    }

    [Test]
    public async Task AddError_WithNullError_Throws() {
        var context = new RuleEvaluationContext();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => {
            context.AddError(null!);
        });
    }

    [Test]
    public async Task AddError_MultipleErrors_AllStored() {
        var context = new RuleEvaluationContext();
        var error1 = new ValidationError("Name", "required", "Name is required");
        var error2 = new ValidationError("Email", "invalid", "Email is invalid");
        var error3 = new ValidationError("Age", "range", "Age must be >= 0");

        context.AddError(error1);
        context.AddError(error2);
        context.AddError(error3);

        await Assert.That(context.Errors.Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Evaluate_WithFalseConditionAndFactory_AddsCustomError() {
        var context = new RuleEvaluationContext();
        var customError = new ValidationError("Field", "code", "Custom message");

        var result = context.Evaluate(false, () => customError);

        await Assert.That(result).IsEqualTo(context);
        await Assert.That(context.Errors.Count()).IsEqualTo(1);
        await Assert.That(context.Errors.First()).IsEqualTo(customError);
    }

    [Test]
    public async Task Evaluate_WithTrueConditionAndFactory_DoesNotAddError() {
        var context = new RuleEvaluationContext();
        var customError = new ValidationError("Field", "code", "Custom message");

        var result = context.Evaluate(true, () => customError);

        await Assert.That(result).IsEqualTo(context);
        await Assert.That(context.Errors.Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Evaluate_WithNullFactory_Throws() {
        var context = new RuleEvaluationContext();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => {
            context.Evaluate(false, null!);
        });
    }

    [Test]
    public async Task Evaluate_WithFactory_ReturnsContextForChaining() {
        var context = new RuleEvaluationContext();

        var result = context.Evaluate(true, () => new ValidationError("", "", ""));

        await Assert.That(result).IsEqualTo(context);
    }

    [Test]
    public async Task GetResult_WithNoErrors_IsValid() {
        var context = new RuleEvaluationContext();

        var result = context.GetResult();

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Errors.Count()).IsEqualTo(0);
    }

    [Test]
    public async Task GetResult_WithErrors_IsInvalid() {
        var context = new RuleEvaluationContext();
        context.AddError(new ValidationError("Name", "required", "Name is required"));

        var result = context.GetResult();

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task GetResult_ReturnsCorrectErrors() {
        var context = new RuleEvaluationContext();
        var error1 = new ValidationError("Name", "required", "Name is required");
        var error2 = new ValidationError("Email", "invalid", "Email is invalid");
        context.AddError(error1);
        context.AddError(error2);

        var result = context.GetResult();

        var errors = result.Errors.ToList();
        await Assert.That(errors[0]).IsEqualTo(error1);
        await Assert.That(errors[1]).IsEqualTo(error2);
    }

    [Test]
    public async Task Evaluate_MultipleConditions_ChainedResults() {
        var context = new RuleEvaluationContext();

        context.Evaluate(true, () => new ValidationError("", "", ""));
        context.Evaluate(false, () => new ValidationError("Field1", "code1", "message1"));
        context.Evaluate(true, () => new ValidationError("", "", ""));
        context.Evaluate(false, () => new ValidationError("Field2", "code2", "message2"));

        var result = context.GetResult();

        await Assert.That(context.Errors.Count()).IsEqualTo(2);
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ValidationError_ToString_FormatsCorrectly() {
        var error = new ValidationError("Email", "invalid", "Invalid email format");

        var stringRep = error.ToString();

        await Assert.That(stringRep).Contains("Email");
        await Assert.That(stringRep).Contains("Invalid email format");
        await Assert.That(stringRep).Contains("invalid");
    }
}