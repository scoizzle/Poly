namespace Poly.Management.Configuration.Abstractions.Requests;

public record GetConfigurationSectionChildrenRequest(string AppId, IEnumerable<string> EarlierKeys, string? ParentPath);
