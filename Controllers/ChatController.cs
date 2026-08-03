using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.DTOs;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;
using DTT_Backend_API.Services;

namespace DTT_Backend_API.Controllers;

// AI Symptom Checker + Escalate to Staff — xem Tai Lieu/ai_chatbot_luong_nghiep_vu.md
// và Tai Lieu/ai_chatbot_roadmap.md cho toàn bộ luồng nghiệp vụ/thiết kế API.
[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly GeminiService _gemini;

    public ChatController(AppDbContext context, GeminiService gemini)
    {
        _context = context;
        _gemini = gemini;
    }

    private static string GetErrorDetail(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value
                 ?? User.FindFirst("userId")?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    // Chỉ bệnh nhân (role_id=3) mới được tạo/nhắn/escalate phiên chat của chính họ.
    private async Task<Patient?> GetCurrentPatientAsync()
    {
        if (User.FindFirst("role_id")?.Value != "3") return null;
        var userId = GetCurrentUserId();
        if (userId == null) return null;
        return await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId.Value);
    }

    // Tên hiển thị của 1 nhân viên (Lễ tân...) cho bệnh nhân thấy trong chat — mirror đúng logic
    // AuthController.DoctorLogin dùng để tính FullName lúc đăng nhập, để tên hiển thị nhất quán giữa
    // WinForms và chat bệnh nhân. Ưu tiên users.full_name (xem add_staff_full_names.sql — cột dành
    // riêng cho các role không có hồ sơ Doctors như Lễ tân/Điều dưỡng/KTV/Dược sĩ), rồi tới
    // Doctors.FullName, cuối cùng mới fallback "TênVaiTrò + 4 số cuối SĐT".
    private async Task<string> ResolveStaffNameAsync(Guid userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user != null && !string.IsNullOrWhiteSpace(user.FullName)) return user.FullName;

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
        if (doctor != null && !string.IsNullOrWhiteSpace(doctor.FullName)) return doctor.FullName;

        if (user == null) return "Lễ tân";

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == user.RoleId);
        string roleName = role?.RoleName ?? "Lễ tân";
        string last4 = user.PhoneNumber.Length > 4 ? user.PhoneNumber.Substring(user.PhoneNumber.Length - 4) : "";
        return $"{roleName} {last4}".Trim();
    }

    // POST /api/chat/sessions — Bệnh nhân mở phiên chat mới, status='AI'
    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession()
    {
        try
        {
            var patient = await GetCurrentPatientAsync();
            if (patient == null)
                return StatusCode(403, new { success = false, message = "Chỉ bệnh nhân mới có thể sử dụng tính năng chat." });

            var session = new ChatSession
            {
                PatientId = patient.PatientId,
                Status = "AI",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.ChatSessions.Add(session);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, sessionId = session.SessionId, status = session.Status });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // GET /api/chat/sessions/active — Bệnh nhân mở lại màn Chat: tìm phiên đang mở (AI/Escalated)
    // gần nhất để TIẾP TỤC thay vì luôn tạo phiên mới. Trước đây mobile luôn gọi CreateSession mỗi
    // lần vào màn hình, nên nếu bệnh nhân thoát ra rồi mở lại giữa lúc đang chờ Lễ tân trả lời, phiên
    // cũ (lễ tân vẫn đang đợi) bị "mồ côi" — bệnh nhân nhảy sang 1 session hoàn toàn khác không ai biết.
    [HttpGet("sessions/active")]
    public async Task<IActionResult> GetActiveSession()
    {
        try
        {
            var patient = await GetCurrentPatientAsync();
            if (patient == null)
                return StatusCode(403, new { success = false, message = "Chỉ bệnh nhân mới có thể sử dụng tính năng chat." });

            var session = await _context.ChatSessions
                .Where(s => s.PatientId == patient.PatientId && s.Status != "Closed")
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync();

            if (session == null) return Ok(new { success = true, hasActive = false });

            return Ok(new { success = true, hasActive = true, sessionId = session.SessionId, status = session.Status });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // POST /api/chat/sessions/{id}/messages — Bệnh nhân gửi tin nhắn.
    // status='AI': lưu tin nhắn + gọi Gemini + lưu phản hồi AI + cập nhật suggested_specialty_id.
    // status='Escalated': chỉ lưu tin nhắn (Lễ tân xem qua polling), KHÔNG gọi Gemini.
    [HttpPost("sessions/{id}/messages")]
    public async Task<IActionResult> SendPatientMessage(int id, [FromBody] SendChatMessageDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Content))
                return BadRequest(new { success = false, message = "Nội dung tin nhắn không được để trống." });

            var patient = await GetCurrentPatientAsync();
            if (patient == null)
                return StatusCode(403, new { success = false, message = "Chỉ bệnh nhân mới có thể sử dụng tính năng chat." });

            var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == id);
            if (session == null) return NotFound(new { success = false, message = "Không tìm thấy phiên chat." });
            if (session.PatientId != patient.PatientId)
                return StatusCode(403, new { success = false, message = "Bạn không có quyền truy cập phiên chat này." });
            if (session.Status == "Closed")
                return BadRequest(new { success = false, message = "Phiên chat đã đóng." });

            var userId = GetCurrentUserId();
            _context.ChatMessages.Add(new ChatMessage
            {
                SessionId = id,
                SenderType = "Patient",
                SenderUserId = userId,
                Content = dto.Content.Trim(),
                CreatedAt = DateTime.UtcNow
            });
            session.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Khi đã Escalated, Lễ tân trả lời trực tiếp — không còn AI xen vào cuộc trò chuyện nữa.
            if (session.Status == "Escalated")
                return Ok(new { success = true, escalated = true });

            var history = await _context.ChatMessages
                .Where(m => m.SessionId == id)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
            var specialtyNames = await _context.Specialties.Where(s => s.Status).Select(s => s.SpecialtyName).ToListAsync();

            var result = await _gemini.GetReplyAsync(history, specialtyNames, HttpContext.RequestAborted);

            int? suggestedSpecialtyId = null;
            string? suggestedSpecialtyName = null;
            if (!string.IsNullOrWhiteSpace(result.SuggestedSpecialtyName))
            {
                var matched = await MatchSpecialtyAsync(result.SuggestedSpecialtyName);
                if (matched != null)
                {
                    suggestedSpecialtyId = matched.SpecialtyId;
                    suggestedSpecialtyName = matched.SpecialtyName;
                    session.SuggestedSpecialtyId = matched.SpecialtyId;
                }
            }

            _context.ChatMessages.Add(new ChatMessage
            {
                SessionId = id,
                SenderType = "AI",
                SenderUserId = null,
                Content = result.Reply,
                CreatedAt = DateTime.UtcNow
            });
            session.UpdatedAt = DateTime.UtcNow;

            // AI phát hiện bệnh nhân muốn kết thúc (vd: "kết thúc", "tạm biệt"...) → tự đóng phiên,
            // không cần chờ thao tác gì thêm từ bệnh nhân. Chỉ áp dụng khi còn ở pha AI — phiên đã
            // Escalated được lễ tân đóng thủ công qua /sessions/{id}/close, không tự động ở đây.
            if (result.ShouldClose)
            {
                session.Status = "Closed";
                session.ClosedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                reply = result.Reply,
                suggestedSpecialtyId,
                suggestedSpecialtyName,
                shouldEscalate = result.ShouldEscalate,
                status = session.Status
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // Khớp tên chuyên khoa Gemini trả về với bảng specialties thật — ưu tiên khớp đúng tuyệt đối
    // (không phân biệt hoa/thường), rồi mới tới khớp gần đúng — tránh AI tự "sáng tác" chuyên khoa
    // không tồn tại trong hệ thống.
    private async Task<Specialty?> MatchSpecialtyAsync(string name)
    {
        var trimmed = name.Trim();
        var all = await _context.Specialties.Where(s => s.Status).ToListAsync();
        var exact = all.FirstOrDefault(s => string.Equals(s.SpecialtyName, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        return all.FirstOrDefault(s =>
            s.SpecialtyName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(s.SpecialtyName, StringComparison.OrdinalIgnoreCase));
    }

    // GET /api/chat/sessions/{id}/messages — Bệnh nhân/Lễ tân dùng để lấy lịch sử + polling.
    [HttpGet("sessions/{id}/messages")]
    public async Task<IActionResult> GetMessages(int id)
    {
        try
        {
            var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == id);
            if (session == null) return NotFound(new { success = false, message = "Không tìm thấy phiên chat." });

            if (!await AccessControl.CanAccessPatientAsync(User, _context, session.PatientId))
                return StatusCode(403, new { success = false, message = "Bạn không có quyền truy cập phiên chat này." });

            var rawMessages = await _context.ChatMessages
                .Where(m => m.SessionId == id)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            // Tra tên thật cho từng lễ tân xuất hiện trong lịch sử (thường chỉ 1 người/phiên, nhưng
            // tra theo distinct id để đúng cả khi phiên từng đổi người phụ trách).
            var staffIds = rawMessages
                .Where(m => m.SenderType == "Staff" && m.SenderUserId.HasValue)
                .Select(m => m.SenderUserId!.Value)
                .Distinct()
                .ToList();
            var staffNames = new Dictionary<Guid, string>();
            foreach (var sid in staffIds) staffNames[sid] = await ResolveStaffNameAsync(sid);

            var messages = rawMessages.Select(m => new ChatMessageDto
            {
                MessageId = m.MessageId,
                SenderType = m.SenderType,
                SenderUserId = m.SenderUserId,
                SenderName = m.SenderType == "Staff" && m.SenderUserId.HasValue && staffNames.TryGetValue(m.SenderUserId.Value, out var n) ? n : null,
                Content = m.Content,
                CreatedAt = m.CreatedAt
            }).ToList();

            return Ok(new { success = true, status = session.Status, messages });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // POST /api/chat/sessions/{id}/escalate — Bệnh nhân bấm "Cần tư vấn thêm" (hoặc theo gợi ý AI).
    [HttpPost("sessions/{id}/escalate")]
    public async Task<IActionResult> Escalate(int id, [FromBody] EscalateChatDto? dto)
    {
        try
        {
            var patient = await GetCurrentPatientAsync();
            if (patient == null)
                return StatusCode(403, new { success = false, message = "Chỉ bệnh nhân mới có thể sử dụng tính năng chat." });

            var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == id);
            if (session == null) return NotFound(new { success = false, message = "Không tìm thấy phiên chat." });
            if (session.PatientId != patient.PatientId)
                return StatusCode(403, new { success = false, message = "Bạn không có quyền truy cập phiên chat này." });
            if (session.Status == "Closed")
                return BadRequest(new { success = false, message = "Phiên chat đã đóng, không thể chuyển tiếp." });

            // Bấm nhiều lần khi đã Escalated: coi như thành công, không tạo thêm Notification trùng lặp.
            if (session.Status == "Escalated")
                return Ok(new { success = true, status = session.Status });

            session.Status = "Escalated";
            session.UpdatedAt = DateTime.UtcNow;

            var receptionists = await _context.Users
                .Where(u => u.RoleId == 4 && u.Status == "Active")
                .ToListAsync();
            foreach (var staff in receptionists)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = staff.UserId,
                    Title = "💬 Bệnh Nhân Cần Hỗ Trợ Tư Vấn",
                    Content = $"{patient.FullName ?? "Một bệnh nhân"} vừa yêu cầu tư vấn thêm qua Chat AI. Vui lòng vào mục Chat Hỗ Trợ để tiếp nhận.",
                    Type = "chat_escalate",
                    RelatedId = session.SessionId,
                    RelatedType = "chat_session",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, status = session.Status });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // GET /api/chat/staff/queue — Lễ tân xem danh sách phiên đang chờ tiếp nhận.
    [HttpGet("staff/queue")]
    [StaffOnly]
    public async Task<IActionResult> GetStaffQueue()
    {
        try
        {
            var pending = await _context.ChatSessions
                .Where(s => s.Status == "Escalated" && s.AssignedStaffId == null)
                .OrderBy(s => s.UpdatedAt)
                .ToListAsync();

            return Ok(new { success = true, items = await BuildQueueItemsAsync(pending) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // GET /api/chat/staff/my-sessions — Các phiên lễ tân hiện tại ĐÃ tiếp nhận nhưng chưa đóng.
    // Cần thiết để lễ tân tìm lại phiên đang dở dang sau khi tắt/mở lại app WinForms — trước đây
    // sau khi claim, phiên biến mất khỏi /staff/queue (đúng, vì đã có người nhận) nhưng không có
    // chỗ nào khác để tìm lại, khiến phiên bị "kẹt" không ai trả lời được nữa dù bệnh nhân vẫn chờ.
    [HttpGet("staff/my-sessions")]
    [StaffOnly]
    public async Task<IActionResult> GetMySessions()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return StatusCode(403, new { success = false, message = "Không xác định được người dùng." });

            var mine = await _context.ChatSessions
                .Where(s => s.Status == "Escalated" && s.AssignedStaffId == userId.Value)
                .OrderBy(s => s.UpdatedAt)
                .ToListAsync();

            return Ok(new { success = true, items = await BuildQueueItemsAsync(mine) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    private async Task<List<ChatQueueItemDto>> BuildQueueItemsAsync(List<ChatSession> sessions)
    {
        var patientMap = await _context.Patients.ToDictionaryAsync(p => p.PatientId, p => p);
        var specialtyMap = await _context.Specialties.ToDictionaryAsync(s => s.SpecialtyId, s => s.SpecialtyName);

        var items = new List<ChatQueueItemDto>();
        foreach (var s in sessions)
        {
            var lastMessage = await _context.ChatMessages
                .Where(m => m.SessionId == s.SessionId)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            patientMap.TryGetValue(s.PatientId, out var patient);
            items.Add(new ChatQueueItemDto
            {
                SessionId = s.SessionId,
                PatientId = s.PatientId,
                PatientName = patient?.FullName ?? "Bệnh nhân",
                SuggestedSpecialtyName = s.SuggestedSpecialtyId.HasValue && specialtyMap.TryGetValue(s.SuggestedSpecialtyId.Value, out var specName) ? specName : null,
                LastMessagePreview = lastMessage?.Content,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            });
        }
        return items;
    }

    // POST /api/chat/staff/sessions/{id}/claim — Lễ tân tiếp nhận phiên.
    // Cập nhật có điều kiện (assigned_staff_id IS NULL) để tránh 2 lễ tân cùng nhận 1 phiên.
    [HttpPost("staff/sessions/{id}/claim")]
    [StaffOnly]
    public async Task<IActionResult> ClaimSession(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return StatusCode(403, new { success = false, message = "Không xác định được người dùng." });

            var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == id);
            if (session == null) return NotFound(new { success = false, message = "Không tìm thấy phiên chat." });
            if (session.Status != "Escalated")
                return BadRequest(new { success = false, message = "Phiên không ở trạng thái chờ tiếp nhận." });
            if (session.AssignedStaffId != null && session.AssignedStaffId != userId.Value)
                return Conflict(new { success = false, message = "Phiên đã được tiếp nhận bởi nhân viên khác." });

            // Idempotent: nếu CHÍNH lễ tân này đã tiếp nhận từ trước (vd: đóng cửa sổ/app rồi mở lại
            // để tiếp tục), coi như thành công luôn — KHÔNG gửi lại câu chào tự động (tránh spam lặp
            // lại mỗi lần họ mở lại dialog cho cùng 1 phiên).
            bool isFreshClaim = session.AssignedStaffId == null;
            var staffName = await ResolveStaffNameAsync(userId.Value);

            if (isFreshClaim)
            {
                session.AssignedStaffId = userId.Value;
                session.UpdatedAt = DateTime.UtcNow;

                // Tự động chào bệnh nhân kèm tên thật của lễ tân ngay khi tiếp nhận — lễ tân không
                // phải gõ tay câu chào mỗi lần, bệnh nhân biết ngay ai đang hỗ trợ thay vì chờ im lặng.
                _context.ChatMessages.Add(new ChatMessage
                {
                    SessionId = id,
                    SenderType = "Staff",
                    SenderUserId = userId.Value,
                    Content = $"Xin chào, tôi là {staffName} — Lễ tân sẽ hỗ trợ bạn. Bạn vui lòng cho biết thêm để mình tư vấn nhé!",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, assignedStaffId = session.AssignedStaffId, staffName });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // POST /api/chat/staff/sessions/{id}/messages — Lễ tân đã tiếp nhận trả lời trực tiếp.
    [HttpPost("staff/sessions/{id}/messages")]
    [StaffOnly]
    public async Task<IActionResult> SendStaffMessage(int id, [FromBody] SendChatMessageDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Content))
                return BadRequest(new { success = false, message = "Nội dung tin nhắn không được để trống." });

            var userId = GetCurrentUserId();
            if (userId == null) return StatusCode(403, new { success = false, message = "Không xác định được người dùng." });

            var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == id);
            if (session == null) return NotFound(new { success = false, message = "Không tìm thấy phiên chat." });
            if (session.Status != "Escalated")
                return BadRequest(new { success = false, message = "Phiên không ở trạng thái đang tư vấn trực tiếp." });
            if (session.AssignedStaffId != userId.Value)
                return StatusCode(403, new { success = false, message = "Bạn chưa tiếp nhận phiên chat này." });

            _context.ChatMessages.Add(new ChatMessage
            {
                SessionId = id,
                SenderType = "Staff",
                SenderUserId = userId.Value,
                Content = dto.Content.Trim(),
                CreatedAt = DateTime.UtcNow
            });
            session.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // POST /api/chat/sessions/{id}/close — Lễ tân đóng phiên sau khi tư vấn xong.
    [HttpPost("sessions/{id}/close")]
    [StaffOnly]
    public async Task<IActionResult> CloseSession(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return StatusCode(403, new { success = false, message = "Không xác định được người dùng." });

            var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == id);
            if (session == null) return NotFound(new { success = false, message = "Không tìm thấy phiên chat." });
            if (session.Status != "Escalated")
                return BadRequest(new { success = false, message = "Chỉ có thể đóng phiên đang ở trạng thái tư vấn trực tiếp." });
            if (session.AssignedStaffId != userId.Value)
                return StatusCode(403, new { success = false, message = "Bạn chưa tiếp nhận phiên chat này." });

            session.Status = "Closed";
            session.ClosedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, status = session.Status });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }
}
