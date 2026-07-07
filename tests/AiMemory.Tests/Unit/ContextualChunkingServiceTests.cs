using AiMemory.Models;
using AiMemory.Services;
using FluentAssertions;
using Xunit;

namespace AiMemory.Tests.Unit;

public class ContextualChunkingServiceTests
{
    private static CodeChunk MakeChunk(string? language, string? symbolName, string content = "code") =>
        new("MyProject", "/root", "src/File.cs", language, "type", symbolName, content, "hash");

    [Fact]
    public void GetContextualContent_ForCSharpChunkWithSymbol_ContainsAllExpectedFields()
    {
        var chunk = MakeChunk("csharp", "Namespace.Foo.Bar");

        var result = ContextualChunkingService.GetContextualContent(chunk);

        result.Should().StartWith("[CONTEXT:");
        result.Should().Contain("Project: MyProject");
        result.Should().Contain("File: src/File.cs");
        result.Should().Contain("Lang: C#");
        result.Should().Contain("Symbol: Namespace.Foo.Bar");
        result.Should().Contain("ChunkType: type");
    }

    [Fact]
    public void GetContextualContent_ForSqlChunk_ContainsSqlLanguage()
    {
        var chunk = MakeChunk("sql", "dbo.MyProc");

        var result = ContextualChunkingService.GetContextualContent(chunk);

        result.Should().StartWith("[CONTEXT:");
        result.Should().Contain("Lang: SQL");
        result.Should().Contain("Symbol: dbo.MyProc");
        result.Should().NotContain("ChunkType:");
    }

    [Fact]
    public void GetContextualContent_ForMarkdownChunk_ContainsMarkdownLanguage()
    {
        var chunk = MakeChunk("markdown", "Section Title");

        var result = ContextualChunkingService.GetContextualContent(chunk);

        result.Should().StartWith("[CONTEXT:");
        result.Should().Contain("Lang: Markdown");
        result.Should().Contain("Section: Section Title");
    }

    [Fact]
    public void GetContextualContent_ForChunkWithoutLanguage_DoesNotContainLangPrefix()
    {
        var chunk = MakeChunk(null, null);

        var result = ContextualChunkingService.GetContextualContent(chunk);

        result.Should().StartWith("[CONTEXT:");
        result.Should().NotContain("Lang:");
    }

    [Fact]
    public void GetContextualContent_AlwaysStartsWithContextPrefix()
    {
        var chunk = MakeChunk("json", null, "body");

        var result = ContextualChunkingService.GetContextualContent(chunk);

        result.Should().StartWith("[CONTEXT:");
        result.Should().Contain("body");
    }
}
