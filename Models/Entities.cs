using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DTT_Backend_API.Models;

[Table("roles")]
public class Role
{
    [Key]
    [Column("role_id")]
    public int RoleId { get; set; }

    [Required]
    [Column("role_name")]
    [StringLength(50)]
    public string RoleName { get; set; } = string.Empty;

    [Column("role_code")]
    [StringLength(50)]
    public string? RoleCode { get; set; }

    [Column("description")]
    public string? Description { get; set; }
}

[Table("users")]
public class User
{
    [Key]
    [Column("user_id")]
    public Guid UserId { get; set; } = Guid.NewGuid();

    [Required]
    [Column("phone_number")]
    [StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [Column("email")]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("role_id")]
    public int RoleId { get; set; } = 1; // 1 = Patient

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    [Column("avatar_url")]
    public string? AvatarUrl { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("patients")]
public class Patient
{
    [Key]
    [Column("patient_id")]
    public int PatientId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("full_name")]
    [StringLength(255)]
    public string? FullName { get; set; }

    [Column("date_of_birth")]
    public DateTime? DateOfBirth { get; set; }

    [Column("gender")]
    [StringLength(20)]
    public string? Gender { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("health_insurance_number")]
    [StringLength(50)]
    public string? HealthInsuranceNumber { get; set; }

    [Column("cccd_number")]
    [StringLength(12)]
    public string? CccdNumber { get; set; }

    [Column("phone_number")]
    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    [Column("verification_status")]
    [StringLength(20)]
    public string VerificationStatus { get; set; } = "pending";

    [Column("verified_at")]
    public DateTime? VerifiedAt { get; set; }

    [Column("verified_by")]
    public Guid? VerifiedBy { get; set; }

    [Column("verification_note")]
    public string? VerificationNote { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("family_members")]
public class FamilyMember
{
    [Key]
    [Column("member_id")]
    public int MemberId { get; set; }

    [Column("owner_patient_id")]
    public int OwnerPatientId { get; set; }

    [Required]
    [Column("full_name")]
    [StringLength(255)]
    public string FullName { get; set; } = string.Empty;

    [Column("date_of_birth")]
    public DateTime? DateOfBirth { get; set; }

    [Column("gender")]
    [StringLength(20)]
    public string? Gender { get; set; }

    [Required]
    [Column("relationship")]
    [StringLength(50)]
    public string Relationship { get; set; } = string.Empty;

    [Column("phone_number")]
    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    [Column("cccd_number")]
    [StringLength(12)]
    public string? CccdNumber { get; set; }

    [Column("health_insurance_number")]
    [StringLength(50)]
    public string? HealthInsuranceNumber { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("verification_status")]
    [StringLength(20)]
    public string VerificationStatus { get; set; } = "pending";

    [Column("verified_at")]
    public DateTime? VerifiedAt { get; set; }

    [Column("verified_by")]
    public Guid? VerifiedBy { get; set; }

    [Column("verification_note")]
    public string? VerificationNote { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("specialties")]
public class Specialty
{
    [Key]
    [Column("specialty_id")]
    public int SpecialtyId { get; set; }

    [Required]
    [Column("specialty_name")]
    [StringLength(100)]
    public string SpecialtyName { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("status")]
    public bool Status { get; set; } = true;
}

[Table("doctors")]
public class Doctor
{
    [Key]
    [Column("doctor_id")]
    public int DoctorId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("specialty_id")]
    public int? SpecialtyId { get; set; }

    [Column("full_name")]
    [StringLength(255)]
    public string? FullName { get; set; }

    [Column("degree")]
    [StringLength(255)]
    public string? Degree { get; set; }

    [Column("experience_years")]
    public int ExperienceYears { get; set; } = 0;

    [Column("clinic_room")]
    [StringLength(50)]
    public string? ClinicRoom { get; set; }

    [Column("leave_start_date")]
    public DateOnly? LeaveStartDate { get; set; }

    [Column("leave_end_date")]
    public DateOnly? LeaveEndDate { get; set; }

    [Column("avatar_url")]
    public string? AvatarUrl { get; set; }

    [Column("rating")]
    public decimal Rating { get; set; } = 5.0m;

    [Column("review_count")]
    public int ReviewCount { get; set; } = 0;

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = "Active";
}

[Table("appointments")]
public class Appointment
{
    [Key]
    [Column("appointment_id")]
    public int AppointmentId { get; set; }

    [Column("patient_id")]
    public int PatientId { get; set; }

