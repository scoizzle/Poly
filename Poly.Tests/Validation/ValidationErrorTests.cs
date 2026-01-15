using Poly.Validation;

namespace Poly.Tests.Validation;

public class ValidationErrorTests {
    [Test]
    public async Task Constructor_InitializesProperties()
    {
        var error = new ValidationError("Name", "required", "Name is required");

        await Assert.That(error.Path).IsEqualTo("Name");
        await Assert.That(error.Code).IsEqualTo("required");
        await Assert.That(error.Message).IsEqualTo("Name is required");
    }

    [Test]
    public async Task ToString_FormatsAsPathMessage()
    {
        var error = new ValidationError("Email", "invalid", "Email format is invalid");

        var result = error.ToString();

        await Assert.That(result).Contains("Email");
        await Assert.That(result).Contains("Email format is invalid");
        await Assert.That(result).Contains("invalid");
    }

    [Test]
    public async Task ToString_WithEmptyPath_FormatsCorrectly()
    {
        var error = new ValidationError("", "error", "An error occurred");

        var result = error.ToString();

        await Assert.That(result).Contains("error");
        await Assert.That(result).Contains("An error occurred");
    }

    [Test]
    public async Task Equality_SameValues_AreEqual()
    {
        var error1 = new ValidationError("Name", "required", "Name is required");
        var error2 = new ValidationError("Name", "required", "Name is required");

        await Assert.That(error1).IsEqualTo(error2);
    }

    [Test]
    public async Task Equality_DifferentPath_AreNotEqual()
    {
        var error1 = new ValidationError("Name", "required", "Name is required");
        var error2 = new ValidationError("Email", "required", "Name is required");

        await Assert.That(error1).IsNotEqualTo(error2);
    }

    [Test]
    public async Task Equality_DifferentCode_AreNotEqual()
    {
        var error1 = new ValidationError("Name", "required", "Name is required");
        var error2 = new ValidationError("Name", "invalid", "Name is required");

        await Assert.That(error1).IsNotEqualTo(error2);
    }

    [Test]
    public async Task Equality_DifferentMessage_AreNotEqual()
    {
        var error1 = new ValidationError("Name", "required", "Name is required");
        var error2 = new ValidationError("Name", "required", "Different message");

        await Assert.That(error1).IsNotEqualTo(error2);
    }

    [Test]
    public async Task IsRecord_SupportsDeconstruction()
    {
        var error = new ValidationError("Name", "required", "Name is required");

        var (path, code, message) = error;

        await Assert.That(path).IsEqualTo("Name");
        await Assert.That(code).IsEqualTo("required");
        await Assert.That(message).IsEqualTo("Name is required");
    }
}