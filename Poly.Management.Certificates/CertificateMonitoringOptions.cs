using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Poly.Management.Certificates.Monitoring;

public record CertificateMonitoringCertificateFilters(
    string Pattern
)
{
    [JsonIgnore]
    public Regex Regex { get; init; } = new Regex(pattern: Pattern, RegexOptions.None);
}

public record CertificateMonitoringOptions
(
    TimeSpan? ScanningFrequency,
    TimeSpan? DegradedHealthThreshold,
    IEnumerable<CertificateMonitoringCertificateFilters>? Filters,
    string BackgroundMonitoringMeterName = "poly.management.certificates.monitoring",
    string BackgroundMonitoringScanHistogramName = "poly.management.certificates.monitoring.scan_duration",
    string BackgroundMonitoringScanCounterName = "poly.management.certificates.monitoring.scan_count",
    string BackgroundMonitoringCertificateCounterName = "poly.management.certificates.monitoring.certificate_count"
)
{
    public CertificateMonitoringOptions() : this(ScanningFrequency: default, DegradedHealthThreshold: default, Filters: default) { }

    public const string ConfigurationPath = "Poly:Management:Certificates:Monitoring";

    public TimeSpan ScanningFrequencyMinusLastScanDuration(TimeSpan scanDuration)
    {
        var frequency = ScanningFrequency ?? TimeSpan.FromHours(value: 1);
        if (scanDuration > frequency)
            return frequency;
        return frequency - scanDuration;
    }

    public DateTimeOffset TimeToConsiderStaleFrom(DateTimeOffset now) =>
        now - (ScanningFrequency ?? TimeSpan.FromHours(value: 1));

    public DateTimeOffset TimeToConsiderDegradedFrom(DateTimeOffset now) =>
        now + (DegradedHealthThreshold ?? TimeSpan.FromDays(value: 7));

    public bool CertificateMatchesAnyFilters(X509Certificate2 certificate)
    {
        if (Filters is null) return true;

        IEnumerable<string> CertificatePropertyStrings = [
            $"issuer={certificate.Issuer}",
            $"subject={certificate.Subject}",
            $"friendly_name={certificate.FriendlyName}",
            $"serial_number={certificate.SerialNumber}",
            $"thumbprint={certificate.Thumbprint}"
        ];

        IEnumerable<bool> query =
            from filter in Filters
            from propertyString in CertificatePropertyStrings
            select filter.Regex.IsMatch(propertyString);

        return query.Any(static _ => _);
    }
}
