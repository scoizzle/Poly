using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace Poly.Management.Certificates;

public record CertificateInformation(
    StoreLocation StoreLocation,
    StoreName StoreName,
    [property: JsonIgnore] X509Certificate2 Certificate
)
{
    public string Subject { get; init; } = Certificate.Subject;
    public string Issuer { get; init; } = Certificate.Issuer;
    public DateTime NotAfter { get; init; } = Certificate.NotAfter;
    public DateTime NotBefore { get; init; } = Certificate.NotBefore;
    public string SerialNumber { get; init; } = Certificate.SerialNumber;
    public string Thumbprint { get; init; } = Certificate.Thumbprint;
}