using AiMemory.Configuration;
using AiMemory.Models;
using Npgsql;

namespace AiMemory.Services
{
    public static class SqlPredicateBuilder
    {
        public static string BuildRuleCandidatePredicate(PatternsConfig patterns)
        {
            var parts = patterns.Rules.ContentPatterns.Select(p =>
                p.Contains('%') ? $"c.content ILIKE '{EscapeSqlLiteral(p)}'" : $"c.content ILIKE '%{EscapeSqlLiteral(p)}%'");
            return string.Join(" OR ", parts);
        }

        public static string BuildKnowledgeCandidatePredicate(PatternsConfig patterns)
        {
            var fileParts = patterns.Knowledge.FilePathPatterns.Select(p =>
                $"c.file_path ILIKE '{EscapeSqlLiteral(p)}'");
            var contentParts = patterns.Knowledge.ContentPatterns.Select(p =>
                p.Contains('%') ? $"c.content ILIKE '{EscapeSqlLiteral(p)}'" : $"c.content ILIKE '%{EscapeSqlLiteral(p)}%'");
            return string.Join(" OR ", fileParts.Concat(contentParts));
        }

        private static string EscapeSqlLiteral(string value)
        {
            return value.Replace("'", "''");
        }
    }

    public sealed class PgVectorService : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ChunkRepository _chunks;
        private readonly RuleRepository _rules;
        private readonly KnowledgeRepository _knowledge;
        private readonly SearchService _search;
        private readonly SymbolGraphRepository _symbolGraph;
        private readonly ExtractionStateRepository _extractionState;

        public PgVectorService(string connectionString)
        {
            _dataSource = PgVectorDataSource.CreateDataSource(connectionString);
            _chunks = new ChunkRepository(_dataSource);
            _rules = new RuleRepository(_dataSource);
            _knowledge = new KnowledgeRepository(_dataSource);
            _search = new SearchService(_dataSource);
            _symbolGraph = new SymbolGraphRepository(_dataSource);
            _extractionState = new ExtractionStateRepository(_dataSource);
        }

        public async Task UpsertChunkAsync(string workspaceName, CodeChunk chunk, float[] embedding, CancellationToken ct = default)
            => await _chunks.UpsertChunkAsync(workspaceName, chunk, embedding, ct);

        public async Task<int> DeleteEntityFrameworkMigrationChunksAsync(string workspace, IReadOnlyList<string> projects, CancellationToken ct = default)
            => await _chunks.DeleteEntityFrameworkMigrationChunksAsync(workspace, projects, ct);

        public async Task<int> DeleteTestChunksAsync(string workspace, IReadOnlyList<string> projects, CancellationToken ct = default)
            => await _chunks.DeleteTestChunksAsync(workspace, projects, ct);

        public async Task<int> DeleteOrphanChunksAsync(string workspace, IReadOnlyList<string> projects, IReadOnlyList<string> currentFilePaths, CancellationToken ct = default)
            => await _chunks.DeleteOrphanChunksAsync(workspace, projects, currentFilePaths, ct);

        public async Task<IReadOnlyList<(string Project, string File, string? Symbol, string Content, double Distance)>> SearchAsync(float[] embedding, string query, int limit, CancellationToken ct = default)
            => await _search.SearchAsync(embedding, query, limit, ct);

        public async Task<IReadOnlyList<CodeSearchResult>> SearchCodeAsync(float[] embedding, string query, int limit, string? project, CancellationToken ct = default)
            => await _search.SearchCodeAsync(embedding, query, limit, project, ct);

        public async Task<IReadOnlyList<BusinessRuleSearchResult>> SearchBusinessRulesAsync(float[] embedding, string query, int limit, string? project, CancellationToken ct = default)
            => await _rules.SearchBusinessRulesAsync(embedding, query, limit, project, ct);

        public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchKnowledgeAsync(float[] embedding, string query, int limit, string? project, CancellationToken ct = default)
            => await _knowledge.SearchKnowledgeAsync(embedding, query, limit, project, ct);

        public async Task<IReadOnlyList<FileChunkResult>> GetFileChunksAsync(string file, string? project, int maxTotalChars, CancellationToken ct = default)
            => await _chunks.GetFileChunksAsync(file, project, maxTotalChars, ct);

        public async Task<IReadOnlyList<RelatedFileResult>> FindRelatedFilesAsync(float[] embedding, int limit, string? project, string? excludeFile, CancellationToken ct = default)
            => await _search.FindRelatedFilesAsync(embedding, limit, project, excludeFile, ct);

        public async Task<ExtractionStats> GetRuleExtractionStatsAsync(string workspace, IReadOnlyList<string> projects, bool semantic, CancellationToken ct = default)
            => await _rules.GetRuleExtractionStatsAsync(workspace, projects, semantic, ct);

        public async Task<ExtractionStats> GetKnowledgeExtractionStatsAsync(string workspace, IReadOnlyList<string> projects, bool semantic, CancellationToken ct = default)
            => await _knowledge.GetKnowledgeExtractionStatsAsync(workspace, projects, semantic, ct);

        public async Task<IReadOnlyList<ExtractionChunkResult>> GetChunksForRuleExtractionAsync(string workspace, IReadOnlyList<string> projects, int? limit, bool semantic, bool refresh, CancellationToken ct = default)
            => await _rules.GetChunksForRuleExtractionAsync(workspace, projects, limit, semantic, refresh, ct);

