using AiMemory.Models;
using AiMemory.Services;
using FluentAssertions;
using Testcontainers.PostgreSql;
using Xunit;

namespace AiMemory.Tests.Integration;

public sealed class PgVectorServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .WithDatabase("ai_memory")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private PgVectorService _pg = null!;
    private string _workspace = "test_ws";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var cs = _container.GetConnectionString();
        _pg = new PgVectorService(cs);
        await ApplySchemaAsync(cs);
    }

    public async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static async Task ApplySchemaAsync(string connectionString)
    {
        var schemaDir = Path.Combine(AppContext.BaseDirectory, "sql");
        if (!Directory.Exists(schemaDir)) return;
        await using var conn = new Npgsql.NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        foreach (var file in Directory.GetFiles(schemaDir, "*.sql").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var sql = await File.ReadAllTextAsync(file);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task UpsertAndSearch_roundtrip()
    {
        var chunk = new CodeChunk("testproj", "/root", "Foo.cs", "csharp", "type", "Foo", "public class Foo {}", AiMemory.Services.HashService.Sha256("public class Foo {}"));
        var embedding = Enumerable.Repeat(0.1f, 1024).ToArray();
        await _pg.UpsertChunkAsync(_workspace, chunk, embedding);

        var results = await _pg.SearchCodeAsync(embedding, "Foo", 5, null);
        results.Should().NotBeEmpty();
        results[0].Content.Should().Contain("Foo");
    }

    [Fact]
    public async Task UpsertAndSearch_roundtrip2()
    {
        var content = "public class Bar { }";
        var chunk = new CodeChunk("testproj2", "/root2", "Bar.cs", "csharp", "type", "Bar", content, AiMemory.Services.HashService.Sha256(content));
        var embedding = Enumerable.Range(0, 1024).Select(i => 0.2f * i).ToArray();
        await _pg.UpsertChunkAsync(_workspace, chunk, embedding);

        var results = await _pg.SearchCodeAsync(embedding, "Bar", 5, null);
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UpsertBusinessRuleCandidate_inserts_and_searches()
    {
        var content = "public class Validator { void Validate() { throw new BusinessException(\"Cliente bloqueado\"); } }";
        var chunk = new CodeChunk("rule_proj", "/rule_root", "Valid.cs", "csharp", "type", "Valid", content, AiMemory.Services.HashService.Sha256(content));
        var embed = Enumerable.Repeat(0.3f, 1024).ToArray();
        await _pg.UpsertChunkAsync(_workspace, chunk, embed);

        var stats = await _pg.GetRuleExtractionStatsAsync(_workspace, ["rule_proj"], false);
        stats.TotalChunks.Should().Be(1);
    }

    [Fact]
    public async Task DeleteOrphanChunks_removes_orphan()
    {
        var content = "public class Orphan { }";
        var chunk = new CodeChunk("orphan_proj", "/orphan_root", "Orphan.cs", "csharp", "type", "Orphan", content, AiMemory.Services.HashService.Sha256(content));
        var embed = Enumerable.Repeat(0.4f, 1024).ToArray();
        await _pg.UpsertChunkAsync(_workspace, chunk, embed);
        var resultsBefore = await _pg.SearchCodeAsync(embed, "Orphan", 5, null);
        resultsBefore.Should().NotBeEmpty();

        var deleted = await _pg.DeleteOrphanChunksAsync(_workspace, ["orphan_proj"], ["/orphan_root/OtherFile.cs"]);
        deleted.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Migration_idempotency()
    {
        var schemaDir = Path.Combine(AppContext.BaseDirectory, "sql");
        if (!Directory.Exists(schemaDir)) return;
        var files = Directory.GetFiles(schemaDir, "*.sql").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var file in files)
        {
            var sql = await File.ReadAllTextAsync(file);
            await using var conn2 = new Npgsql.NpgsqlConnection(_container.GetConnectionString());
            await conn2.OpenAsync();
            await using var cmd = conn2.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
        true.Should().BeTrue();
    }
}
