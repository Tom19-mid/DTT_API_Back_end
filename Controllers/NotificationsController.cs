using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/notifications/patient/{patientId}
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientNotifications(int patientId)
    {
        try
        {
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == patientId);
            Guid targetUserId = patient?.UserId ?? Guid.Empty;

            var list = await _context.Notifications
                .Where(n => n.UserId == targetUserId || targetUserId == Guid.Empty)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            // Auto-seed welcome & sample notifications if empty so app UI looks great & alive
            if (list.Count == 0 && patient != null)
            {
                list = new List<Notification>
                {
                    new Notification
                    {
                        UserId = patient.UserId,
                        Title = "Chào mừng bạn đến với DTT Healthcare",
                        Content = "Cảm ơn bạn đã tin tưởng và sử dụng ứng dụng chăm sóc sức khỏe. Chúc bạn một ngày nhiều năng lượng!",
                        Type = "system",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow.AddHours(-24)
                    },
                    new Notification
                    {
                        UserId = patient.UserId,
                        Title = "Hoàn thiện hồ sơ y tế",
                        Content = "Vui lòng cập nhật thông tin BHYT và CCCD trong phần Hồ sơ để quá trình đăng ký khám tại bệnh viện diễn ra nhanh chóng nhất.",
                        Type = "system",
                        IsRead = true,
                        ReadAt = DateTime.UtcNow.AddHours(-12),
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    new Notification
                    {
                        UserId = patient.UserId,
                        Title = "Ưu đãi 20% các Gói Khám Tầm Soát",
                        Content = "DTT Healthcare ưu đãi 20% chi phí các gói khám sức khỏe tổng quát và tầm soát ung thư. Hãy đặt lịch ngay hôm nay!",
                        Type = "promotion",
                        IsRead = true,
                        ReadAt = DateTime.UtcNow.AddDays(-1),
                        CreatedAt = DateTime.UtcNow.AddDays(-3)
                    }
                };

                _context.Notifications.AddRange(list);
                await _context.SaveChangesAsync();
            }

            var dtos = list.Select(n =>
            {
                var (icon, color, bgColor) = GetIconAndColors(n.Type);
                return new NotificationDto
                {
                    Id = n.NotificationId.ToString(),
                    Type = n.Type ?? "system",
                    Title = n.Title ?? "Thông báo",
                    Message = n.Content ?? "",
                    Time = GetRelativeTime(n.CreatedAt),
                    Read = n.IsRead,
                    Icon = icon,
                    Color = color,
                    BgColor = bgColor
                };
            });

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi tải thông báo: " + ex.Message });
        }
    }

    // PUT /api/notifications/{id}/read
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        try
        {
            var noti = await _context.Notifications.FindAsync(id);
            if (noti == null) return NotFound(new { success = false, message = "Không tìm thấy thông báo." });

            if (!noti.IsRead)
            {
                noti.IsRead = true;
                noti.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = "Đã đánh dấu đã đọc." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // PUT /api/notifications/patient/{patientId}/read-all
    [HttpPut("patient/{patientId}/read-all")]
    public async Task<IActionResult> MarkAllAsRead(int patientId)
    {
        try
        {
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == patientId);
            Guid targetUserId = patient?.UserId ?? Guid.Empty;

            var unread = await _context.Notifications
                .Where(n => (n.UserId == targetUserId || targetUserId == Guid.Empty) && !n.IsRead)
                .ToListAsync();

            foreach (var item in unread)
            {
                item.IsRead = true;
                item.ReadAt = DateTime.UtcNow;
            }

            if (unread.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, count = unread.Count });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private static (string Icon, string Color, string BgColor) GetIconAndColors(string? type)
    {
        return (type?.ToLower()) switch
        {
            "appointment" => ("calendar", "#3B82F6", "#EFF6FF"),
            "result" => ("flask", "#10B981", "#ECFDF5"),
            "promotion" => ("gift", "#F59E0B", "#FFFBEB"),
            _ => ("information-circle", "#6B7280", "#F3F4F6")
        };
    }

    private static string GetRelativeTime(DateTime time)
    {
        var span = DateTime.UtcNow - time.ToUniversalTime();
        if (span.TotalMinutes < 60)
            return $"{Math.Max(1, (int)span.TotalMinutes)} phút trước";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours} giờ trước";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays} ngày trước";
        return time.ToString("dd/MM/yyyy");
    }
}

public class NotificationDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "system";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public bool Read { get; set; }
    public string Icon { get; set; } = "information-circle";
    public string Color { get; set; } = "#6B7280";
    public string BgColor { get; set; } = "#F3F4F6";
}
