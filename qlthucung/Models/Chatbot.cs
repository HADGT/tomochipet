using System.Collections.Generic;

namespace qlthucung.Models
{
    public class Chatbot
    {

        public class ChatRequest
        {
            public string Message { get; set; }
        }

        public class ChatResponse
        {
            public string Response { get; set; } = string.Empty;
            public string? Intent { get; set; }
            public double? Confidence { get; set; }
            public Dictionary<string, object>? HotelInfo { get; set; }
            public Dictionary<string, object>? BookingInfo { get; set; }
            public string? ExtractedRequirements { get; set; }
        };

        public class IntentResult
        {
            public string Intent { get; set; } = "";
            public List<string> SearchTerms { get; set; } = new();
            public HealthAnalysis HealthAnalysis { get; set; }
        }

        public class HealthAnalysis
        {
            public List<string> Symptoms { get; set; } = new();
            public List<string> Measures { get; set; } = new();
            public List<string> NeededProducts { get; set; } = new();
            public List<string> NeededServices { get; set; } = new();
        }
    }
}
