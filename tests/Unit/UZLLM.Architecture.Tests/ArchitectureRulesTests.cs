using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace UZLLM.Architecture.Tests;

public sealed class ArchitectureRulesTests
{
    private static readonly string[] ModuleNames =
    [
        "Identity", "Organizations", "Projects", "ApiKeys", "Catalog", "Routing",
        "Providers", "Usage", "Billing", "Payments", "Notifications", "Audit"
    ];

    private static string[] DiscoveredModuleNames => Directory
        .GetDirectories(Path.Combine(RepositoryRoot, "src", "Modules"))
        .Select(directory => new DirectoryInfo(directory).Name)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    // These pre-existing web/composition seams are frozen until their contracts are extracted.
    // A new caller or another implementation type is not implicitly approved.
    private static readonly HashSet<(string Source, string Namespace, string Type)> LegacyInfrastructureSeams =
    [
        ("Identity", "UZLLM.Modules.Audit.Infrastructure", "AuditServiceCollectionExtensions"),
        ("ApiKeys", "UZLLM.Modules.Identity.Infrastructure", "IdentityRouteHandlerBuilderExtensions"),
        ("Organizations", "UZLLM.Modules.Identity.Infrastructure", "IdentityRouteHandlerBuilderExtensions"),
        ("Projects", "UZLLM.Modules.Identity.Infrastructure", "IdentityRouteHandlerBuilderExtensions"),
        ("Payments", "UZLLM.Modules.Identity.Infrastructure", "IdentityRouteHandlerBuilderExtensions"),
        ("Providers", "UZLLM.Modules.Identity.Infrastructure", "IdentityRouteHandlerBuilderExtensions"),
        ("Notifications", "UZLLM.Modules.Identity.Infrastructure", "IdentityEmailPayloadCodec")
    ];

    [Theory]
    [InlineData("Billing", "UZLLM.Modules.Usage.Contracts", "UsageEvidence", true)]
    [InlineData("Billing", "UZLLM.Modules.Usage.Infrastructure", "PostgreSqlUsageStore", false)]
    [InlineData("Identity", "UZLLM.Modules.Audit.Infrastructure", "AuditServiceCollectionExtensions", true)]
    [InlineData("Payments", "UZLLM.Modules.Identity.Infrastructure", "IdentityRouteHandlerBuilderExtensions", true)]
    [InlineData("Payments", "UZLLM.Modules.Identity.Infrastructure", "PostgreSqlIdentityStore", false)]
    [InlineData("Notifications", "UZLLM.Modules.Identity.Infrastructure", "IdentityEmailPayloadCodec", true)]
    [InlineData("Catalog", "UZLLM.Modules.Identity.Infrastructure", "IdentityEmailPayloadCodec", false)]
    [InlineData("Billing", "UZLLM.Provider.OpenAI.Infrastructure", "OpenAiAdapter", false)]
    [InlineData("Billing", "UZLLM.Gateway.Api", "GatewayHost", false)]
    public void Cross_module_reference_policy_accepts_only_contracts_and_recorded_seams(
        string source, string targetNamespace, string type, bool expected)
    {
        Assert.Equal(expected, IsAllowedModuleTypeReference(source, targetNamespace, type));
    }

    [Fact]
    public void Business_modules_do_not_use_other_modules_implementation_types()
    {
        var violations = DiscoveredModuleNames
            .SelectMany(source => ReadTypeReferences(source)
                .Where(reference => !IsAllowedModuleTypeReference(source, reference.Namespace, reference.Type))
                .Select(reference => $"{source} -> {reference.Namespace}.{reference.Type}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0,
            "Cross-module implementation access must use a contract or a documented legacy seam: "
            + string.Join("; ", violations));
    }

    [Fact]
    public void Module_reference_scan_sees_contracts_and_the_recorded_legacy_seams()
    {
        Assert.Contains(ReadTypeReferences("Billing"), reference =>
            reference.Namespace == "UZLLM.Modules.Usage.Contracts"
            && reference.Type == "UsageEvidence");
        Assert.Contains(ReadTypeReferences("Identity"), reference =>
            reference.Namespace == "UZLLM.Modules.Audit.Infrastructure"
            && reference.Type == "AuditServiceCollectionExtensions");
        Assert.Contains(ReadTypeReferences("Notifications"), reference =>
            reference.Namespace == "UZLLM.Modules.Identity.Infrastructure"
            && reference.Type == "IdentityEmailPayloadCodec");
        Assert.Contains(ReadTypeReferences("Billing"), reference =>
            reference.Namespace == "System.Diagnostics"
            && reference.Type == "DebuggableAttribute+DebuggingModes");
    }

    [Fact]
    public void Business_module_project_references_stay_inside_modules_and_building_blocks()
    {
        var modulesRoot = Path.Combine(RepositoryRoot, "src", "Modules") + Path.DirectorySeparatorChar;
        var buildingBlocksRoot = Path.Combine(RepositoryRoot, "src", "BuildingBlocks") + Path.DirectorySeparatorChar;

        foreach (var moduleName in DiscoveredModuleNames)
        {
            var projectPath = Path.Combine(modulesRoot, moduleName, $"UZLLM.Modules.{moduleName}.csproj");
            var project = XDocument.Load(projectPath);
            foreach (var reference in project.Descendants("ProjectReference"))
            {
                var include = Assert.IsType<string>(reference.Attribute("Include")?.Value);
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!,
                    include.Replace('\\', Path.DirectorySeparatorChar)));
                Assert.True(target.StartsWith(modulesRoot, StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith(buildingBlocksRoot, StringComparison.OrdinalIgnoreCase),
                    $"{moduleName} cannot reference a host or provider adapter: {include}");

                if (target.StartsWith(modulesRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var targetModule = Path.GetFileName(Path.GetDirectoryName(target));
                    var contractNamespace = $"UZLLM.Modules.{targetModule}.Contracts";
                    Assert.Contains(ReadTypeReferences(moduleName), typeReference =>
                        typeReference.Namespace.StartsWith($"UZLLM.Modules.{targetModule}.",
                            StringComparison.Ordinal)
                        && (typeReference.Namespace == contractNamespace
                            || typeReference.Namespace.StartsWith(contractNamespace + ".",
                                StringComparison.Ordinal)
                            || LegacyInfrastructureSeams.Contains((moduleName, typeReference.Namespace,
                                typeReference.Type))));
                }
            }
        }
    }

    [Fact]
    public void Production_projects_target_net10_and_do_not_pin_package_versions()
    {
        var projectFiles = Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories);

        Assert.NotEmpty(projectFiles);

        foreach (var projectFile in projectFiles)
        {
            var project = XDocument.Load(projectFile);
            Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
            Assert.DoesNotContain(project.Descendants("PackageReference"), reference => reference.Attribute("Version") is not null);
        }
    }