        public async Task<IReadOnlyList<ExtractionChunkResult>> GetChunksForKnowledgeExtractionAsync(string workspace, IReadOnlyList<string> projects, int? limit, bool semantic, bool refresh, CancellationToken ct = default)
            => await _knowledge.GetChunksForKnowledgeExtractionAsync(workspace, projects, limit, semantic, refresh, ct);

        public async Task<UpsertExtractionResult> UpsertBusinessRuleCandidateAsync(ExtractedBusinessRule rule, float[] embedding, CancellationToken ct = default)
            => await _rules.UpsertBusinessRuleCandidateAsync(rule, embedding, ct);

        public async Task<UpsertExtractionResult> UpsertKnowledgeCandidateAsync(ExtractedKnowledge knowledge, float[] embedding, CancellationToken ct = default)
            => await _knowledge.UpsertKnowledgeCandidateAsync(knowledge, embedding, ct);

        public async Task MarkExtractionChunkProcessedAsync(Guid chunkId, string stage, string contentHash, CancellationToken ct = default)
            => await _extractionState.MarkExtractionChunkProcessedAsync(chunkId, stage, contentHash, ct);

        public async Task MarkExtractionChunkFailedAsync(Guid chunkId, string stage, string contentHash, string error, CancellationToken ct = default)
            => await _extractionState.MarkExtractionChunkFailedAsync(chunkId, stage, contentHash, error, ct);

        public async Task<Guid?> UpsertSymbolAsync(int projectId, Guid? chunkId, string kind, string fullName, string filePath, int lineStart, int lineEnd, CancellationToken ct = default)
            => await _symbolGraph.UpsertSymbolAsync(projectId, chunkId, kind, fullName, filePath, lineStart, lineEnd, ct);

        public async Task UpsertSymbolRelationAsync(Guid sourceId, Guid targetId, string relation, CancellationToken ct = default)
            => await _symbolGraph.UpsertSymbolRelationAsync(sourceId, targetId, relation, ct);

        public async Task<Guid?> GetSymbolIdByNameAsync(int projectId, string fullName, CancellationToken ct = default)
            => await _symbolGraph.GetSymbolIdByNameAsync(projectId, fullName, ct);

        public async Task<int> GetProjectIdByNameAsync(string name, CancellationToken ct = default)
            => await _symbolGraph.GetProjectIdByNameAsync(name, ct);

        public async Task<Guid?> GetChunkIdBySymbolNameAsync(int projectId, string symbolName, CancellationToken ct = default)
            => await _symbolGraph.GetChunkIdBySymbolNameAsync(projectId, symbolName, ct);

        public async Task<IReadOnlyList<(string Project, string Symbol, string File, string Relation)>> GetSymbolCallersAsync(string symbolName, string? project, CancellationToken ct = default)
            => await _symbolGraph.GetSymbolCallersAsync(symbolName, project, ct);

        public async Task<IReadOnlyList<(string Project, string Symbol, string File, string Relation)>> GetSymbolCalleesAsync(string symbolName, string? project, CancellationToken ct = default)
            => await _symbolGraph.GetSymbolCalleesAsync(symbolName, project, ct);

        public async Task<IReadOnlyList<(string Project, string ParentName, string Relation)>> GetClassHierarchyAsync(string className, string? project, CancellationToken ct = default)
            => await _symbolGraph.GetClassHierarchyAsync(className, project, ct);

        public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
    }

    public sealed record CodeSearchResult(
        string Project,
        string File,
        string? Language,
        string? ChunkType,
        string? Symbol,
        string Content,
        double Distance);

    public sealed record BusinessRuleSearchResult(
        string? Project,
        string Title,
        string Description,
        string? SourceFile,
        string? Symbol,
        string Status,
        string? Evidence,
        decimal? Confidence,
        double Distance);

    public sealed record KnowledgeSearchResult(
        string? Project,
        string Kind,
        string Title,
        string Content,
        string? Source,
        string? Symbol,
        string Status,
        string? Evidence,
        decimal? Confidence,
        double Distance);

    public sealed record FileChunkResult(
        string Project,
        string File,
        string Content);

    public sealed record RelatedFileResult(
        string Project,
        string File,
        double Distance,
        int MatchedChunks,
        string? Symbols);

    public sealed record ExtractionChunkResult(
        Guid Id,
        string Project,
        string File,
        string? Language,
        string? ChunkType,
        string? Symbol,
        string Content,
        string ContentHash);

    public sealed record ExtractionStats(
        long TotalChunks,
        long CandidateChunks,
        long ProcessedCandidateChunks,
        long PendingCandidateChunks,
        long FailedCandidateChunks,
        long ChangedCandidateChunks,
        long ActionableCandidateChunks);

    public sealed record ExtractedBusinessRule(
        Guid ChunkId,
        string Title,
        string Description,
        string SourceFile,
        string? SymbolName,
        string Evidence,
        decimal Confidence,
        string ContentHash);

    public sealed record ExtractedKnowledge(
        Guid ChunkId,
        string Kind,
        string Title,
        string Content,
        string Source,
        string? SymbolName,
        string Evidence,
        decimal Confidence,
        string ContentHash);

    public sealed record UpsertExtractionResult(Guid? Id, string? Status, string Action);
}
