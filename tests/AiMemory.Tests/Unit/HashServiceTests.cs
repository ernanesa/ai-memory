using System.Security.Cryptography;
using AiMemory.Services;
using FluentAssertions;
using Xunit;

namespace AiMemory.Tests.Unit;

public class HashServiceTests
{
    [Fact]
    public void Sha256_OfHello_ReturnsDeterministicNonEmptyValue()
    {
        var first = HashService.Sha256("hello");
        var second = HashService.Sha256("hello");

        first.Should().NotBeEmpty();
        first.Should().Be(second);
    }

    [Fact]
    public void Sha256_OfEmptyString_ReturnsKnownEmptyHash()
    {
        var result = HashService.Sha256(string.Empty);

        var expected = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
        result.Should().Be(expected);
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Sha256_OfNull_ThrowsArgumentNullException()
    {
        var act = () => HashService.Sha256(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sha256_OfA_IsDifferentFromB()
    {
        var a = HashService.Sha256("a");
        var b = HashService.Sha256("b");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Sha256_ReturnsLowercaseHex()
    {
        var result = HashService.Sha256("sample");
        result.Should().MatchRegex("^[0-9a-f]+$");
        result.Should().HaveLength(64);
    }
}
