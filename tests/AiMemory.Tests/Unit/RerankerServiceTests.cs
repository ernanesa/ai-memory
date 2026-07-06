using AiMemory.Services;
using FluentAssertions;
using Xunit;

namespace AiMemory.Tests.Unit;

public class RerankerServiceTests
{
    private static CodeSearchResult MakeResult(double distance, string? symbol = null, string? language = null) =>
        new("Proj", "src/File.cs", language, "type", symbol, "content", distance);

    [Fact]
    public void RerankCode_WhenQueryContainsSymbolTerm_BoostsScore()
    {
        var result = MakeResult(1.0, symbol: "FooBar", language: "csharp");

        var reranked = RerankerService.RerankCode(new[] { result }, "foobar");

        reranked.Should().HaveCount(1);
        reranked[0].Distance.Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void RerankCode_StructuralQueryWithConfigResult_AppliesPenalty()
    {
        var result = MakeResult(1.0, symbol: null, language: "json");

        var reranked = RerankerService.RerankCode(new[] { result }, "class Foo");

        reranked.Should().HaveCount(1);
        reranked[0].Distance.Should().BeLessThan(1.0);
        reranked[0].Distance.Should().Be(0.85);
    }

    [Fact]
    public void RerankCode_EmptyInput_ReturnsEmptyList()
    {
        var reranked = RerankerService.RerankCode(Array.Empty<CodeSearchResult>(), "anything");

        reranked.Should().BeEmpty();
    }

    [Fact]
    public void RerankCode_ReturnsOrderedByScoreDescending()
    {
        var boosted = MakeResult(1.0, symbol: "Important", language: "csharp");
        var penalized = MakeResult(1.0, symbol: null, language: "json");

        var reranked = RerankerService.RerankCode(new[] { penalized, boosted }, "class Important");

        reranked.Should().HaveCount(2);
        reranked[0].Distance.Should().BeGreaterThanOrEqualTo(reranked[1].Distance);
        reranked[0].Symbol.Should().Be("Important");
    }
}
