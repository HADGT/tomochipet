using System.Collections.Generic;

namespace qlthucung.Models.Chat
{
    public class HybridQuery
    {
        public string RawText { get; set; }
        public List<string> Keywords { get; set; } = new();
        public Dictionary<string, double> Vector { get; set; } = new();
        public float[] Embedding { get; set; } = null;
    }
}
