using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;
using qlthucung.Models.Chat;
using qlthucung.Services.chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static qlthucung.Models.Chatbot;

namespace qlthucung.Controllers
{
    public class PetAIController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly AppDbContext _context;
        public PetAIController(IChatService chatService, AppDbContext context)
        {
            _chatService = chatService;
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> QueryAI([FromBody] ChatQuery query)
        {
            if (query.Category == "product" || query.Category == "health")
            {
                // 1. Nếu chưa chọn petType → yêu cầu chọn
                if (string.IsNullOrEmpty(query.PetType))
                {
                    var petTypes = await _context.DanhMucs
                        .Where(dm => dm.ParentID == "Thú cưng")
                        .Select(dm => dm.Tendanhmuc)
                        .ToListAsync();

                    return Ok(new
                    {
                        message = "Vui lòng chọn loại thú cưng:",
                        petTypes
                    });
                }
                
            }

            // Gọi AI để phân tích câu hỏi và trả về keyword
            var reply = await _chatService.GetPetAdviceAsync(query);

            return Ok(new { ai = reply });
        }

        // Lấy danh sách thú cưng
        [HttpGet]
        public async Task<IActionResult> GetPetTypes()
        {
            List<string> types = await _context.DanhMucs
                .Where(dm => dm.ParentID.Trim().ToLower() == "thú cưng")
                .Select(dm => dm.Tendanhmuc)
                .ToListAsync();

            return Ok(types.Select(x => new { name = x }).ToList());
        }

        // Lấy danh sách category
        [HttpGet]
        public IActionResult StartChat()
        {
            return Ok(new
            {
                message = "Chào bạn! Bạn muốn được giúp gì hôm nay?",
                options = new[]
                {
                new { key = "product", label = "Tìm sản phẩm cho thú cưng" },
                new { key = "health", label = "Tư vấn bệnh/thú cưng" },
                new { key = "care", label = "Chăm sóc & mẹo nuôi" }
            }
            });
        }
    }
}
