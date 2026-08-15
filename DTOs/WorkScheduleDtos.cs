using System.ComponentModel.DataAnnotations;

namespace DTT_Backend_API.DTOs;

public class TimeSlotDto
{
    public int SlotId { get; set; }
    public int ScheduleId { get; set; }
    public string? ScheduleCode { get; set; }
    public string? SlotCode { get; set; }
    public int SlotOrder { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string Status { get; set; } = "Chưa đặt lịch"; // "Chưa đặt lịch", "Đã đặt lịch", "Đã đóng"
    public string? PatientName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class WorkScheduleResponseDto
{
    public int ScheduleId { get; set; }
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string? Specialty { get; set; }
    public string? SpecialtyName { get; set; }
    public string WorkDate { get; set; } = string.Empty; // Format: DD/MM/YYYY
    public string StartTime { get; set; } = string.Empty; // Format: HH:mm
    public string EndTime { get; set; } = string.Empty;   // Format: HH:mm
    public string Status { get; set; } = "Trống lịch";   // "Trống lịch", "Còn lịch để đặt", "Đã hết lịch để đặt", "Không hoạt động"
    public string? ScheduleCode { get; set; }
    public bool IsAvailable { get; set; } = true;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<TimeSlotDto> TimeSlots { get; set; } = new();
}

public class CreateWorkScheduleDto
{
    [Required(ErrorMessage = "DoctorId là bắt buộc.")]
    public int DoctorId { get; set; }

    public string? DoctorName { get; set; }

    [Required(ErrorMessage = "Ngày làm việc là bắt buộc.")]
    public string WorkDate { get; set; } = string.Empty; // DD/MM/YYYY or YYYY-MM-DD

    [Required(ErrorMessage = "Giờ bắt đầu là bắt buộc.")]
    public string StartTime { get; set; } = "08:00";

    [Required(ErrorMessage = "Giờ kết thúc là bắt buộc.")]
    public string EndTime { get; set; } = "17:00";

    public string? Status { get; set; } = "Trống lịch";
}

public class UpdateWorkScheduleDto
{
    public int? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public string? WorkDate { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Status { get; set; }
    public List<TimeSlotDto>? TimeSlots { get; set; }
}

public class ToggleLockScheduleDto
{
    public string? Status { get; set; }
    public bool? IsLocked { get; set; }
}
