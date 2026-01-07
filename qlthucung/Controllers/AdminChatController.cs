using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using qlthucung.Models;
using qlthucung.Security;
using System.Threading.Tasks;
using System;
using qlthucung.Services.chat;
using System.Linq;
using System.Collections.Generic;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using GenerativeAI;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace qlthucung.Controllers
{
    public class AdminChatController : Controller
    {
        private readonly UserManager<AppIdentityUser> _userManager;
        private readonly AppDbContext _context;
        private readonly IChatService _chatService;
        private readonly RoleManager<AppIdentityRole> _roleManager;
        

        public AdminChatController(UserManager<AppIdentityUser> userManager, AppDbContext context, IChatService chatService, RoleManager<AppIdentityRole> roleManager)
        {
            _userManager = userManager;
            _context = context;
            _chatService = chatService;
            _roleManager = roleManager;
            
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "User")]
        public IActionResult UserIndex()
        {
            return View();
        }

        [HttpGet("get-messages")]
        public async Task<IActionResult> GetMessages(string userId)
        {
            var messages = await _chatService.GetMessagesAsync(userId);
            return Ok(messages);
        }

        [HttpGet]
        public async Task<IActionResult> GetUserList()
        {
            var senderIds = _context.Messages.Select(m => m.SenderId);
            var receiverIds = _context.Messages.Select(m => m.ReceiverId);

            var userIdsInMessages = senderIds
                .Union(receiverIds)
                .Distinct()
                .ToList();

            var users = _userManager.Users
                .Where(u => userIdsInMessages.Contains(u.Id))
                .ToList();

            var result = new List<object>();

            foreach (var user in users)
            {
                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

                if (!isAdmin)
                {
                    result.Add(new { user.Id, user.UserName });
                }
            }

            return Ok(result);
        }

    }
}
