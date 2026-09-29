using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace AOSharp.Clientless;

public static class DomainProxySinkExtensions
{
    public static LoggerConfiguration DomainProxySink(this LoggerSinkConfiguration loggerConfiguration, IFormatProvider fmtProvider = null)
    {
        return loggerConfiguration.Sink(new DomainProxySink(fmtProvider));
    }
}

public class DomainProxySink : ILogEventSink
{
    private readonly IFormatProvider _formatProvider;

    public DomainProxySink(IFormatProvider formatProvider)
    {
        _formatProvider = formatProvider;
    }

    public void Emit(LogEvent logEvent)
    {
        var message = $"[{Client.CharacterName}] {logEvent.RenderMessage(_formatProvider)}";

        if (logEvent.Level == LogEventLevel.Debug)
        {
            Client.HostProxy.Debug(message);
        }
        else if (logEvent.Level == LogEventLevel.Error)
        {
            Client.HostProxy.Error(message);
        }
        else if (logEvent.Level == LogEventLevel.Warning)
        {
            Client.HostProxy.Warning(message);
        }
        else if (logEvent.Level == LogEventLevel.Information)
        {
            Client.HostProxy.Information(message);
        }
    }
}