    [Fact]
    public void Every_business_module_has_required_internal_structure()
    {
        foreach (var moduleName in ModuleNames)
        {
            var moduleDirectory = Path.Combine(RepositoryRoot, "src", "Modules", moduleName);

            Assert.True(File.Exists(Path.Combine(moduleDirectory, $"UZLLM.Modules.{moduleName}.csproj")));
            Assert.True(Directory.Exists(Path.Combine(moduleDirectory, "Domain")));
            Assert.True(Directory.Exists(Path.Combine(moduleDirectory, "Application")));
            Assert.True(Directory.Exists(Path.Combine(moduleDirectory, "Infrastructure")));
            Assert.True(Directory.Exists(Path.Combine(moduleDirectory, "Contracts")));
        }
    }

    [Fact]
    public void Deployable_hosts_do_not_reference_each_other()
    {
        var hostProjects = new[]
        {
            "UZLLM.Gateway.Api",
            "UZLLM.Management.Api",
            "UZLLM.Worker"
        };

        foreach (var hostProject in hostProjects)
        {
            var projectPath = Path.Combine(RepositoryRoot, "src", hostProject, $"{hostProject}.csproj");
            var project = XDocument.Load(projectPath);
            var references = project.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty);

            Assert.DoesNotContain(references, reference => hostProjects.Any(host => reference.Contains(host, StringComparison.Ordinal)));
        }
    }

    private static string RepositoryRoot => FindRepositoryRoot(AppContext.BaseDirectory);

    private static bool IsAllowedModuleTypeReference(string source, string targetNamespace, string type)
    {
        if (targetNamespace.StartsWith("UZLLM.Provider.", StringComparison.Ordinal)
            || targetNamespace.StartsWith("UZLLM.Gateway.", StringComparison.Ordinal)
            || targetNamespace.StartsWith("UZLLM.Management.", StringComparison.Ordinal)
            || targetNamespace.StartsWith("UZLLM.Worker", StringComparison.Ordinal)) return false;
        if (!targetNamespace.StartsWith("UZLLM.Modules.", StringComparison.Ordinal)) return true;
        var parts = targetNamespace.Split('.');
        if (parts.Length < 4 || !DiscoveredModuleNames.Contains(parts[2], StringComparer.Ordinal)) return false;
        if (parts[2] == source || parts[3] == "Contracts") return true;
        return LegacyInfrastructureSeams.Contains((source, targetNamespace, type));
    }

    private static IReadOnlyCollection<(string Namespace, string Type)> ReadTypeReferences(string moduleName)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var assemblyName = $"UZLLM.Modules.{moduleName}.dll";
        var assemblyPath = Path.Combine(RepositoryRoot, "artifacts", "bin",
            $"UZLLM.Modules.{moduleName}", configuration, assemblyName);
        Assert.True(File.Exists(assemblyPath), $"Build {assemblyName} before checking module references.");

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        return metadata.TypeReferences.Select(handle => DescribeTypeReference(metadata, handle))
            .Distinct().ToArray();
    }

    private static (string Namespace, string Type) DescribeTypeReference(
        MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        var name = metadata.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var parent = DescribeTypeReference(metadata, (TypeReferenceHandle)reference.ResolutionScope);
            return (parent.Namespace, $"{parent.Type}+{name}");
        }

        return (metadata.GetString(reference.Namespace), name);
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UZLLM.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
