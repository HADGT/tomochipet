using System.Threading.Tasks;

namespace qlthucung.Services.chat
{
    // Implement this interface to provide dense embeddings (optional).
    public interface IEmbeddingService
    {
        // Returns embedding vector (float[]) or throws on unrecoverable errors.
        Task<float[]> GetEmbeddingAsync(string text);
    }
}