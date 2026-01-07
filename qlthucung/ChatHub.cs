using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;
using qlthucung.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace qlthucung
{
    public class ChatHub : Hub
    {
        private static HashSet<string> connectedUsers = new();
        private readonly AppDbContext _context;

        public ChatHub(AppDbContext context)
        {
            _context = context;
        }
        public async Task SendMessage(string senderId, string receiverId, string message)
        {
            try
            {
                Console.WriteLine($"[SendMessage] senderId: {senderId}, receiverId: {receiverId}, message: {message}");
                var msg = new Message
                {
                    SenderId = senderId,
                    ReceiverId = receiverId,
                    Content = message,
                    SentAt = DateTime.Now,
                    IsRead = false
                };

                _context.Messages.Add(msg);
                await _context.SaveChangesAsync();
                // Nếu là người gửi mới → thông báo cho admin
                if (!connectedUsers.Contains(senderId))
                {
                    connectedUsers.Add(senderId);
                    await Clients.User("admin").SendAsync("NewUserConnected", senderId);
                }

                // Gửi cho người nhận
                await Clients.User(receiverId).SendAsync("ReceiveMessage", senderId, message);

                // (Tuỳ chọn) Gửi echo lại cho người gửi
                await Clients.User(senderId).SendAsync("ReceiveMessage", senderId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SendMessage ERROR] {ex.Message}");
                throw; // Để SignalR gửi lỗi cho client
            }
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
                // Nếu là admin → add vào group Admins
                if (userId == "admin") // hoặc check role từ DB
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
                }
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                connectedUsers.Remove(userId);
            }
            await base.OnDisconnectedAsync(exception);
        }


        public async Task LoadMessages(string userId, string otherUserId)
        {
            var messages = await _context.Messages
                .Where(m => (m.SenderId == userId && m.ReceiverId == otherUserId)
                         || (m.SenderId == otherUserId && m.ReceiverId == userId))
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            await Clients.User(userId).SendAsync("LoadMessageHistory", messages);
        }

    }
}
