using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Poly.Management.Certificates.Monitoring;

namespace Poly.Management.Certificates;

public class CertificateDiscoveryService(
    ILoggerFactory loggerFactory,
    IMeterFactory meterFactory,
    TimeProvider timeProvider) : ICertificateDiscoveryService
{
    private readonly SemaphoreSlim syncSemaphore = new SemaphoreSlim(initialCount: 1);
    private readonly ILogger logger = loggerFactory.CreateLogger(categoryName: typeof(CertificateDiscoveryService).Namespace);
    private List<(StoreLocation Location, StoreName Name)>? X509StoresThatExist;

    public event Action<IEnumerable<CertificateInformation>>? OnCertificateScanCompleted;

    public DateTimeOffset LastScanCompletedAt { get; private set; }
    public TimeSpan LastScanDuration { get; private set; }
    public IEnumerable<CertificateInformation> LatestCertificateInformation { get; private set; } = Enumerable.Empty<CertificateInformation>();


    public async ValueTask<IEnumerable<CertificateInformation>> ScanAsync(CertificateMonitoringOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            await syncSemaphore.WaitAsync(cancellationToken);

            Stopwatch stopwatch = Stopwatch.StartNew();
            LatestCertificateInformation = EnumerateCertificates(options, cancellationToken).ToList();
            stopwatch.Stop();

            LastScanDuration = stopwatch.Elapsed;
            LastScanCompletedAt = timeProvider.GetUtcNow();

            ReportScanningMetrics(options, LastScanDuration);

            if (OnCertificateScanCompleted is not null)
                OnCertificateScanCompleted(LatestCertificateInformation);

            return LatestCertificateInformation;
        }
        catch (Exception error)
        {
            logger.FailedToScanForCertificates(error);
            throw;
        }
        finally
        {
            syncSemaphore.Release();
        }
    }

    private IEnumerable<(StoreLocation Location, StoreName Name)> EnumerateTheX509StoresThatExist()
    {
        foreach (StoreLocation storeLocation in GetEnumValues<StoreLocation>())
        {
            foreach (StoreName storeName in GetEnumValues<StoreName>())
            {
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

                yield return (storeLocation, storeName);
            }
        }
    }

    private IEnumerable<CertificateInformation> EnumerateCertificates(CertificateMonitoringOptions options, CancellationToken cancellationToken)
    {
        X509StoresThatExist ??= EnumerateTheX509StoresThatExist().ToList();

        foreach (var (storeLocation, storeName) in X509StoresThatExist)
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

                var matchesAnyFilters = options.CertificateMatchesAnyFilters(certificate);
                if (matchesAnyFilters)
                {
                    logger.DiscoveredCertificate(storeLocation, storeName, certName: certificate.Subject);
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

    private void ReportScanningMetrics(CertificateMonitoringOptions options, TimeSpan scanDuration)
    {
        Meter meter = meterFactory.Create(name: options.BackgroundMonitoringMeterName);
        Histogram<double> durationHistogram = meter.CreateHistogram<double>(name: options.BackgroundMonitoringScanHistogramName);
        Counter<long> scanCounter = meter.CreateCounter<long>(name: options.BackgroundMonitoringScanCounterName);

        durationHistogram.Record(value: scanDuration.TotalMilliseconds);
        scanCounter.Add(delta: 1);
    }
}
