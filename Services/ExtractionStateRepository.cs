using Npgsql;

namespace AiMemory.Services
{
    public sealed class ExtractionStateRepository
    {
        private readonly NpgsqlDataSource _dataSource;

        public ExtractionStateRepository(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task MarkExtractionChunkProcessedAsync(Guid chunkId, string stage, string contentHash, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        INSERT INTO ai_extraction_chunk_state(chunk_id, stage, content_hash, status, processed_at, error, updated_at)
        VALUES ($1, $2, $3, 'processed', NOW(), NULL, NOW())
        ON CONFLICT(chunk_id, stage)
        DO UPDATE SET content_hash = EXCLUDED.content_hash,
                      status = 'processed',
                      processed_at = NOW(),
                      error = NULL,
                      updated_at = NOW();
        """;
            cmd.Parameters.AddWithValue(chunkId);
            cmd.Parameters.AddWithValue(stage);
            cmd.Parameters.AddWithValue(contentHash);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task MarkExtractionChunkFailedAsync(Guid chunkId, string stage, string contentHash, string error, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        INSERT INTO ai_extraction_chunk_state(chunk_id, stage, content_hash, status, processed_at, error, updated_at)
        VALUES ($1, $2, $3, 'failed', NOW(), $4, NOW())
        ON CONFLICT(chunk_id, stage)
        DO UPDATE SET content_hash = EXCLUDED.content_hash,
                      status = 'failed',
                      processed_at = NOW(),
                      error = EXCLUDED.error,
                      updated_at = NOW();
        """;
            cmd.Parameters.AddWithValue(chunkId);
            cmd.Parameters.AddWithValue(stage);
            cmd.Parameters.AddWithValue(contentHash);
            cmd.Parameters.AddWithValue(error.Length <= 2_000 ? error : error[..2_000]);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
