using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Api.Logging;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Data;
using Maliev.Aspire.ServiceDefaults.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

[Collection("Read-only catalog process environment")]
public sealed class CatalogConfiguredDiagnosticLoggingTests
{
    [Fact]
    public void ConfiguredCatalogHost_SelectsOwnedConsoleFormatterAndWarningFilterWithoutDatabaseWork()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Production");
        var services = fixture.Host.Services;
        var formatter = SelectedFormatter(services);
        Assert.NotNull(formatter);
        Assert.Contains(services.GetServices<ILoggerProvider>(), provider => provider is ConsoleLoggerProvider);
        fixture.AssertNoDatabaseWork();
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    [InlineData("Invalidate")]
    public async Task ConfiguredCatalogHost_ActualCacheWarningProducer_PreservesExceptionTraceAndHidesKey(string operation)
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Production");
        using var capture = new SelectedFormatterCapture();
        using var factory = fixture.Host.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(capture));
            builder.ConfigureServices(services => services.AddSingleton<IDistributedCache>(new FailedCacheStore()));
        });
        var services = factory.Services;
        capture.Formatter = SelectedFormatter(services);
        using var activity = new Activity("CatalogDiagnosticSource").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var scope = services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICatalogCache>();
        const string key = "private@example.invalid?token=private-token";
        if (operation == "Read") Assert.Null(await cache.GetAsync<string>(key, CancellationToken.None));
        else if (operation == "Write") await cache.SetAsync(key, new[] { "Synthetic fixture value" }, CancellationToken.None);
        else await cache.RemoveAsync(key, CancellationToken.None);
        var line = Assert.Single(capture.Lines, value => value.Contains(typeof(DistributedCatalogCache).FullName!, StringComparison.Ordinal));
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("WARNING", root.GetProperty("severity").GetString());
        Assert.Equal(0, root.GetProperty("eventId").GetInt32());
        Assert.Equal("System.InvalidOperationException", root.GetProperty("exceptionType").GetString());
        Assert.Equal("System.ArgumentException", root.GetProperty("innerExceptionType").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("sourceLocation").GetString()));
        Assert.Equal(activity.TraceId.ToHexString(), root.GetProperty("traceId").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), root.GetProperty("spanId").GetString());
        Assert.False(root.TryGetProperty("CacheKey", out _));
        Assert.DoesNotContain(key, line);
        Assert.DoesNotContain("private-token", line);
        Assert.DoesNotContain("private@example.invalid", line);
        fixture.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task ConfiguredCatalogHost_ActualMiddlewareProducer_PreservesTypeIncidentAndResponseWithoutPrivatePath()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Production");
        using var capture = new SelectedFormatterCapture();
        using var factory = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        var services = factory.Services;
        capture.Formatter = SelectedFormatter(services);
        using var activity = new Activity("CatalogRequestFailureSource").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/customers/private@example.invalid";
        context.TraceIdentifier = "CATALOG_SOURCE:0001";
        using var response = new MemoryStream();
        context.Response.Body = response;
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("private@example.invalid?token=private-token"),
            services.GetRequiredService<ILogger<ExceptionHandlingMiddleware>>(), services.GetRequiredService<IHostEnvironment>());
        await middleware.InvokeAsync(context);
        Assert.Equal(400, context.Response.StatusCode);
        var line = Assert.Single(capture.Lines, value => value.Contains("UnhandledRequestFailure", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("CRITICAL", root.GetProperty("severity").GetString());
        Assert.Equal("InvalidOperationException", root.GetProperty("ExceptionType").GetString());
        Assert.Equal(context.TraceIdentifier, root.GetProperty("IncidentId").GetString());
        Assert.Equal("GET", root.GetProperty("Method").GetString());
        Assert.Equal(400, root.GetProperty("StatusCode").GetInt32());
        Assert.Equal(activity.TraceId.ToHexString(), root.GetProperty("traceId").GetString());
        Assert.False(root.TryGetProperty("Path", out _));
        Assert.DoesNotContain("private@example.invalid", line);
        Assert.DoesNotContain("private-token", line);
        response.Position = 0;
        using var body = await JsonDocument.ParseAsync(response);
        Assert.Equal(context.TraceIdentifier, body.RootElement.GetProperty("traceId").GetString());
        Assert.Equal(400, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("details").ValueKind);
        fixture.AssertNoDatabaseWork();
    }

    private static CatalogDiagnosticConsoleFormatter SelectedFormatter(IServiceProvider services)
    {
        Assert.Equal(CatalogDiagnosticConsoleFormatter.FormatterName,
            services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName);
        var filters = services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;
        Assert.Single(filters.Rules, rule => rule.ProviderName == typeof(ConsoleLoggerProvider).FullName
            && rule.CategoryName is null && rule.LogLevel == LogLevel.Warning);
        return Assert.Single(services.GetServices<ConsoleFormatter>().OfType<CatalogDiagnosticConsoleFormatter>());
    }

    private sealed class SelectedFormatterCapture : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        public CatalogDiagnosticConsoleFormatter? Formatter { get; set; }
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
        public void Dispose() { }

        private sealed class CaptureLogger(SelectedFormatterCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level != LogLevel.None;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (owner.Formatter is not { } selected) return;
                using var writer = new StringWriter(CultureInfo.InvariantCulture);
                var entry = new LogEntry<TState>(level, category, eventId, state, exception, formatter);
                selected.Write(entry, owner.scopes, writer);
                var line = writer.ToString();
                if (line.Length != 0) owner.Lines.Enqueue(line);
            }
        }
    }

    // Controlled dependency failure drives the unchanged application cache producer; no Redis/provider parity is claimed.
    private sealed class FailedCacheStore : IDistributedCache
    {
        private static Exception Failure() => new InvalidOperationException("private@example.invalid?token=private-token", new ArgumentException("private-token"));
        public byte[]? Get(string key) => throw Failure();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromException<byte[]?>(Failure());
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Failure();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.FromException(Failure());
        public void Remove(string key) => throw Failure();
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(Failure());
        public void Refresh(string key) => throw Failure();
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(Failure());
    }
}
