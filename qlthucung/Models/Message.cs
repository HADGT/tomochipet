using System;

namespace qlthucung.Models
{
    public class Message
    {
        public int Id { get; set; }
        public string SenderId { get; set; }   // ID người gửi
        public string ReceiverId { get; set; } // ID người nhận (Admin/User)
        public string Content { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
    }
}
