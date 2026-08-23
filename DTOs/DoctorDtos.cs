using System.ComponentModel.DataAnnotations;

namespace DTT_Backend_API.DTOs;

public class CreateDoctorDto
{
    [Required(ErrorMessage = "Họ và tên bác sĩ là bắt buộc.")]
    public string FullName { get; set; } = string.Empty;

    public string? Degree { get; set; }

    public int ExperienceYears { get; set; } = 0;

    public string? ClinicRoom { get; set; }

    public int? SpecialtyId { get; set; }

    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
    [StringLength(10, ErrorMessage = "Số điện thoại tối đa 10 chữ số.")]
    public string Phone { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string Status { get; set; } = "Active";

    public string? AvatarUrl { get; set; }

    public string? Avatar { get; set; }

    // Admin tự đánh dấu hồ sơ test (QA tạo để thử nghiệm) — tự động ẩn khỏi App Bệnh nhân.
    public bool IsTestData { get; set; } = false;

    // Leave fields (Format: DD/MM/YYYY)
    public string? LeaveStartDate { get; set; }
    public string? LeaveEndDate { get; set; }
    public string? LeaveReason { get; set; }
    public string? LeaveStatus { get; set; }
}

public class UpdateDoctorDto
{
    public string? FullName { get; set; }

    public string? Degree { get; set; }

    public int? ExperienceYears { get; set; }

    public string? ClinicRoom { get; set; }

    public int? SpecialtyId { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Status { get; set; }

    public string? AvatarUrl { get; set; }

    public string? Avatar { get; set; }

    // Admin tự đánh dấu hồ sơ test (QA tạo để thử nghiệm) — tự động ẩn khỏi App Bệnh nhân. Nullable
    // vì đây là edit DTO: không gửi field này nghĩa là "giữ nguyên giá trị hiện có", không mặc định về false.
    public bool? IsTestData { get; set; }

    // Leave fields (Format: DD/MM/YYYY)
    public string? LeaveStartDate { get; set; }
    public string? LeaveEndDate { get; set; }
    public string? LeaveReason { get; set; }
    public string? LeaveStatus { get; set; }
}

public class UpdateDoctorStatusDto
{
    [Required(ErrorMessage = "Trạng thái mới là bắt buộc.")]
    public string Status { get; set; } = string.Empty;
}
