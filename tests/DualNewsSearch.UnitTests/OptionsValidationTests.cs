using System.ComponentModel.DataAnnotations;
using DualNewsSearch.Application.Configuration;
using FluentAssertions;

namespace DualNewsSearch.UnitTests;

public sealed class OptionsValidationTests
{
    [Fact]
    public void MissingEndpointsAndInvalidFusionWindowFailValidation()
    {
        var elasticsearch = new ElasticsearchOptions
        {
            Endpoint = string.Empty,
            IndexAlias = string.Empty,
            IndexName = string.Empty,
            TimeoutMs = 0
        };
        var vespa = new VespaOptions
        {
            Endpoint = "http://vespa/",
            ConfigEndpoint = string.Empty,
            Namespace = "news",
            DocumentType = "news",
            RankProfile = "cjk_bm25_all"
        };
        var fusion = new FusionOptions
        {
            EsTopK = 10,
            VespaTopK = 10,
            FinalTopK = 20,
            MaxFusionDepth = 10,
            GlobalTimeoutMs = 1000
        };

        Validate(elasticsearch).Should().NotBeEmpty();
        Validate(vespa).Should().NotBeEmpty();
        Validate(fusion).Select(x => x.ErrorMessage)
            .Should().Contain(x => x!.Contains("FinalTopK", StringComparison.Ordinal));
    }

    private static IReadOnlyList<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            instance,
            new ValidationContext(instance),
            results,
            validateAllProperties: true);
        return results;
    }
}
