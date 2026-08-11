using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DoctorLeave> DoctorLeaves => Set<DoctorLeave>();
    public DbSet<Specialty> Specialties => Set<Specialty>();
    public DbSet<Icd10Catalog> Icd10Catalogs => Set<Icd10Catalog>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentStatus> AppointmentStatuses => Set<AppointmentStatus>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<MedicalTest> MedicalTests => Set<MedicalTest>();
    public DbSet<UltrasoundResult> UltrasoundResults => Set<UltrasoundResult>();
    public DbSet<ClinicalService> ClinicalServices => Set<ClinicalService>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<HealthPackage> HealthPackages => Set<HealthPackage>();
    public DbSet<HealthPackageDetail> HealthPackageDetails => Set<HealthPackageDetail>();
    public DbSet<MedicineCategory> MedicineCategories => Set<MedicineCategory>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionDetail> PrescriptionDetails => Set<PrescriptionDetail>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
}
