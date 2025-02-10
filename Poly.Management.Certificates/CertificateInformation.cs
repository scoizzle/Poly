using System.Security.Cryptography.X509Certificates;

namespace Poly.Management.Certificates;

public record CertificateInformation(
    StoreLocation StoreLocation,
    StoreName StoreName,
    X509Certificate2 Certificate
);