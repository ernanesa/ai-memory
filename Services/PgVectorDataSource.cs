using Npgsql;
using Pgvector;

namespace AiMemory.Services
{
    public static class PgVectorDataSource
    {
        public static NpgsqlDataSource CreateDataSource(string connectionString)
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            return builder.Build();
        }
    }
}
