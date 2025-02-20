using Microsoft.Extensions.Logging;
using Poly.Management.Configuration.Abstractions.Requests;

namespace Poly.Management.Configuration;

static partial class DistributedConfigurationClientLoggingExtensions
{

    [LoggerMessage(EventId = -1, Level = LogLevel.Error, Message = "Failed to get configuration section {request}")]
    public static partial void FailedToGetConfigurationSection(this ILogger logger, Exception ex, GetConfigurationSectionRequest request);

    [LoggerMessage(EventId = -2, Level = LogLevel.Error, Message = "Failed to get configuration section children. {request}")]
    public static partial void FailedToGetConfigurationSectionChildren(this ILogger logger, Exception ex, GetConfigurationSectionChildrenRequest request);

    [LoggerMessage(EventId = -3, Level = LogLevel.Error, Message = "Failed to set configuration section. {request}")]
    public static partial void FailedToSetConfigurationSection(this ILogger logger, Exception ex, SetConfigurationSectionRequest request);
}
