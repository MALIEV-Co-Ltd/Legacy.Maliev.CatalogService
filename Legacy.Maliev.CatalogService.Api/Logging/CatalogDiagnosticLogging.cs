using Microsoft.Extensions.Logging.Console;

namespace Legacy.Maliev.CatalogService.Api.Logging;

/// <summary>Registers Catalog's private console diagnostics after the shared service defaults.</summary>
public static class CatalogDiagnosticLogging
{
    /// <summary>Selects the application-owned formatter and preserves the original console warning filter.</summary>
    /// <param name="builder">The application logging builder.</param>
    /// <returns>The same logging builder.</returns>
    public static ILoggingBuilder AddCatalogPrivateDiagnostics(this ILoggingBuilder builder)
    {
        builder.AddConsoleFormatter<CatalogDiagnosticConsoleFormatter, ConsoleFormatterOptions>();
        builder.AddConsole(options => options.FormatterName = CatalogDiagnosticConsoleFormatter.FormatterName);
        builder.AddFilter<ConsoleLoggerProvider>(null, LogLevel.Warning);
        return builder;
    }
}
