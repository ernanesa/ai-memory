namespace AiMemory.Services
{
    public static class SqlPredicates
    {
        public const string RuleCandidatePredicate = """
    c.content ILIKE '%throw new%'
    OR c.content ILIKE '%BusinessException%'
    OR c.content ILIKE '%ValidationException%'
    OR c.content ILIKE '%não pode%'
    OR c.content ILIKE '%nao pode%'
    OR c.content ILIKE '%deve%'
    OR c.content ILIKE '%obrigatório%'
    OR c.content ILIKE '%obrigatorio%'
    OR c.content ILIKE '%inválido%'
    OR c.content ILIKE '%invalido%'
    OR c.content ILIKE '%ErroContext%'
    OR c.content ILIKE '%ErrosContext%'
    OR c.content ILIKE '%ErroBase%'
    OR c.content ILIKE '%AdicionarErro%'
    OR c.content ILIKE '%AdicionaErro%'
    OR c.content ILIKE '%AddErro%'
    OR c.content ILIKE '%AddError%'
    OR c.content ILIKE '%AddFailure%'
    OR c.content ILIKE '%AddNotification%'
    OR c.content ILIKE '%Notificar%'
    OR c.content ILIKE '%Notification%'
    OR c.content ILIKE '%TemErro%'
    OR c.content ILIKE '%HasError%'
    OR c.content ILIKE '%IsValid%'
    OR c.content ILIKE '%Validate%'
    OR c.content ILIKE '%Validator%'
    OR c.content ILIKE '%RuleFor%'
    OR c.content ILIKE '%Elegivel%'
    OR c.content ILIKE '%Elegível%'
    OR c.content ILIKE '%Permite%'
    OR c.content ILIKE '%Bloqueado%'
    OR c.content ILIKE '%Cancelado%'
    OR c.content ILIKE '%Vencido%'
    """;

        public const string KnowledgeCandidatePredicate = """
    c.file_path ILIKE '%.csproj'
    OR c.file_path ILIKE '%Program.cs'
    OR c.file_path ILIKE '%Startup.cs'
    OR c.file_path ILIKE '%appsettings%'
    OR c.content ILIKE '%HttpClient%'
    OR c.content ILIKE '%MassTransit%'
    OR c.content ILIKE '%RabbitMQ%'
    OR c.content ILIKE '%Kafka%'
    OR c.content ILIKE '%MediatR%'
    OR c.content ILIKE '%EntityFramework%'
    OR c.content ILIKE '%TODO%'
    OR c.content ILIKE '%FIXME%'
    OR c.content ILIKE '%HACK%'
    """;

        public static readonly string SemanticRuleCandidatePredicate = $"""
    (
        {RuleCandidatePredicate}
        OR c.file_path ILIKE '%handler%'
        OR c.file_path ILIKE '%service%'
        OR c.file_path ILIKE '%application%'
        OR c.file_path ILIKE '%domain%'
        OR c.file_path ILIKE '%usecase%'
        OR c.file_path ILIKE '%use_case%'
        OR c.file_path ILIKE '%policy%'
        OR c.file_path ILIKE '%policies%'
        OR c.file_path ILIKE '%specification%'
        OR c.file_path ILIKE '%specifications%'
        OR c.file_path ILIKE '%query%'
        OR c.symbol_name ILIKE '%handler%'
        OR c.symbol_name ILIKE '%service%'
        OR c.symbol_name ILIKE '%application%'
        OR c.symbol_name ILIKE '%domain%'
        OR c.symbol_name ILIKE '%usecase%'
        OR c.symbol_name ILIKE '%policy%'
        OR c.symbol_name ILIKE '%specification%'
        OR c.symbol_name ILIKE '%validar%'
        OR c.symbol_name ILIKE '%validate%'
        OR c.symbol_name ILIKE '%pode%'
        OR c.symbol_name ILIKE '%permite%'
        OR c.symbol_name ILIKE '%elegiv%'
        OR c.symbol_name ILIKE '%cancel%'
        OR c.symbol_name ILIKE '%aprova%'
        OR c.symbol_name ILIKE '%bloque%'
        OR c.symbol_name ILIKE '%venc%'
    )
    AND NOT (
        c.file_path ILIKE '%/constants/%'
        OR c.file_path ILIKE '%\\constants\\%'
        OR c.file_path ILIKE '%/constant/%'
        OR c.file_path ILIKE '%\\constant\\%'
        OR c.file_path ILIKE '%/mappings/%'
        OR c.file_path ILIKE '%\\mappings\\%'
        OR c.file_path ILIKE '%/mapping/%'
        OR c.file_path ILIKE '%\\mapping\\%'
        OR c.file_path ILIKE '%/entitiemappings/%'
        OR c.file_path ILIKE '%\\entitiemappings\\%'
        OR c.file_path ILIKE '%/configurations/%'
        OR c.file_path ILIKE '%\\configurations\\%'
        OR c.file_path ILIKE '%/configuration/%'
        OR c.file_path ILIKE '%\\configuration\\%'
        OR c.file_path ILIKE '%/options/%'
        OR c.file_path ILIKE '%\\options\\%'
        OR c.file_path ILIKE '%dto.cs'
        OR c.file_path ILIKE '%request.cs'
        OR c.file_path ILIKE '%response.cs'
        OR c.file_path ILIKE '%viewmodel.cs'
        OR c.content ILIKE '%IEntityTypeConfiguration<%'
        OR c.content ILIKE '%EntityTypeBuilder<%'
        OR (
            c.chunk_type = 'type'
            AND c.content ILIKE '% interface %'
        )
    )
    """;

        public static string LimitClause(int? limit)
        {
            return limit is null ? "" : $"LIMIT {limit.Value}";
        }

        public static string? NormalizeFilter(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static string EntityFrameworkMigrationContentPredicate(string alias)
        {
            return $"""
        {alias}.language = 'csharp'
        AND (
            {alias}.content ILIKE '%: Migration%'
            OR {alias}.content ILIKE '%:Migration%'
            OR {alias}.content ILIKE '%: ModelSnapshot%'
            OR {alias}.content ILIKE '%:ModelSnapshot%'
            OR {alias}.content ILIKE '%MigrationBuilder%'
            OR {alias}.content ILIKE '%BuildTargetModel%'
            OR {alias}.content ILIKE '%[Migration(%'
            OR (
                {alias}.content ILIKE '%[DbContext(%'
                AND {alias}.content ILIKE '%ProductVersion%'
            )
        )
        """;
        }

        public static string EntityFrameworkMigrationFilePredicate(string chunkAlias)
        {
            return $"""
        EXISTS (
            SELECT 1
            FROM ai_chunks ef_marker
            WHERE ef_marker.project_id = {chunkAlias}.project_id
              AND ef_marker.file_path = {chunkAlias}.file_path
              AND ({EntityFrameworkMigrationContentPredicate("ef_marker")})
        )
        """;
        }

        public static string NonEntityFrameworkMigrationFilePredicate(string chunkAlias)
        {
            return $"NOT ({EntityFrameworkMigrationFilePredicate(chunkAlias)})";
        }

        public static string TestContentPredicate(string alias)
        {
            return $"""
        (
            {alias}.content ILIKE '%<IsTestProject>true</IsTestProject>%'
            OR {alias}.content ILIKE '%Microsoft.NET.Test.Sdk%'
            OR {alias}.content ILIKE '%MSTest.Sdk%'
            OR {alias}.content ILIKE '%MSTest.TestFramework%'
            OR {alias}.content ILIKE '%coverlet.collector%'
            OR {alias}.content ILIKE '%PackageReference Include="xunit"%'
            OR {alias}.content ILIKE '%PackageReference Include=''xunit''%'
            OR {alias}.content ILIKE '%PackageReference Include="NUnit"%'
            OR {alias}.content ILIKE '%PackageReference Include=''NUnit''%'
            OR (
                {alias}.language = 'csharp'
                AND (
                    {alias}.content ILIKE '%using Xunit%'
                    OR {alias}.content ILIKE '%using NUnit.Framework%'
                    OR {alias}.content ILIKE '%Microsoft.VisualStudio.TestTools.UnitTesting%'
                    OR {alias}.content ILIKE '%[Fact%'
                    OR {alias}.content ILIKE '%[Theory%'
                    OR {alias}.content ILIKE '%[Test%'
                    OR {alias}.content ILIKE '%[TestCase%'
                    OR {alias}.content ILIKE '%[TestMethod%'
                    OR {alias}.content ILIKE '%[TestClass%'
                    OR {alias}.content ILIKE '%[TestFixture%'
                    OR {alias}.content ILIKE '%[SetUp%'
                    OR {alias}.content ILIKE '%[OneTimeSetUp%'
                    OR {alias}.content ILIKE '%[TearDown%'
                    OR {alias}.content ILIKE '%[OneTimeTearDown%'
                )
            )
        )
        """;
        }

        public static string TestFilePredicate(string chunkAlias, string projectAlias)
        {
            return $"""
        (
            {projectAlias}.name ~* '(^|[._-])(test|tests|unittests|integrationtests|functionaltests|acceptancetests|spec|specs)([._-]|$)'
            OR {projectAlias}.name ~* '(tests|specs)$'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/test/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/unittests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/unit.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/integrationtests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/integration.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/functionaltests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/functional.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/acceptancetests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/acceptance.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/spec/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/specs/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%.specs/%'
            OR replace({chunkAlias}.file_path, '\', '/') ~* '(tests|specs)\.cs$'
            OR EXISTS (
                SELECT 1
                FROM ai_chunks test_marker
                WHERE test_marker.project_id = {chunkAlias}.project_id
                  AND test_marker.file_path = {chunkAlias}.file_path
                  AND ({TestContentPredicate("test_marker")})
            )
        )
        """;
        }

        public static string IndexableFilePredicate(string chunkAlias, string projectAlias)
        {
            return $"""
        {NonEntityFrameworkMigrationFilePredicate(chunkAlias)}
        AND NOT ({TestFilePredicate(chunkAlias, projectAlias)})
        """;
        }
    }
}
