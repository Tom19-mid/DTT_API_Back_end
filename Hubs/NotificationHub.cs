using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DTT_Backend_API.Hubs;

/// <summary>
/// Kênh real-time dùng chung cho thông báo (Admin → Bệnh nhân/Bác sĩ) và cập nhật trạng thái xác
/// thực tài khoản — trước đây toàn bộ hệ thống chỉ có REST polling (client phải tự bấm refresh/mở
/// lại màn hình mới thấy thông báo mới), đúng như QA report "Ko có hiện thông báo real-time" và
/// "Tài khoản đã xác thực hoặc từ chối ko có hiện real-time".
/// Mỗi kết nối tự join 2 group: "user:{userId}" (nhắm đúng 1 tài khoản) và "role:{roleId}" (nhắm cả
/// một nhóm vai trò, vd toàn bộ Bác sĩ) — controller chỉ cần gửi tới đúng group cần thiết.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var roleId = Context.User?.FindFirst("role_id")?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        if (!string.IsNullOrWhiteSpace(roleId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{roleId}");

        await base.OnConnectedAsync();
    }
}
