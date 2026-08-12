using System.Xml.Linq;
using FluentAssertions;

namespace DualNewsSearch.UnitTests;

public sealed class DeploymentArtifactTests
{
    [Fact]
    public void VespaApplicationPackageIsXmlValidAndContainsNoSemanticFields()
    {
        string root = FindRepositoryRoot();
        string servicesPath = Path.Combine(root, "deploy", "vespa", "application", "services.xml");
        string schemaPath = Path.Combine(root, "deploy", "vespa", "application", "schemas", "news.sd");

        Action parse = () => XDocument.Load(servicesPath);
        parse.Should().NotThrow();
        string schema = File.ReadAllText(schemaPath);
        string lowerSchema = schema.ToLowerInvariant();
        schema.Should().Contain("rank-profile cjk_bm25_all");
        schema.Should().Contain("index_version");
        lowerSchema.Should().NotContain("tensor");
        lowerSchema.Should().NotContain("embedding");
        lowerSchema.Should().NotContain("onnx");
    }

    [Fact]
    public void LinuxDependencyScriptOnlyStartsAndValidatesDockerDependencies()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(
            root,
            "deploy",
            "linux-mvp",
            "dependencies-up.sh"));

        script.Should().Contain("docker run --detach");
        script.Should().NotContain("docker compose");
        script.Should().NotContain("docker network");
        script.Should().NotContain("--network");
        script.Should().Contain("analysis-ik");
        script.Should().NotContain("prepareandactivate");
        script.Should().NotContain("create-index-template.json");
        script.Should().NotContain("_aliases");

        Directory.GetFiles(Path.Combine(root, "deploy"), "docker-compose.yml", SearchOption.AllDirectories)
            .Should().BeEmpty();
        Directory.GetFiles(Path.Combine(root, "deploy"), "Dockerfile", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    [Fact]
    public void DmInitializationCreatesOnlyEmptyAuraSearchSchema()
    {
        string root = FindRepositoryRoot();
        string sql = File.ReadAllText(Path.Combine(root, "deploy", "dm", "dm.sql"));

        sql.Should().Contain("aurasearch_desired_documents");
        sql.Should().Contain("aurasearch_index_outbox");
        sql.Should().Contain("aurasearch_search_queries");
        sql.Should().Contain("aurasearch_search_results");
        sql.Should().Contain("aurasearch_search_clicks");
        sql.Should().Contain("\"Cover\" NVARCHAR2(2048) NULL");
        sql.Should().Contain("20260812005621_AddCover");
        sql.Should().NotContain("INSERT INTO \"aurasearch_desired_documents\"");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DualNewsSearch.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
