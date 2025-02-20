namespace Poly.Management.Configuration.Abstractions.Requests;

public record SetConfigurationSectionRequest(string appId, string key, string? value);
