using Poly.Management.Certificates.Monitoring;

namespace Poly.Management.Certificates;

public interface ICertificateDiscoveryService
{
    public event Action<IEnumerable<CertificateInformation>> OnCertificateScanCompleted;

    public DateTimeOffset LastScanCompletedAt { get; }
    public TimeSpan LastScanDuration { get; }
    public IEnumerable<CertificateInformation> LatestCertificateInformation { get; }
    public ValueTask<IEnumerable<CertificateInformation>> ScanAsync(CertificateMonitoringOptions options, CancellationToken cancellationToken = default);
}
