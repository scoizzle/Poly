using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Poly.Management.Certificates.Monitoring;

public class CertificateMonitoringHealthCheck(
    TimeProvider timeProvider,
    ICertificateDiscoveryService discoveryService,
    IOptionsMonitor<CertificateMonitoringOptions> optionsMonitor) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        CertificateMonitoringOptions options = optionsMonitor.CurrentValue;

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset timeToRescanCertificates = options.TimeToConsiderStale(now);
        DateTimeOffset timeToDegradeCertificates = options.TimeToConsiderDegraded(now);

        if (discoveryService.LastScanCompletedAt < timeToRescanCertificates)
            await discoveryService.ScanAsync(options, cancellationToken);

        List<CertificateInformation> invalidCertificates = discoveryService
            .LatestCertificateInformation
            .Where(predicate: static e => !e.Certificate.Verify())
            .ToList();

        if (invalidCertificates.Count > 0)
        {
            IReadOnlyDictionary<string, object> invalidCertificateInformation = invalidCertificates
                .ToDictionary(
                    keySelector: static e => $"{e.StoreLocation}/{e.StoreName}/{e.Certificate.SubjectName.Name}",
                    elementSelector: static e => e.Certificate as object
                );

            return HealthCheckResult.Unhealthy(description: $"At least one certificate is invalid.", data: invalidCertificateInformation);
        }

        List<CertificateInformation> certificatesWithinDegradationThreshold = discoveryService
            .LatestCertificateInformation
            .Where(predicate: e => e.Certificate.NotAfter < timeToDegradeCertificates)
            .ToList();

        if (certificatesWithinDegradationThreshold.Count > 0)
        {
            IReadOnlyDictionary<string, object> degradedCertificateInformation = certificatesWithinDegradationThreshold
                .ToDictionary(
                    keySelector: static e => $"{e.StoreLocation}/{e.StoreName}/{e.Certificate.FriendlyName}",
                    elementSelector: static e => e.Certificate as object
                );

            return HealthCheckResult.Degraded(description: $"At least one certificate is about to expire.", data: degradedCertificateInformation);
        }

        return HealthCheckResult.Healthy(description: $"All certificates are valid as of {discoveryService.LastScanCompletedAt}");
    }
}
