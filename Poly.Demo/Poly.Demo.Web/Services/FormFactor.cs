using Poly.Demo.Shared.Services;

namespace Poly.Demo.Web.Services;

public class FormFactor : IFormFactor {
    public string GetFormFactor() {
        return "Web";
    }

    public string GetPlatform() {
        return Environment.OSVersion.ToString();
    }
}