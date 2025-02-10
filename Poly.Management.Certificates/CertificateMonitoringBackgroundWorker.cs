using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Poly.Management.Certificates.Monitoring;

class CertificateMonitoringBackgroundWorker(
    IMeterFactory meterFactory,
    ICertificateDiscoveryService certificateDiscovery,
    IOptionsMonitor<CertificateMonitoringOptions> optionsMonitor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CertificateMonitoringOptions options = optionsMonitor.CurrentValue;

            TimeSpan scanDuration = await ScanAsync(cancellationToken: stoppingToken);

            ReportMetrics(options, scanDuration);

            await Task.Delay(delay: options.ScanningFrequencyMinusLastScanDuration(scanDuration));
        }
    }

    private async ValueTask<TimeSpan> ScanAsync(CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        await certificateDiscovery.ScanAsync(cancellationToken);

        stopwatch.Stop();

        return stopwatch.Elapsed;
    }

    private void ReportMetrics(CertificateMonitoringOptions options, TimeSpan scanDuration)
    {
        Meter meter = meterFactory.Create(name: options.BackgroundMonitoringMeterName);
        Histogram<double> durationHistogram = meter.CreateHistogram<double>(name: options.BackgroundMonitoringScanHistogramName);
        Counter<long> scanCounter = meter.CreateCounter<long>(name: options.BackgroundMonitoringScanCounterName);

        durationHistogram.Record(value: scanDuration.TotalMilliseconds);
        scanCounter.Add(delta: 1);
    }
}