    [Column("doctor_id")]
    public int DoctorId { get; set; }

    [Column("slot_id")]
    public int? SlotId { get; set; }

    [Column("reason")]
    public string? Reason { get; set; }

    [Column("status_id")]
    public int StatusId { get; set; } = 1;

    [Column("queue_number")]
    public int QueueNumber { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    [Column("cancel_reason")]
    public string? CancelReason { get; set; }

    [Column("cancelled_at")]
    public DateTime? CancelledAt { get; set; }

    [Column("cancelled_by")]
    public Guid? CancelledBy { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("appointment_statuses")]
public class AppointmentStatus
{
    [Key]
    [Column("status_id")]
    public int StatusId { get; set; }

    [Column("status_name")]
    [StringLength(50)]
    public string StatusName { get; set; } = string.Empty;
}

[Table("medical_records")]
public class MedicalRecord
{
    [Key]
    [Column("medical_record_id")]
    public int MedicalRecordId { get; set; }

    [Column("appointment_id")]
    public int AppointmentId { get; set; }

    [Column("patient_id")]
    public int PatientId { get; set; }

    [Column("doctor_id")]
    public int DoctorId { get; set; }

    [Column("symptoms")]
    public string? Symptoms { get; set; }

    [Column("diagnosis")]
    public string? Diagnosis { get; set; }

    [Column("conclusion")]
    public string? Conclusion { get; set; }

    [Column("treatment_plan")]
    public string? TreatmentPlan { get; set; }

    [Column("doctor_note")]
    public string? DoctorNote { get; set; }

    [Column("blood_pressure")]
    public string? BloodPressure { get; set; }

    [Column("heart_rate")]
    public int? HeartRate { get; set; }

    [Column("temperature")]
    public decimal? Temperature { get; set; }

    [Column("height")]
    public decimal? Height { get; set; }

    [Column("weight")]
    public decimal? Weight { get; set; }

    [Column("bmi")]
    public decimal? Bmi { get; set; }

    [Column("icd_code")]
    public string? IcdCode { get; set; }

    [Column("icd_description")]
    public string? IcdDescription { get; set; }

    [Column("examination_date")]
    public DateTime ExaminationDate { get; set; }

    [Column("re_examination_date")]
    public DateOnly? ReExaminationDate { get; set; }

    [Column("status")]
    [StringLength(30)]
    public string Status { get; set; } = "Draft";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("medical_tests")]
public class MedicalTest
{
    [Key]
    [Column("test_id")]
    public int TestId { get; set; }

    [Column("medical_record_id")]
    public int MedicalRecordId { get; set; }

    [Column("test_name")]
    [StringLength(255)]
    public string TestName { get; set; } = string.Empty;

    [Column("test_type")]
    [StringLength(50)]
    public string? TestType { get; set; }

    [Column("result_value")]
    public string? ResultValue { get; set; }

    [Column("result_status")]
    [StringLength(20)]
    public string ResultStatus { get; set; } = "pending";

    [Column("unit")]
    [StringLength(50)]
    public string? Unit { get; set; }

    [Column("reference_range")]
    [StringLength(100)]
    public string? ReferenceRange { get; set; }

    [Column("performed_at")]
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

    [Column("result_file_url")]
    public string? ResultFileUrl { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("ultrasound_results")]
public class UltrasoundResult
{
    [Key]
    [Column("ultrasound_id")]
    public int UltrasoundId { get; set; }

    [Column("medical_record_id")]
    public int MedicalRecordId { get; set; }

    [Column("ultrasound_type")]
    [StringLength(100)]
    public string? UltrasoundType { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("conclusion")]
    public string? Conclusion { get; set; }

    [Column("image_urls", TypeName = "text[]")]
    public string[]? ImageUrls { get; set; }

    [Column("performed_at")]
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("invoices")]
public class Invoice
{
    [Key]
    [Column("invoice_id")]
    public int InvoiceId { get; set; }

    [Column("appointment_id")]
    public int AppointmentId { get; set; }

    [Column("patient_id")]
    public int PatientId { get; set; }

    [Column("total_amount")]
    public decimal TotalAmount { get; set; }

    [Column("paid_amount")]
    public decimal PaidAmount { get; set; }

    [Column("payment_status")]
    [StringLength(20)]
    public string PaymentStatus { get; set; } = "unpaid";

    [Column("payment_method")]
    [StringLength(50)]
    public string? PaymentMethod { get; set; }

    [Column("invoice_date")]
    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("notifications")]
public class Notification
{
    [Key]
    [Column("notification_id")]
    public int NotificationId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("title")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("type")]
    [StringLength(50)]
    public string Type { get; set; } = "system";

    [Column("is_read")]
    public bool IsRead { get; set; } = false;

    [Column("related_id")]
    public int? RelatedId { get; set; }

    [Column("related_type")]
    [StringLength(50)]
    public string? RelatedType { get; set; }

    [Column("read_at")]
    public DateTime? ReadAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ── Health Packages ───────────────────────────────────────────────────────────

[Table("health_packages")]
public class HealthPackage
{
    [Key]
    [Column("package_id")]
    public int PackageId { get; set; }

    [Required]
    [Column("title")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("gender_target")]
    [StringLength(10)]
    public string GenderTarget { get; set; } = "all"; // 'male' | 'female' | 'all'

    [Column("image_url")]
    public string? ImageUrl { get; set; }

    [Column("booked_count")]
    public int BookedCount { get; set; } = 0;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<HealthPackageDetail> Details { get; set; } = new();
}

[Table("health_package_details")]
public class HealthPackageDetail
{
    [Key]
    [Column("detail_id")]
    public int DetailId { get; set; }

    [Column("package_id")]
    public int PackageId { get; set; }

    [Required]
    [Column("service_name")]
    [StringLength(255)]
    public string ServiceName { get; set; } = string.Empty;

    [Column("sort_order")]
    public int SortOrder { get; set; } = 0;

    // Navigation
    [ForeignKey("PackageId")]
    public HealthPackage? Package { get; set; }
}

// ── Invoice Items ──────────────────────────────────────────────────────────────

[Table("invoice_items")]
public class InvoiceItem
{
    [Key]
    [Column("item_id")]
    public int ItemId { get; set; }

    [Column("invoice_id")]
    public int InvoiceId { get; set; }

    [Required]
    [Column("item_name")]
    [StringLength(255)]
    public string ItemName { get; set; } = string.Empty;

    [Column("item_type")]
    [StringLength(50)]
    public string? ItemType { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; } = 1;

    [Column("unit_price")]
    public decimal UnitPrice { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── Medicines ─────────────────────────────────────────────────────────────────

[Table("medicine_categories")]
public class MedicineCategory
{
    [Key]
    [Column("category_id")]
    public int CategoryId { get; set; }

    [Required]
    [Column("category_name")]
    [StringLength(100)]
    public string CategoryName { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("medicines")]
public class Medicine
{
    [Key]
    [Column("medicine_id")]
    public int MedicineId { get; set; }

    [Column("category_id")]
    public int CategoryId { get; set; }

    [Required]
    [Column("medicine_name")]
    [StringLength(255)]
    public string MedicineName { get; set; } = string.Empty;

    [Required]
    [Column("unit")]
    [StringLength(50)]
    public string Unit { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("default_usage")]
    public string? DefaultUsage { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── Prescriptions ─────────────────────────────────────────────────────────────

[Table("prescriptions")]
public class Prescription
{
    [Key]
    [Column("prescription_id")]
    public int PrescriptionId { get; set; }

    [Column("medical_record_id")]
    public int MedicalRecordId { get; set; }

    [Column("doctor_id")]
    public int DoctorId { get; set; }

    [Column("patient_id")]
    public int PatientId { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    [Column("note")]
    public string? Note { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<PrescriptionDetail> Details { get; set; } = new();
}

[Table("prescription_details")]
public class PrescriptionDetail
{
    [Key]
    [Column("prescription_detail_id")]
    public int PrescriptionDetailId { get; set; }

    [Column("prescription_id")]
    public int PrescriptionId { get; set; }

    [Column("medicine_id")]
    public int MedicineId { get; set; }

    [Required]
    [Column("medicine_name_snapshot")]
    [StringLength(255)]
    public string MedicineNameSnapshot { get; set; } = string.Empty;

    [Required]
    [Column("unit_snapshot")]
    [StringLength(50)]
    public string UnitSnapshot { get; set; } = string.Empty;

    [Column("quantity")]
    public int Quantity { get; set; }

    [Required]
    [Column("dosage")]
    [StringLength(100)]
    public string Dosage { get; set; } = string.Empty;

    [Required]
    [Column("frequency")]
    [StringLength(100)]
    public string Frequency { get; set; } = string.Empty;

    [Required]
    [Column("duration")]
    [StringLength(100)]
    public string Duration { get; set; } = string.Empty;

    [Column("usage_instruction")]
    public string? UsageInstruction { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
