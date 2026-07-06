using System.Text.Json.Serialization;

namespace AiMemory.Configuration
{
    public sealed class PatternsConfig
    {
        public RulePatterns Rules { get; set; } = new();
        public KnowledgePatterns Knowledge { get; set; } = new();

        public static PatternsConfig Default { get; } = new()
        {
            Rules = new RulePatterns
            {
                ContentPatterns = ["throw new", "BusinessException", "ValidationException", "não pode", "nao pode", "deve", "obrigatório", "obrigatorio", "inválido", "invalido", "ErroContext", "ErrosContext", "ErroBase", "AdicionarErro", "AdicionaErro", "AddErro", "AddError", "AddFailure", "AddNotification", "Notificar", "Notification", "TemErro", "HasError", "IsValid", "Validate", "Validator", "RuleFor", "Elegivel", "Elegível", "Permite", "Bloqueado", "Cancelado", "Vencido"]
            },
            Knowledge = new KnowledgePatterns
            {
                FilePathPatterns = ["%.csproj", "%Program.cs", "%Startup.cs", "%appsettings%"],
                ContentPatterns = ["HttpClient", "MassTransit", "RabbitMQ", "Kafka", "MediatR", "EntityFramework", "TODO", "FIXME", "HACK"]
            }
        };
    }

    public sealed class RulePatterns
    {
        public List<string> ContentPatterns { get; set; } = [];
    }

    public sealed class KnowledgePatterns
    {
        public List<string> FilePathPatterns { get; set; } = [];
        public List<string> ContentPatterns { get; set; } = [];
    }
}