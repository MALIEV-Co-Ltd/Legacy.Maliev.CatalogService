using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Tests.Integration;

namespace Legacy.Maliev.CatalogService.Tests.Startup;

[Collection("Read-only catalog process environment")]
public sealed class CatalogStartupFailureAcceptanceTests
{
    private const string Sentinel = "private-catalog-startup-sentinel";

    [Theory]
    [InlineData("malformed")]
    [InlineData("missing-root")]
    [InlineData("host-start")]
    public async Task ActualEntryPoint_ContainsFailurePrivatelyAndExitsOne(string phase)
    {
        var root = Path.Combine(Path.GetTempPath(), Sentinel + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), phase == "host-start"
                ? JsonSerializer.Serialize(Settings()) : "{ invalid-" + Sentinel);
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Legacy.Maliev.CatalogService.slnx")))
                repository = repository.Parent;
            Assert.NotNull(repository);
            var api = Path.Combine(repository.FullName, "Legacy.Maliev.CatalogService.Api", "bin", "Release", "net10.0",
                "Legacy.Maliev.CatalogService.Api.dll");
            Assert.True(File.Exists(api) && File.Exists(Path.ChangeExtension(api, ".runtimeconfig.json")),
                "Build the actual API Release artifacts before subprocess acceptance.");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(api)!
            };
            var benign = new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "DOTNET_ROOT", "HOME", "USERPROFILE" }
                .Select(key => (Key: key, Value: Environment.GetEnvironmentVariable(key))).ToArray();
            start.Environment.Clear();
            foreach (var (key, value) in benign)
                if (value is not null) start.Environment[key] = value;
            foreach (var argument in new[] { api, "--environment=Production", "--contentRoot=" +
                (phase == "missing-root" ? Path.Combine(root, Sentinel) : root) }) start.ArgumentList.Add(argument);
            if (phase == "host-start") start.ArgumentList.Add("--urls=http://127.0.0.1:0/" + Sentinel);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Owned startup child did not start.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var stdout = child.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = child.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await child.WaitForExitAsync(deadline.Token);
                Assert.Equal(1, child.ExitCode);
                var captured = await DrainAsync(stdout, stderr, deadline.Token);
                var raw = captured[0] + captured[1];
                Assert.DoesNotContain(Sentinel, raw, StringComparison.Ordinal);
                Assert.DoesNotContain("Unhandled exception", raw, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
                var entries = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var entry in entries)
                {
                    using var parsed = JsonDocument.Parse(entry);
                    Assert.False(parsed.RootElement.TryGetProperty("StackTrace", out _));
                }
                var line = Assert.Single(entries, entry =>
                {
                    using var parsed = JsonDocument.Parse(entry);
                    return parsed.RootElement.TryGetProperty("eventId", out var id) && id.GetInt32() == 5102;
                });
                using var json = JsonDocument.Parse(line);
                Assert.Equal("CRITICAL", json.RootElement.GetProperty("severity").GetString());
                Assert.Equal("StartupFailure", json.RootElement.GetProperty("EventName").GetString());
                Assert.Equal("HostInitialization", json.RootElement.GetProperty("Operation").GetString());
                Assert.Matches("^[A-Za-z][A-Za-z0-9_.+`]{0,255}$", json.RootElement.GetProperty("exceptionType").GetString()!);
                Assert.False(json.RootElement.TryGetProperty("State", out _));
                Assert.False(json.RootElement.TryGetProperty("Scopes", out _));
            }
            finally
            {
                using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    // Cancel owned readers, then terminate only the child identified by the retained handle.
                    deadline.Cancel();
                    if (!child.HasExited) child.Kill();
                    await child.WaitForExitAsync(cleanupDeadline.Token);
                    try { await DrainAsync(stdout, stderr, cleanupDeadline.Token); }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cleanupDeadline.IsCancellationRequested)
                    {
                        // Cancellation of the owned output reads is expected during cleanup.
                    }
                    Assert.True(child.HasExited);
                }
                finally
                {
                    child.StandardOutput.Dispose();
                    child.StandardError.Dispose();
                }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OutputDrain_StalledOwnedReaderHonorsDeadline(bool stdoutStalls)
    {
        var stalled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource();
        var completed = Task.FromResult(string.Empty);
        try
        {
            var drain = DrainAsync(stdoutStalls ? stalled.Task : completed,
                stdoutStalls ? completed : stalled.Task, deadline.Token);
            Assert.False(drain.IsCompleted);
            deadline.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { stalled.TrySetResult(string.Empty); }
    }

    private static Task<string[]> DrainAsync(Task<string> stdout, Task<string> stderr, CancellationToken deadline)
        => Task.WhenAll(stdout, stderr).WaitAsync(deadline);

    [Fact]
    public void ActualMvcEntryPoint_HostAbortedPassthroughPreservesConfiguredHostWithoutDatabaseWork()
    {
        var originalExitCode = Environment.ExitCode;
        try
        {
            using var environment = new CatalogEnvironmentScope(null);
            using var fixture = new CatalogDocumentationHost("Production");
            Assert.NotNull(fixture.Host.Services);
            fixture.AssertNoDatabaseWork();
            Assert.Equal(originalExitCode, Environment.ExitCode);
        }
        finally { Environment.ExitCode = originalExitCode; }
    }

    private static Dictionary<string, string?> Settings()
    {
        using var rsa = RSA.Create(2048);
        return new()
        {
            ["ConnectionStrings:CatalogDbContext"] = "Host=127.0.0.1;Port=1;Database=startup_control;Username=fixture",
            ["ConnectionStrings:CountryDbContext"] = "Host=127.0.0.1;Port=1;Database=startup_control;Username=fixture",
            ["ConnectionStrings:CurrencyDbContext"] = "Host=127.0.0.1;Port=1;Database=startup_control;Username=fixture",
            ["Cache:RedisEnabled"] = "false",
            ["InstantQuotationCatalog:ReconciliationEnabled"] = "false",
            ["CORS:AllowedOrigins:0"] = "https://startup.example.test",
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
            ["Jwt:Issuer"] = "https://startup.example.test",
            ["Jwt:Audience"] = "startup-control",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            ["Observability:TracingEnabled"] = "false",
            ["Observability:RuntimeMetricsEnabled"] = "false"
        };
    }
}
