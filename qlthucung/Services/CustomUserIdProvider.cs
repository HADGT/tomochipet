using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

public class CustomUserIdProvider : IUserIdProvider
{
    public string GetUserId(HubConnectionContext connection)
    {
        // Đầu tiên thử lấy từ user login
        var userId = connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(userId))
            return userId;

        // Nếu không có, thử lấy từ query string
        var httpContext = connection.GetHttpContext();
        return httpContext?.Request.Query["userId"];
    }
}
