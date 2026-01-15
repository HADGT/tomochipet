using qlthucung.Models;
using qlthucung.Models.Chat;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace qlthucung.Services.chat
{
    public interface IChatService
    {
        Task<List<Message>> GetMessagesAsync(string userId);
        Task<string> GetPetAdviceAsync(ChatQuery query);
        Task<string> AskAsync(string query);
        Task<List<Product>> GetProductsFallback(HybridQuery queryVector, string petType);
        Task TrainContentIndexAsync();
    }
}
