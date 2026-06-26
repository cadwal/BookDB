using System;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace BookDB.Data;

public sealed class SerilogDbUpLog : IUpgradeLog
{
    private readonly ILogger<DatabaseStartupService> _logger;

    public SerilogDbUpLog(ILogger<DatabaseStartupService> logger)
    {
        _logger = logger;
    }

    public void LogTrace(string format, params object[] args)
    {
        _logger.LogTrace(format, args);
    }

    public void LogDebug(string format, params object[] args)
    {
        _logger.LogDebug(format, args);
    }

    public void LogInformation(string format, params object[] args)
    {
        _logger.LogInformation(format, args);
    }

    public void LogWarning(string format, params object[] args)
    {
        _logger.LogWarning(format, args);
    }

    public void LogError(Exception ex, string format, params object[] args)
    {
        _logger.LogError(ex, format, args);
    }

    public void LogError(string format, params object[] args)
    {
        _logger.LogError(format, args);
    }
}
