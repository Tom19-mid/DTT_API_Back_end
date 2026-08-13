namespace DTT_Backend_API.DTOs;

public class CreateAppointmentDto
{
    public int PatientId { get; set; }
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string TimeSlot { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Fee { get; set; }
}

public class UpdateAppointmentDto
{
    public int PatientId { get; set; }
    public int DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public string? SpecialtyName { get; set; }
    public string? Date { get; set; }
    public string? TimeSlot { get; set; }
    public string? Reason { get; set; }
    public string? Fee { get; set; }
    public int StatusId { get; set; }
    public string? CancelReason { get; set; }
    public string? CancelTime { get; set; }
    public string? CancelledBy { get; set; }
    public string? Note { get; set; }
    public string? Notes { get; set; }
    public string? NurseNote { get; set; }
}

public class AppointmentResponseDto
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public int PatientAge { get; set; }
    public string? Reason { get; set; }
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string TimeSlot { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
    /// <summary>Trạng thái thanh toán THẬT lấy từ bảng invoices ("paid" | "pending" | "unpaid") — không suy ra từ appointment.status_id.</summary>
    public string PaymentStatus { get; set; } = "unpaid";
    public int QueueNumber { get; set; }
    public string ClinicRoom { get; set; } = "Phòng 101";
    public string Fee { get; set; } = "250.000đ";
    public bool IsPackage { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? Note { get; set; }
    public int? MemberId { get; set; }
    /// <summary>JSON sinh hiệu do Điều dưỡng đo (null nếu chưa đo)</summary>
    public string? NurseNote { get; set; }
}
