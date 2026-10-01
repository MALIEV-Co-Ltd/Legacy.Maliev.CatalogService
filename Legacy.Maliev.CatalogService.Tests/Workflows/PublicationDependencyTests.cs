using YamlDotNet.RepresentationModel;

namespace Legacy.Maliev.CatalogService.Tests.Workflows;

public sealed class PublicationDependencyTests
{
    [Fact]
    public void Publisher_ProvidesPinnedDockerDependencies_WithoutOpeningDeploymentGate()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Legacy.Maliev.CatalogService.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(root.FullName, ".github", "workflows", "publish-image.yml"))));
        var document = (YamlMappingNode)yaml.Documents[0].RootNode;
        Assert.Single(yaml.Documents);
        var workflowPermissions = (YamlMappingNode)document.Children[new YamlScalarNode("permissions")];
        Assert.Single(workflowPermissions.Children);
        Assert.Equal("read", Value(workflowPermissions, "contents"));
        var jobs = (YamlMappingNode)document.Children[new YamlScalarNode("jobs")];
        Assert.Equal(2, jobs.Children.Count);
        var deploymentGate = (YamlMappingNode)jobs.Children[new YamlScalarNode("deployment-gate")];
        Assert.Equal("vars.LEGACY_DEPLOY_ENABLED != 'true'", Value(deploymentGate, "if"));
        Assert.False(deploymentGate.Children.ContainsKey(new YamlScalarNode("permissions")));
        var publish = (YamlMappingNode)jobs.Children[new YamlScalarNode("publish")];
        Assert.Equal("vars.LEGACY_DEPLOY_ENABLED == 'true'", Value(publish, "if"));
        Assert.Equal("MALIEV-Co-Ltd/Legacy.Maliev.Workflows/.github/workflows/publish-image.yml@503e8846390a597c267d2889b33a9c26863389b3", Value(publish, "uses"));
        var permissions = (YamlMappingNode)publish.Children[new YamlScalarNode("permissions")];
        Assert.Equal(3, permissions.Children.Count);
        Assert.Equal("read", Value(permissions, "contents"));
        Assert.Equal("read", Value(permissions, "actions"));
        Assert.Equal("write", Value(permissions, "id-token"));
        var inputs = (YamlMappingNode)publish.Children[new YamlScalarNode("with")];
        Assert.Equal(8, inputs.Children.Count);
        Assert.Equal("${{ vars.LEGACY_ARTIFACT_REGISTRY }}/legacy-maliev-catalog-service", Value(inputs, "image"));
        Assert.Equal("Legacy.Maliev.CatalogService.Api/Dockerfile", Value(inputs, "dockerfile"));
        Assert.Equal("003b255f0fb0f0bce032f5b5ff15d28be0c8c391", Value(inputs, "legacy-service-defaults-ref"));
        Assert.Equal("78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7", Value(inputs, "compatibility-contracts-ref"));
        Assert.Equal(".", Value(inputs, "context"));
        Assert.Equal("legacy-production", Value(inputs, "environment"));
        Assert.Equal("${{ vars.LEGACY_WORKLOAD_IDENTITY_PROVIDER }}", Value(inputs, "workload-identity-provider"));
        Assert.Equal("${{ vars.LEGACY_CATALOG_PUBLISHER_SERVICE_ACCOUNT }}", Value(inputs, "service-account"));

        var dockerfile = File.ReadAllText(Path.Combine(root.FullName, "Legacy.Maliev.CatalogService.Api", "Dockerfile"));
        Assert.Contains("COPY Directory.Build.props .", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN dotnet restore", dockerfile, StringComparison.Ordinal);
        Assert.True(dockerfile.IndexOf("COPY Directory.Build.props .", StringComparison.Ordinal) <
                    dockerfile.IndexOf("RUN dotnet restore", StringComparison.Ordinal));
    }

    private static string? Value(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;
}
