using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Poly.Management.Certificates.Monitoring;

namespace Poly.Management.Certificates;

public class CertificateDiscoveryService(
    ILoggerFactory loggerFactory,
    IOptionsMonitor<CertificateMonitoringOptions> optionsMonitor) : ICertificateDiscoveryService
{
    private readonly SemaphoreSlim m_syncSemaphore = new SemaphoreSlim(initialCount: 1);
    private readonly ILogger logger = loggerFactory.CreateLogger(categoryName: typeof(CertificateDiscoveryService).Namespace);

    public DateTime LastScanCompletedAt { get; private set; }
    public IEnumerable<CertificateInformation> LatestCertificateInformation { get; private set; } = Enumerable.Empty<CertificateInformation>();

    public async ValueTask<IEnumerable<CertificateInformation>> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await m_syncSemaphore.WaitAsync(cancellationToken);
            LatestCertificateInformation = EnumerateCertificates(options: optionsMonitor.CurrentValue, cancellationToken).ToList();
            LastScanCompletedAt = DateTime.UtcNow;
            return LatestCertificateInformation;
        }
        catch (Exception error)
        {
            logger.FailedToScanForCertificates(error);
            throw;
        }
        finally
        {
            m_syncSemaphore.Release();
        }
    }

    private IEnumerable<CertificateInformation> EnumerateCertificates(CertificateMonitoringOptions options, CancellationToken cancellationToken)
    {
        foreach (StoreLocation storeLocation in GetEnumValues<StoreLocation>())
        {
            foreach (StoreName storeName in GetEnumValues<StoreName>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using X509Store store = new X509Store(storeName, storeLocation);

                try
                {
                    store.Open(OpenFlags.OpenExistingOnly);
                    logger.OpenedStore(storeLocation, storeName);
                }
                catch (CryptographicException error)
                {
                    logger.UnableToOpenStore(error, storeLocation, storeName);
                    continue;
                }

                foreach (X509Certificate2 certificate in store.Certificates)
                {
                    logger.DiscoveredCertificate(storeLocation, storeName, certName: certificate.FriendlyName);

                    if (!options.CertificateMatchesAnyFilters(certificate))
                        continue;

                    yield return new CertificateInformation(StoreLocation: storeLocation, StoreName: storeName, Certificate: certificate);
                }
            }
        }
    }

    private static IEnumerable<T> GetEnumValues<T>()
        where T : Enum
    {
        return (T[])Enum.GetValues(typeof(T));
    }

}
