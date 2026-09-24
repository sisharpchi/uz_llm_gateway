using System.Xml.Linq;

namespace UZLLM.Architecture.Tests;

public sealed class ArchitectureRulesTests
{
    private static readonly string[] ModuleNames =
    [
        "Identity", "Organizations", "Projects", "ApiKeys", "Catalog", "Routing",
        "Providers", "Usage", "Billing", "Payments", "Notifications", "Audit"
    ];

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
