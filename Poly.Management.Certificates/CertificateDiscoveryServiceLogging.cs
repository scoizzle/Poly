using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Poly.Management.Certificates;

static partial class CertificateDiscoveryServiceLogging
{
    [LoggerMessage(EventId = -2, Message = "Unable to open {storeLocation}/{storeName}.", Level = LogLevel.Error)]
    public static partial void UnableToOpenStore(this ILogger logger, Exception error, StoreLocation storeLocation, StoreName storeName);

    [LoggerMessage(EventId = -1, Message = "Failed to scan for certificates.", Level = LogLevel.Error)]
    public static partial void FailedToScanForCertificates(this ILogger logger, Exception error);

    [LoggerMessage(EventId = 1, Message = "Opened X509 store {storeLocation}/{storeName}.", Level = LogLevel.Trace)]
    public static partial void OpenedStore(this ILogger logger, StoreLocation storeLocation, StoreName storeName);

    [LoggerMessage(EventId = 2, Message = "Discovered X509 Certificate {storeLocation}/{storeName}/{certName}.", Level = LogLevel.Information)]
    public static partial void DiscoveredCertificate(this ILogger logger, StoreLocation storeLocation, StoreName storeName, string certName);
}