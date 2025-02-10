namespace Poly.Management.Certificates;

public interface ICertificateDiscoveryService
{
    public DateTime LastScanCompletedAt { get; }
    public IEnumerable<CertificateInformation> LatestCertificateInformation { get; }
    public ValueTask<IEnumerable<CertificateInformation>> ScanAsync(CancellationToken cancellationToken = default);
}
