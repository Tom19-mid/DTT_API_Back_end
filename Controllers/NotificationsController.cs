using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    // ── GET /api/notifications (Lấy danh sách thông báo lọc theo UserId của Admin / Web đang đăng nhập) ──
    [HttpGet]
    public async Task<IActionResult> GetAllNotifications([FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? type, [FromQuery] string? userId)
    {
        try
        {
            /*
            // Code cũ lấy toàn bộ thông báo hệ thống không lọc theo user_id:
            var query = _context.Notifications.AsNoTracking().AsQueryable();
            */
            var query = _context.Notifications.AsNoTracking().AsQueryable();

            Guid filterUserId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(userId) && Guid.TryParse(userId, out var parsedQueryId))
            {
                filterUserId = parsedQueryId;
            }
            else
            {
                var claimIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("user_id")?.Value;
                if (!string.IsNullOrWhiteSpace(claimIdStr) && Guid.TryParse(claimIdStr, out var parsedClaimId))
                {
                    filterUserId = parsedClaimId;
                }
            }

            if (filterUserId != Guid.Empty)
            {
                query = query.Where(n => n.UserId == filterUserId);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(n => (n.Title != null && n.Title.ToLower().Contains(s)) || (n.Content != null && n.Content.ToLower().Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            {
                if (status.Equals("UNREAD", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(n => !n.IsRead);
                else if (status.Equals("READ", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(n => n.IsRead);
            }

            if (!string.IsNullOrWhiteSpace(type) && type != "ALL")
            {
                query = query.Where(n => n.Type == type);
            }

            var list = await query.OrderByDescending(n => n.CreatedAt).ToListAsync();

            var dtos = list.Select(n => new
            {
                notificationId = n.NotificationId,
                userId = n.UserId.ToString(),
                title = n.Title ?? "Thông báo",
                content = n.Content ?? "",
                type = n.Type ?? "system",
                relatedId = n.RelatedId,
                relatedType = n.RelatedType,
                isRead = n.IsRead,
                readAt = n.ReadAt.HasValue ? n.ReadAt.Value.ToString("o") : null,
                createdAt = n.CreatedAt.ToString("o")
            });

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách thông báo: " + ex.Message });
        }
    }

    // ── POST /api/notifications (Tạo mới thông báo từ Admin) ─────────────────
    [HttpPost]
    public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Content))
            {
                return BadRequest(new { success = false, message = "Tiêu đề và nội dung thông báo không được để trống." });
            }

            Guid targetUserId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(dto.UserId) && Guid.TryParse(dto.UserId, out var parsedGuid))
            {
                targetUserId = parsedGuid;
            }
            else
            {
                /*
                // Code cũ tự gán cho user đầu tiên trong CSDL:
                var defaultUser = await _context.Users.FirstOrDefaultAsync();
                if (defaultUser != null) targetUserId = defaultUser.UserId;
                */
                var claimIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("user_id")?.Value;
                if (!string.IsNullOrWhiteSpace(claimIdStr) && Guid.TryParse(claimIdStr, out var parsedAdminId))
                {
                    targetUserId = parsedAdminId;
                }
                else
                {
                    var defaultUser = await _context.Users.FirstOrDefaultAsync();
                    if (defaultUser != null) targetUserId = defaultUser.UserId;
                }
            }

            if (targetUserId == Guid.Empty)
            {
                return BadRequest(new { success = false, message = "Không tìm thấy người dùng hợp lệ để gửi thông báo." });
            }

            var notification = new Notification
            {
                UserId = targetUserId,
                Title = dto.Title.Trim(),
                Content = dto.Content.Trim(),
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "system" : dto.Type.Trim(),
                RelatedId = dto.RelatedId,
                RelatedType = dto.RelatedType,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Tạo thông báo thành công.",
                data = new
                {
                    notificationId = notification.NotificationId,
                    userId = notification.UserId.ToString(),
                    title = notification.Title,
                    content = notification.Content,
                    type = notification.Type,
                    relatedId = notification.RelatedId,
                    relatedType = notification.RelatedType,
                    isRead = notification.IsRead,
                    createdAt = notification.CreatedAt.ToString("o")
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi tạo thông báo: " + ex.Message });
        }
    }

    // ── PUT /api/notifications/read-all (Đánh dấu đã đọc tất cả thông báo) ───
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllNotificationsAsRead()
    {
        try
        {
            var unreadList = await _context.Notifications.Where(n => !n.IsRead).ToListAsync();
            DateTime now = DateTime.UtcNow;
            foreach (var noti in unreadList)
            {
                noti.IsRead = true;
                noti.ReadAt = now;
            }

            if (unreadList.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = $"Đã đánh dấu đã đọc {unreadList.Count} thông báo.", count = unreadList.Count });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi đánh dấu đã đọc tất cả: " + ex.Message });
        }
    }

    /*
    // ── DELETE /api/notifications/clear-read (Xóa tất cả thông báo đã đọc) ───
    [HttpDelete("clear-read")]
    public async Task<IActionResult> ClearReadNotifications()
    {
        try
        {
            var readList = await _context.Notifications.Where(n => n.IsRead).ToListAsync();
            if (readList.Count > 0)
            {
                _context.Notifications.RemoveRange(readList);
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = $"Đã xóa {readList.Count} thông báo đã đọc.", count = readList.Count });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi xóa thông báo đã đọc: " + ex.Message });
        }
    }
    */

    // ── DELETE /api/notifications/{id} (Xóa 1 thông báo theo ID) ─────────────
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteNotification(int id)
    {
        try
        {
            var noti = await _context.Notifications.FindAsync(id);
            if (noti == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy thông báo cần xóa." });
            }

            _context.Notifications.Remove(noti);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã xóa thông báo thành công." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi xóa thông báo: " + ex.Message });
        }
    }

    /*
    // =========================================================================
    // CODE GỐC BAN ĐẦU - GIỮ NGUYÊN BẰNG COMMENT THEO YÊU CẦU CỦA NGƯỜI DÙNG
    // =========================================================================
    // GET /api/notifications/patient/{patientId}
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientNotificationsOld(int patientId)
    {
        ...
    }
    */

    // ── GET /api/notifications/patient/{patientId} (Dành cho Mobile Patient App) ──
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientNotifications(int patientId)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, patientId)) return this.ForbidJson();
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
                var (icon, color, bgColor) = GetIconAndColors(n.Type, n.Title);
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

    // ── PUT /api/notifications/{id}/read (Đánh dấu 1 thông báo là đã đọc) ─────
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        try
        {
            var noti = await _context.Notifications.FindAsync(id);
            if (noti == null) return NotFound(new { success = false, message = "Không tìm thấy thông báo." });

            if (!AccessControl.CanAccessUserId(User, noti.UserId)) return this.ForbidJson();

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

    // ── PUT /api/notifications/patient/{patientId}/read-all (Dành cho Mobile Patient) ──
    [HttpPut("patient/{patientId}/read-all")]
    public async Task<IActionResult> MarkAllAsRead(int patientId)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, patientId)) return this.ForbidJson();
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

    private static (string Icon, string Color, string BgColor) GetIconAndColors(string? type, string? title)
    {
        string tStr = (title ?? "").ToLower();
        string typeStr = (type ?? "").ToLower();

        if (tStr.Contains("bác sĩ") || tStr.Contains("gọi khám") || tStr.Contains("mời vào"))
            return ("notifications", "#6366F1", "#EEF2FF"); // Indigo / Doctor Call icon

        if (tStr.Contains("sinh hiệu") || tStr.Contains("huyết áp") || tStr.Contains("mạch"))
            return ("fitness", "#8B5CF6", "#F5F3FF"); // Violet / Vitals icon

        if (tStr.Contains("hoàn tất khám") || tStr.Contains("lưu bệnh án") || tStr.Contains("hoàn thành"))
            return ("checkmark-done-circle", "#10B981", "#ECFDF5"); // Emerald / Completed icon

        if (tStr.Contains("xét nghiệm"))
            return ("flask", "#F59E0B", "#FFFBEB"); // Amber / Lab test icon

        if (tStr.Contains("siêu âm"))
            return ("scan", "#06B6D4", "#ECFEFF"); // Cyan / Ultrasound icon

        if (tStr.Contains("đơn thuốc") || tStr.Contains("thuốc"))
            return ("medkit", "#3B82F6", "#EFF6FF"); // Blue / Medicine icon

        if (tStr.Contains("hóa đơn") || tStr.Contains("thanh toán"))
            return ("receipt", "#10B981", "#ECFDF5"); // Emerald / Receipt icon

        if (tStr.Contains("tài khoản") || tStr.Contains("chào mừng") || tStr.Contains("xác thực"))
            return ("person-circle", "#3B82F6", "#EFF6FF"); // Blue / Account icon

        return typeStr switch
        {
            "appointment" => ("calendar", "#3B82F6", "#EFF6FF"),
            "result" => ("document-text", "#10B981", "#ECFDF5"),
            "promotion" => ("gift", "#F59E0B", "#FFFBEB"),
            _ => ("information-circle", "#6B7280", "#F3F4F6")
        };
    }

    private static string GetRelativeTime(DateTime time)
    {
        // Normalize to Vietnam Time (UTC+7). LƯU Ý: Npgsql luôn trả DateTime.Kind = Unspecified cho
        // cột "timestamp without time zone" (KHÔNG BAO GIỜ là Utc dù giá trị lưu thật sự là UTC, vì
        // toàn bộ CreatedAt trong hệ thống đều gán = DateTime.UtcNow) — điều kiện "time.Kind == Utc"
        // trước đây luôn sai, khiến notiTime KHÔNG BAO GIỜ được cộng +7h trong khi localNow thì có,
        // tạo ra lệch đúng 7 tiếng cho MỌI thông báo bất kể vừa tạo bao lâu.
        DateTime localNow = DateTime.UtcNow.AddHours(7);
        DateTime notiTime = time.AddHours(7);

        var span = localNow - notiTime;

        if (span.TotalMinutes < 1)
            return "Vừa xong";
        if (span.TotalMinutes < 60)
            return $"{(int)Math.Max(1, span.TotalMinutes)} phút trước";
        if (span.TotalHours < 12 && notiTime.Date == localNow.Date)
            return $"{(int)span.TotalHours} giờ trước";
        if (notiTime.Date == localNow.Date)
            return $"{notiTime:HH:mm} • Hôm nay";
        if (notiTime.Date == localNow.Date.AddDays(-1))
            return $"{notiTime:HH:mm} • Hôm qua";

        return $"{notiTime:HH:mm} • {notiTime:dd/MM/yyyy}";
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

public class CreateNotificationDto
{
    public string? UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Type { get; set; } = "system";
    public int? RelatedId { get; set; }
    public string? RelatedType { get; set; }
}
