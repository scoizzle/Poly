using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Poly.Management.Certificates.Monitoring;

class CertificateMonitoringBackgroundWorker(
    ICertificateDiscoveryService certificateDiscovery,
    IOptionsMonitor<CertificateMonitoringOptions> optionsMonitor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            CertificateMonitoringOptions options = optionsMonitor.CurrentValue;

            await certificateDiscovery.ScanAsync(options, cancellationToken);

            CancellationTokenSource cts = new();

            using var optionsChangedRegistration = optionsMonitor.OnChange(listener: _ => cts.Cancel());
            using var stoppingTokenRegistration = cancellationToken.Register(callback: cts.Cancel);

            TimeSpan delay = options.ScanningFrequencyMinusLastScanDuration(scanDuration: certificateDiscovery.LastScanDuration);

            try
            {
                await Task.Delay(delay, cancellationToken: cts.Token);
            }
            catch (TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }
        }
    }
}
