using AiMemory.Services;
using FluentAssertions;
using Xunit;

namespace AiMemory.Tests.Unit;

public sealed class ChunkingServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ChunkingService _service = new();

    public ChunkingServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "aimem-chunk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { }
    }

    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    [Fact]
    public void ChunkFile_CSharpWithSmallClass_ReturnsSingleTypeChunk()
    {
        var path = WriteFile("Foo.cs",
        """
        namespace MyNamespace;

        public class Foo
        {
            public int DoSomething(int value) => value + 1;
        }
        """);

        var chunks = _service.ChunkFile("Proj", _tempRoot, path).ToList();

        chunks.Should().ContainSingle();
        chunks[0].ChunkType.Should().Be("type");
        chunks[0].SymbolName.Should().Contain("Foo");
        chunks[0].Language.Should().Be("csharp");
    }

    [Fact]
    public void ChunkFile_EmptyCSharpFile_ReturnsNoChunks()
    {
        var path = WriteFile("Empty.cs", "");

        var chunks = _service.ChunkFile("Proj", _tempRoot, path).ToList();

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkFile_SqlProcedure_ChunkHasProcedureSymbol()
    {
        var path = WriteFile("proc.sql",
        """
        create procedure dbo.MyProc
        as
        begin
            select * from Users where Id = 1;
        end
        """);

        var chunks = _service.ChunkFile("Proj", _tempRoot, path).ToList();

        chunks.Should().ContainSingle();
        chunks[0].Language.Should().Be("sql");
        chunks[0].SymbolName.Should().Be("dbo.MyProc");
        chunks[0].ChunkType.Should().Be("procedure");
    }

    [Fact]
    public void ChunkFile_MarkdownWithHeaders_ReturnsSectionChunks()
    {
        var path = WriteFile("doc.md",
        """
        # Title One
        This is the body content for title one section here.
        ## Subtitle Two
        This is the body content for subtitle two section here.
        """);

        var chunks = _service.ChunkFile("Proj", _tempRoot, path).ToList();

        chunks.Should().HaveCount(2);
        chunks.Should().OnlyContain(c => c.ChunkType == "section");
        chunks[0].SymbolName.Should().Be("Title One");
        chunks[1].SymbolName.Should().Be("Subtitle Two");
    }

    [Fact]
    public void ChunkFile_CSharpWithFactAttribute_ReturnsNoChunks()
    {
        var path = WriteFile("Calculator.cs",
        """
        using Xunit;

        public class CalculatorTests
        {
            [Fact]
            public void ShouldAdd() { }
        }
        """);

        var chunks = _service.ChunkFile("Proj", _tempRoot, path).ToList();

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkFile_LargeJsonFile_RespectsMaxChunkLength()
    {
        var line = new string('x', 100);
        var content = string.Join("\n", Enumerable.Repeat(line, 70));
        var path = WriteFile("data.json", content);

        var chunks = _service.ChunkFile("Proj", _tempRoot, path).ToList();

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(c => c.Content.Length <= 6000);
    }
}
