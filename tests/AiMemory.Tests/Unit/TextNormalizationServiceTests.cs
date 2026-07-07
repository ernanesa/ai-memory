using AiMemory.Services;
using FluentAssertions;
using Xunit;

namespace AiMemory.Tests.Unit;

public class TextNormalizationServiceTests
{
    [Fact]
    public void NormalizeSentence_CollapsesWhitespaceAndTrims()
    {
        var result = TextNormalizationService.NormalizeSentence("  hello   world\twith\nnewlines  ");
        result.Should().Be("hello world with newlines");
    }

    [Fact]
    public void NormalizeSentence_TrimsSurroundingPunctuation()
    {
        var result = TextNormalizationService.NormalizeSentence("\"'hello world.';,");
        result.Should().Be("hello world");
    }

    [Fact]
    public void LooksLikeBusinessRule_ForBloqueadoNaoPodeGerarCobranca_ReturnsTrue()
    {
        var result = TextNormalizationService.LooksLikeBusinessRule("Cliente bloqueado não pode gerar cobrança");
        result.Should().BeTrue();
    }

    [Fact]
    public void LooksLikeBusinessRule_ForHelloWorld_ReturnsFalse()
    {
        var result = TextNormalizationService.LooksLikeBusinessRule("Hello world");
        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeBusinessRule_ForShortString_ReturnsFalse()
    {
        var result = TextNormalizationService.LooksLikeBusinessRule("curto");
        result.Should().BeFalse();
    }

    [Fact]
    public void ToTitle_TruncatesAtMaxLengthWithEllipsis()
    {
        var result = TextNormalizationService.ToTitle("hello world this is a long sentence", 5);
        result.Should().Be("hello...");
    }

    [Fact]
    public void ToTitle_WhenWithinLimit_ReturnsFullText()
    {
        var result = TextNormalizationService.ToTitle("hello", 10);
        result.Should().Be("hello");
    }

    [Fact]
    public void Truncate_RespectsMaxLength()
    {
        var result = TextNormalizationService.Truncate("hello world", 5);
        result.Should().Be("hello...");
        result.Length.Should().BeLessThanOrEqualTo(5 + 3);
    }

    [Fact]
    public void NormalizeKey_LowercasesAndTrims()
    {
        var result = TextNormalizationService.NormalizeKey("  Hello   World  ");
        result.Should().Be("hello world");
    }
}
