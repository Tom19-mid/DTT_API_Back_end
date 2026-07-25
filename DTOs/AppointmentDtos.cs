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

public class AppointmentResponseDto
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string TimeSlot { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
    public int QueueNumber { get; set; }
    public string ClinicRoom { get; set; } = "Phòng 101";
    public string Fee { get; set; } = "250.000đ";
    public bool IsPackage { get; set; } = false;
    public DateTime CreatedAt { get; set; }
}
