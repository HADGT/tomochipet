using qlthucung.Services.chat;
using System.Collections.Generic;

namespace qlthucung.Models.Chat
{
    public class ChatResultDto
    {
        public string ConversationId { get; set; } = null!;
        public string AssistantText { get; set; } = null!;
        public string Triage { get; set; } = null!;
        public double? Confidence { get; set; }
        public List<ProductSuggestion>? ProductSuggestions { get; set; }
    }
}
