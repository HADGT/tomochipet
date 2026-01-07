using System.Collections.Generic;

namespace qlthucung.Models.Chat
{
    public class HybridQuery
    {
        // Chuỗi gốc người dùng nhập
        public string RawText { get; set; } = "";

        // Danh sách từ khóa (keyword search)
        public List<string> Keywords { get; set; } = new();

        // Vector dùng cho semantic search
        public Dictionary<string, double> Vector { get; set; } = new();
    }
}
