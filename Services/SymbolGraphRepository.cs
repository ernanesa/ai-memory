using Npgsql;

namespace AiMemory.Services
{
    public sealed class SymbolGraphRepository
    {
        private readonly NpgsqlDataSource _dataSource;

        public SymbolGraphRepository(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<Guid?> UpsertSymbolAsync(int projectId, Guid? chunkId, string kind, string fullName, string filePath, int lineStart, int lineEnd, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        INSERT INTO ai_symbols (project_id, chunk_id, kind, full_name, file_path, line_start, line_end)
        VALUES ($1, $2, $3, $4, $5, $6, $7)
        ON CONFLICT (project_id, full_name)
        DO UPDATE SET chunk_id = EXCLUDED.chunk_id,
                      kind = EXCLUDED.kind,
                      file_path = EXCLUDED.file_path,
                      line_start = EXCLUDED.line_start,
                      line_end = EXCLUDED.line_end
        RETURNING id;
        """;
            cmd.Parameters.AddWithValue(projectId);
            cmd.Parameters.AddWithValue((object?)chunkId ?? DBNull.Value);
            cmd.Parameters.AddWithValue(kind);
            cmd.Parameters.AddWithValue(fullName);
            cmd.Parameters.AddWithValue(filePath);
            cmd.Parameters.AddWithValue(lineStart);
            cmd.Parameters.AddWithValue(lineEnd);
            return (Guid?)await cmd.ExecuteScalarAsync(ct);
        }

        public async Task UpsertSymbolRelationAsync(Guid sourceId, Guid targetId, string relation, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        INSERT INTO ai_symbol_relations (source_id, target_id, relation)
        VALUES ($1, $2, $3)
        ON CONFLICT (source_id, target_id, relation)
        DO NOTHING;
        """;
            cmd.Parameters.AddWithValue(sourceId);
            cmd.Parameters.AddWithValue(targetId);
            cmd.Parameters.AddWithValue(relation);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<Guid?> GetSymbolIdByNameAsync(int projectId, string fullName, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM ai_symbols WHERE project_id = $1 AND full_name = $2 LIMIT 1;";
            cmd.Parameters.AddWithValue(projectId);
            cmd.Parameters.AddWithValue(fullName);
            return (Guid?)await cmd.ExecuteScalarAsync(ct);
        }

        public async Task<int> GetProjectIdByNameAsync(string name, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM ai_projects WHERE name = $1 LIMIT 1;";
            cmd.Parameters.AddWithValue(name);
            var res = await cmd.ExecuteScalarAsync(ct);
            return res is int id ? id : -1;
        }

        public async Task<Guid?> GetChunkIdBySymbolNameAsync(int projectId, string symbolName, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM ai_chunks WHERE project_id = $1 AND symbol_name = $2 LIMIT 1;";
            cmd.Parameters.AddWithValue(projectId);
            cmd.Parameters.AddWithValue(symbolName);
            return (Guid?)await cmd.ExecuteScalarAsync(ct);
        }

        public async Task<IReadOnlyList<(string Project, string Symbol, string File, string Relation)>> GetSymbolCallersAsync(string symbolName, string? project, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        SELECT p_source.name, s_source.full_name, s_source.file_path, r.relation
        FROM ai_symbol_relations r
        JOIN ai_symbols s_source ON s_source.id = r.source_id
        JOIN ai_symbols s_target ON s_target.id = r.target_id
        JOIN ai_projects p_source ON p_source.id = s_source.project_id
        JOIN ai_projects p_target ON p_target.id = s_target.project_id
        WHERE s_target.full_name ILIKE $1
          AND ($2::text IS NULL OR p_target.name = $2)
        ORDER BY p_source.name, s_source.full_name;
        """;
            cmd.Parameters.AddWithValue("%" + symbolName);
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(project) ?? DBNull.Value);

            var list = new List<(string, string, string, string)>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            return list;
        }

        public async Task<IReadOnlyList<(string Project, string Symbol, string File, string Relation)>> GetSymbolCalleesAsync(string symbolName, string? project, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        SELECT p_target.name, s_target.full_name, s_target.file_path, r.relation
        FROM ai_symbol_relations r
        JOIN ai_symbols s_source ON s_source.id = r.source_id
        JOIN ai_symbols s_target ON s_target.id = r.target_id
        JOIN ai_projects p_source ON p_source.id = s_source.project_id
        JOIN ai_projects p_target ON p_target.id = s_target.project_id
        WHERE s_source.full_name ILIKE $1
          AND ($2::text IS NULL OR p_source.name = $2)
        ORDER BY p_target.name, s_target.full_name;
        """;
            cmd.Parameters.AddWithValue("%" + symbolName);
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(project) ?? DBNull.Value);

            var list = new List<(string, string, string, string)>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            return list;
        }

        public async Task<IReadOnlyList<(string Project, string ParentName, string Relation)>> GetClassHierarchyAsync(string className, string? project, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        SELECT p_target.name, s_target.full_name, r.relation
        FROM ai_symbol_relations r
        JOIN ai_symbols s_source ON s_source.id = r.source_id
        JOIN ai_symbols s_target ON s_target.id = r.target_id
        JOIN ai_projects p_target ON p_target.id = s_target.project_id
        JOIN ai_projects p_source ON p_source.id = s_source.project_id
        WHERE s_source.full_name ILIKE $1
          AND r.relation IN ('inherits', 'implements')
          AND ($2::text IS NULL OR p_source.name = $2)
        ORDER BY s_target.full_name;
        """;
            cmd.Parameters.AddWithValue("%" + className);
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(project) ?? DBNull.Value);

            var list = new List<(string, string, string)>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            return list;
        }
    }
}
