namespace AiMemory.Models
{
    public sealed record CodeChunk(
        string ProjectName,
        string RootPath,
        string FilePath,
        string? Language,
        string ChunkType,
        string? SymbolName,
        string Content,
        string ContentHash
    );
}
