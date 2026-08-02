namespace DTT_Backend_API.DTOs;

// Bác sĩ chỉ định Xét nghiệm/Siêu âm/cả hai ngay trong lúc "Đang Khám" (status=3).
// serviceIds trỏ tới medical_services — trộn lẫn cả loại 'Test' và 'Ultrasound' trong 1 lần chỉ định.
public class CreateClinicalOrdersDto
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public int DoctorId { get; set; }
    public Guid? OrderedByUserId { get; set; }
    public List<int> ServiceIds { get; set; } = new();
    // Áp dụng cho cả lô chỉ định trong lần gọi này (BS thường chỉ định các dịch vụ cùng 1 lý do/mức ưu tiên)
    public bool IsUrgent { get; set; } = false;
    public string? ClinicalNote { get; set; }
}

public class ClinicalOrderItemDto
{
    public string Kind { get; set; } = string.Empty; // "Test" | "Ultrasound"
    public int Id { get; set; } // TestId hoặc UltrasoundId
    public int MedicalRecordId { get; set; }
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public int PatientAge { get; set; }
    public string PatientGender { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public bool IsUrgent { get; set; } = false;
    public string? ClinicalNote { get; set; }
    public int ImageCount { get; set; }
    public DateTime OrderedAt { get; set; }

    // Chỉ có giá trị khi Status != "Pending" — dùng để Bác sĩ xem lại kết quả trong phiếu khám
    public string? ResultValue { get; set; }
    public string? Unit { get; set; }
    public string? ReferenceRange { get; set; }
    public string? Description { get; set; }
    public string? Conclusion { get; set; }
}

public class SubmitTestResultDto
{
    public string ResultValue { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? ReferenceRange { get; set; }
    // 'Normal' | 'Abnormal'
    public string ResultStatus { get; set; } = "Normal";
    public string? ResultFileUrl { get; set; }
    public Guid? PerformedByUserId { get; set; }
}

public class SubmitUltrasoundResultDto
{
    public string? Description { get; set; }
    public string Conclusion { get; set; } = string.Empty;
    // Ảnh KHÔNG đi qua DTO này — quản lý riêng qua endpoint upload/xóa ảnh (POST|DELETE .../images).
    public Guid? PerformedByUserId { get; set; }
}
