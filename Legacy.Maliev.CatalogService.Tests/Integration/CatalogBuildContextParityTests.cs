using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Data;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CatalogBuildContextCollection
{
    public const string Name = "Catalog build context and process environment";
}

[Collection(CatalogBuildContextCollection.Name)]
public sealed class CatalogBuildContextParityTests
{
    [Theory]
    [InlineData("npipe:////./pipe/dockerDesktopLinuxEngine", true)]
    [InlineData("unix:///var/run/docker.sock", true)]
    [InlineData("tcp://127.0.0.1:2375", false)]
    [InlineData("tcp://remote.example:2376", false)]
    [InlineData("npipe:////remote/pipe/docker_engine", false)]
    [InlineData("npipe:////./pipe/docker_engine/extra", false)]
    [InlineData("npipe:////./pipe/docker_engine?query", false)]
    [InlineData(null, false)]
    public void Remote_or_malformed_endpoints_are_rejected_before_build(string? endpoint, bool allowed)
    {
        Assert.Equal(allowed, IsLocalEndpoint(endpoint));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_external_connection_fails_before_any_database_connection(string? configured)
    {
        WithConnection(configured, () =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => new CatalogDbContextFactory().CreateDbContext([]));
            Assert.Equal("ConnectionStrings__CatalogDbContext is required for design-time migration commands.", error.Message);
        });
    }

    [Fact]
    public void Explicit_synthetic_connection_selects_only_PostgreSql_without_opening_it()
    {
        const string synthetic = "Host=127.0.0.1;Port=1;Database=catalog_context_probe;Username=synthetic;Pooling=false";
        WithConnection(synthetic, () =>
        {
            using var context = new CatalogDbContextFactory().CreateDbContext([]);
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
            Assert.Equal(synthetic, context.Database.GetConnectionString());
            Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        });
    }

    [Fact]
    public async Task Actual_Docker_context_excludes_nested_private_sentinels_but_retains_pinned_build_sources()
    {
        await RequireLocalDockerAsync();
        var root = FindRoot();
        var owned = Path.Combine(root, "TestResults", "catalog-context", Guid.NewGuid().ToString("N"));
        var input = Path.Combine(owned, "input");
        var output = Path.Combine(owned, "output");
        Directory.CreateDirectory(input);
        File.Copy(Path.Combine(root, ".dockerignore"), Path.Combine(input, ".dockerignore"));
        await File.WriteAllTextAsync(Path.Combine(input, "Dockerfile"), "FROM scratch\nCOPY . /context/\n");

        string[] included =
        [
            "Legacy.Maliev.CatalogService.Api/Legacy.Maliev.CatalogService.Api.csproj",
            "Legacy.Maliev.CatalogService.Api/Program.cs",
            ".dependencies/Legacy.Maliev.ServiceDefaults/src/Legacy.Maliev.ServiceDefaults/Legacy.Maliev.ServiceDefaults.csproj",
            ".dependencies/Legacy.Maliev.ServiceDefaults/src/Legacy.Maliev.ServiceDefaults/Extensions.cs",
            ".dependencies/Legacy.Maliev.CompatibilityContracts/src/Legacy.Maliev.CompatibilityContracts/Legacy.Maliev.CompatibilityContracts.csproj",
            ".dependencies/Legacy.Maliev.CompatibilityContracts/src/Legacy.Maliev.CompatibilityContracts/Shared/MessageEnvelope.cs",
        ];
        foreach (var path in included)
        {
            var destination = Path.Combine(input, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(root, path), destination);
        }

        string[] excluded =
        [
            ".git/config", ".env", ".env.synthetic",
            ".dependencies/Legacy.Maliev.ServiceDefaults/.git/config",
            ".dependencies/Legacy.Maliev.ServiceDefaults/.env",
            ".dependencies/Legacy.Maliev.ServiceDefaults/.env.synthetic",
            ".dependencies/Legacy.Maliev.ServiceDefaults/runtime.env",
            ".dependencies/Legacy.Maliev.CompatibilityContracts/.git/config",
            "Legacy.Maliev.CatalogService.Api/bin/synthetic.txt",
            "Legacy.Maliev.CatalogService.Api/obj/synthetic.txt",
        ];
        foreach (var path in excluded)
        {
            var destination = Path.Combine(input, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, "SYNTHETIC_CONTEXT_EXCLUSION_SENTINEL");
        }

        // Real Docker/BuildKit evaluates the repository's ignore file. No custom pattern matcher.
        await DockerAsync("build", "--network=none", "--no-cache", "--progress=quiet",
            "--output", $"type=local,dest={output}", "--file", Path.Combine(input, "Dockerfile"), input);
        foreach (var path in included)
        {
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(input, path)),
                await File.ReadAllBytesAsync(Path.Combine(output, "context", path)));
        }
        var leaked = excluded.Where(path => File.Exists(Path.Combine(output, "context", path))).ToArray();
        Assert.True(leaked.Length == 0, $"Excluded synthetic paths reached COPY: {string.Join(", ", leaked)}");
    }

    private static void WithConnection(string? configured, Action action)
    {
        const string key = "ConnectionStrings__CatalogDbContext";
        var original = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, configured);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.CatalogService.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Catalog test root is unavailable.");
    }

    private static async Task RequireLocalDockerAsync()
    {
        var host = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrEmpty(host))
        {
            Assert.True(IsLocalEndpoint(host), "Remote Docker endpoint refused before build.");
        }
        var context = (await DockerAsync("context", "show")).Trim();
        using var document = JsonDocument.Parse(await DockerAsync("context", "inspect", context));
        var endpoint = document.RootElement[0].GetProperty("Endpoints").GetProperty("docker").GetProperty("Host").GetString();
        Assert.True(IsLocalEndpoint(endpoint), "Non-local Docker context refused before build.");
    }

    private static bool IsLocalEndpoint(string? endpoint)
    {
        const string prefix = "npipe:////./pipe/";
        return endpoint is not null && endpoint.StartsWith(prefix, StringComparison.Ordinal)
            && endpoint.Length > prefix.Length
            && endpoint[prefix.Length..].All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
            || endpoint is "unix:///var/run/docker.sock" or "unix:///run/docker.sock";
    }

    private static async Task<string> DockerAsync(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("docker")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        process.Start();
        try
        {
            var stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            Assert.True(process.ExitCode == 0, $"Docker context prerequisite/build failed, exit {process.ExitCode}; raw output withheld.");
            return await stdout;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanup.Token);
            }
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            if (result.Length + count > 16384)
            {
                throw new InvalidOperationException("Docker diagnostic output budget exceeded; raw output withheld.");
            }
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
