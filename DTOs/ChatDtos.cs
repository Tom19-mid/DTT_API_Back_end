namespace DTT_Backend_API.DTOs;

// AI Symptom Checker + Escalate to Staff — xem Tai Lieu/ai_chatbot_luong_nghiep_vu.md

public class SendChatMessageDto
{
    public string Content { get; set; } = string.Empty;
}

public class EscalateChatDto
{
    // Optional: lý do bệnh nhân bấm "Cần tư vấn thêm" (hiện tại chỉ để log, không bắt buộc)
    public string? Reason { get; set; }
}

public class ChatMessageDto
{
    public int MessageId { get; set; }
    public string SenderType { get; set; } = string.Empty; // Patient | AI | Staff
    public Guid? SenderUserId { get; set; }
    // Chỉ có giá trị khi SenderType='Staff' — tên thật của lễ tân, để bệnh nhân biết đang chat với ai
    // thay vì chỉ thấy nhãn "Lễ tân" chung chung.
    public string? SenderName { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ChatSessionDto
{
    public int SessionId { get; set; }
    public int PatientId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? SuggestedSpecialtyId { get; set; }
    public string? SuggestedSpecialtyName { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Danh sách hàng chờ cho Lễ tân (GET /api/chat/staff/queue) — kèm thông tin bệnh nhân
// để hiển thị trực tiếp trên WinForms mà không cần gọi thêm API.
public class ChatQueueItemDto
{
    public int SessionId { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? SuggestedSpecialtyName { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
