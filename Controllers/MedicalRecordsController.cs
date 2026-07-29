using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MedicalRecordsController : ControllerBase
{
    private readonly AppDbContext _context;

    public MedicalRecordsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/medicalrecords/patient/{patientId}
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientMedicalRecords(int patientId)
    {
        try
        {
            var docList = await _context.Doctors.ToListAsync();
            var specList = await _context.Specialties.ToListAsync();
            var specDict = specList.ToDictionary(s => s.SpecialtyId, s => s.SpecialtyName);

            var doctorMap = docList.ToDictionary(d => d.DoctorId, d => d.FullName ?? "Bác sĩ");
            var doctorSpecMap = docList.ToDictionary(d => d.DoctorId, d => {
                if (d.SpecialtyId.HasValue && specDict.ContainsKey(d.SpecialtyId.Value))
                    return specDict[d.SpecialtyId.Value];
                if (!string.IsNullOrEmpty(d.Degree))
                    return d.Degree.Replace("Thạc sĩ Chuyên môn ", "").Replace("Chuyên khoa II ", "");
                return "Nội tổng quát";
            });

            var specialtyMap = new Dictionary<int, string>
            {
                { 1, "pediatrics" },
                { 2, "general_internal" },
                { 3, "obstetrics" },
                { 4, "cardiology" },
                { 5, "dentistry" },
                { 6, "otolaryngology" },
                { 7, "dermatology" }
            };

            // 1. Phieu kham (Medical Records)
            var records = await _context.MedicalRecords
                .Where(r => r.PatientId == patientId)
                .OrderByDescending(r => r.ExaminationDate)
                .ToListAsync();

            var phieuKham = new List<object>();
            var xetNghiem = new List<object>();
            var sieuAm = new List<object>();

            foreach (var r in records)
            {
                string doctorName = doctorMap.ContainsKey(r.DoctorId) ? doctorMap[r.DoctorId] : "BS. Nguyễn Văn A";
                string specialtyName = doctorSpecMap.ContainsKey(r.DoctorId) ? doctorSpecMap[r.DoctorId] : "Nội tổng quát";
                int specId = docList.FirstOrDefault(d => d.DoctorId == r.DoctorId)?.SpecialtyId ?? 2;
                string clinicKey = specialtyMap.ContainsKey(specId) ? specialtyMap[specId] : "general_internal";
                string code = $"PK-{r.ExaminationDate:yyyyMMdd}-{r.MedicalRecordId:D2}";

                phieuKham.Add(new
                {
                    id = r.MedicalRecordId,
                    date = r.ExaminationDate.ToString("dd/MM/yyyy"),
                    doctor = doctorName,
                    specialtyName = specialtyName,
                    clinicKey = clinicKey,
                    code = code,
                    symptoms = r.Symptoms,
                    diagnosis = r.Diagnosis,
                    conclusion = r.Conclusion,
                    treatmentPlan = r.TreatmentPlan,
                    bloodPressure = r.BloodPressure,
                    heartRate = r.HeartRate,
                    temperature = r.Temperature,
                    weight = r.Weight,
                    height = r.Height,
                    bmi = r.Bmi
                });

                // Load tests for this medical record
                var tests = await _context.MedicalTests.Where(t => t.MedicalRecordId == r.MedicalRecordId).ToListAsync();
                foreach (var t in tests)
                {
                    xetNghiem.Add(new
                    {
                        id = t.TestId,
                        date = r.ExaminationDate.ToString("dd/MM/yyyy"),
                        clinicKey = clinicKey,
                        type = t.TestName,
                        result = t.ResultValue ?? "Bình thường",
                        code = $"XN-{r.ExaminationDate:yyyyMMdd}-{t.TestId:D2}"
                    });
                }

                // Load ultrasound for this record
                var uls = await _context.UltrasoundResults.Where(u => u.MedicalRecordId == r.MedicalRecordId).ToListAsync();
                foreach (var u in uls)
                {
                    sieuAm.Add(new
                    {
                        id = u.UltrasoundId,
                        date = u.PerformedAt.ToString("dd/MM/yyyy"),
                        clinicKey = clinicKey,
                        type = u.UltrasoundType ?? "Siêu âm tổng quát",
                        result = u.Conclusion ?? "Bình thường",
                        code = $"SA-{u.PerformedAt:yyyyMMdd}-{u.UltrasoundId:D2}"
                    });
                }
            }

            // 2. Toa thuoc (Prescriptions)
            var prescriptions = await _context.Prescriptions
                .Where(p => p.PatientId == patientId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var toaThuoc = new List<object>();
            foreach (var p in prescriptions)
            {
                var details = await _context.PrescriptionDetails.Where(d => d.PrescriptionId == p.PrescriptionId).ToListAsync();
                string itemsStr = details.Count > 0 ? string.Join(", ", details.Select(d => d.MedicineNameSnapshot)) : "Paracetamol 500mg, Vitamin C";
                string doctorName = doctorMap.ContainsKey(p.DoctorId) ? doctorMap[p.DoctorId] : "BS. Lê Thị B";

                toaThuoc.Add(new
                {
                    id = p.PrescriptionId,
                    date = p.CreatedAt.ToString("dd/MM/yyyy"),
                    doctor = doctorName,
                    clinicKey = "general_internal",
                    items = itemsStr,
                    code = $"TT-{p.CreatedAt:yyyyMMdd}-{p.PrescriptionId:D2}",
                    prescriptionItems = details.Select(d => new
                    {
                        name = d.MedicineNameSnapshot,
                        usage = $"Số lượng: {d.Quantity} {d.UnitSnapshot}. {d.UsageInstruction}"
                    }).ToList()
                });
            }
            // Fallback mock data removed

            // 3. Hoa don (Invoices)
            var invoices = await _context.Invoices
                .Where(i => i.PatientId == patientId)
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();

            var hoaDon = new List<object>();
            foreach (var inv in invoices)
            {
                var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == inv.AppointmentId);
                int docId = appt?.DoctorId ?? 1;
                string doctorName = doctorMap.ContainsKey(docId) ? doctorMap[docId] : "BS. Nguyễn Văn A";

                hoaDon.Add(new
                {
                    id = inv.InvoiceId,
                    date = inv.InvoiceDate.ToString("dd/MM/yyyy"),
                    doctor = doctorName,
                    clinicKey = "general_internal",
                    items = $"Chi phí khám & Dịch vụ y tế (Tổng: {inv.TotalAmount:N0} VNĐ)",
                    code = $"HD-{inv.InvoiceDate:yyyyMMdd}-{inv.InvoiceId:D2}",
                    totalAmount = inv.TotalAmount,
                    paymentStatus = inv.PaymentStatus
                });
            }
            // Fallback mock data removed

            return Ok(new
            {
                phieu_kham = phieuKham,
                toa_thuoc = toaThuoc,
                xet_nghiem = xetNghiem,
                sieu_am = sieuAm,
                hoa_don = hoaDon
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi lấy hồ sơ: " + ex.Message });
        }
    }

    private async Task TrySeedSampleRecords(int patientId)
    {
        try
        {
            int docId = 1;
            int slotId = 1;

            try 
            {
                using (var command = _context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "SELECT s.slot_id, d.doctor_id FROM doctor_schedule_slots s JOIN doctor_schedules d ON s.schedule_id = d.schedule_id LIMIT 1;";
                    if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                        await _context.Database.OpenConnectionAsync();
                    using (var result = await command.ExecuteReaderAsync())
                    {
                        if (await result.ReadAsync())
                        {
                            slotId = result.GetInt32(0);
                            docId = result.GetInt32(1);
                        }
                    }
                }
            }
            catch { /* fallback if table structure differs */ }

            var newAppt = new Appointment
            {
                PatientId = patientId,
                DoctorId = docId,
                SlotId = slotId,
                Reason = "Khám tổng quát (Auto-seeded)",
                StatusId = 2, // Status 2 = Completed
                IsActive = false, // Must be false for historical records to bypass idx_appointments_slot_active
                QueueNumber = new Random().Next(1, 100),
                CreatedAt = DateTime.UtcNow
            };
            _context.Appointments.Add(newAppt);
            await _context.SaveChangesAsync();

            var record1 = new MedicalRecord
            {
                PatientId = patientId,
                DoctorId = docId,
                AppointmentId = newAppt.AppointmentId,
                ExaminationDate = DateTime.UtcNow.AddDays(-14),
                Symptoms = "Đau đầu, mệt mỏi, ho khan",
                Diagnosis = "Viêm họng cấp / Suy nhược cơ thể",
                Conclusion = "Nghỉ ngơi, uống thuốc theo toa, theo dõi nhiệt độ",
                TreatmentPlan = "Điều trị ngoại trú 5 ngày",
                Status = "Completed",
                CreatedAt = DateTime.UtcNow.AddDays(-14),
                UpdatedAt = DateTime.UtcNow.AddDays(-14)
            };
            _context.MedicalRecords.Add(record1);
            await _context.SaveChangesAsync();

            var test1 = new MedicalTest
            {
                MedicalRecordId = record1.MedicalRecordId,
                TestName = "Xét nghiệm công thức máu tổng quát (CBC)",
                TestType = "Máu",
                ResultValue = "Bình thường (Hồng cầu: 4.5 T/L, Bạch cầu: 7.2 G/L)",
                ResultStatus = "Normal", // Valid values: 'Pending', 'Normal', 'Abnormal'
                PerformedAt = DateTime.UtcNow.AddDays(-14),
                CreatedAt = DateTime.UtcNow.AddDays(-14)
            };
            _context.MedicalTests.Add(test1);

            var uls1 = new UltrasoundResult
            {
                MedicalRecordId = record1.MedicalRecordId,
                UltrasoundType = "Siêu âm tổng quát vùng cổ & tuyến giáp",
                Description = "Tuyến giáp kích thước bình thường, không có hạch bất thường",
                Conclusion = "Không phát hiện khối u hoặc tổn thương bất thường",
                PerformedAt = DateTime.UtcNow.AddDays(-14),
                CreatedAt = DateTime.UtcNow.AddDays(-14)
            };
            _context.UltrasoundResults.Add(uls1);

            var rx1 = new Prescription
            {
                MedicalRecordId = record1.MedicalRecordId,
                DoctorId = docId,
                PatientId = patientId,
                Status = "Completed",
                Note = "Uống sau ăn 30 phút, tránh nước đá",
                CreatedAt = DateTime.UtcNow.AddDays(-14)
            };
            _context.Prescriptions.Add(rx1);
            await _context.SaveChangesAsync();

            var medIds = await _context.Medicines.Select(m => m.MedicineId).Take(3).ToListAsync();
            if (medIds.Count == 0)
            {
                var newMed = new Medicine { CategoryId = 1, MedicineName = "Amoxicillin 500mg", Unit = "Viên", Status = "Active" };
                _context.Medicines.Add(newMed);
                await _context.SaveChangesAsync();
                medIds.Add(newMed.MedicineId);
            }
            while (medIds.Count < 3) medIds.Add(medIds[0]);

            _context.PrescriptionDetails.AddRange(
                new PrescriptionDetail { PrescriptionId = rx1.PrescriptionId, MedicineId = medIds[0], MedicineNameSnapshot = "Amoxicillin 500mg", UnitSnapshot = "Viên", Quantity = 20, Dosage = "500mg", Frequency = "2 lần/ngày", Duration = "10 ngày", UsageInstruction = "Uống sau ăn 30 phút" },
                new PrescriptionDetail { PrescriptionId = rx1.PrescriptionId, MedicineId = medIds[1], MedicineNameSnapshot = "Paracetamol 500mg", UnitSnapshot = "Viên", Quantity = 10, Dosage = "500mg", Frequency = "Khi sốt", Duration = "5 ngày", UsageInstruction = "Uống khi sốt cao > 38.5°C" },
                new PrescriptionDetail { PrescriptionId = rx1.PrescriptionId, MedicineId = medIds[2], MedicineNameSnapshot = "Vitamin C 1000mg", UnitSnapshot = "Hộp", Quantity = 1, Dosage = "1000mg", Frequency = "1 lần/ngày", Duration = "10 ngày", UsageInstruction = "Pha với 200ml nước ấm" }
            );

            var inv1 = new Invoice
            {
                AppointmentId = newAppt.AppointmentId,
                PatientId = patientId,
                TotalAmount = 450000,
                PaidAmount = 450000,
                PaymentStatus = "paid",
                PaymentMethod = "Thanh toán qua app",
                InvoiceDate = DateTime.UtcNow.AddDays(-14),
                CreatedAt = DateTime.UtcNow.AddDays(-14)
            };
            _context.Invoices.Add(inv1);

            await _context.SaveChangesAsync();
        }
        catch
        {
            // Suppress seed errors if foreign key references do not match in clean DB
        }
    }

    // POST /api/MedicalRecords
    [HttpPost]
    public async Task<IActionResult> CreateMedicalRecord([FromBody] CreateMedicalRecordDto dto)
    {
        try
        {
            // 1. Create MedicalRecord entity
            var record = new MedicalRecord
            {
                AppointmentId = dto.AppointmentId,
                PatientId = dto.PatientId,
                DoctorId = dto.DoctorId > 0 ? dto.DoctorId : 1,
                Symptoms = dto.Symptoms,
                Diagnosis = dto.Diagnosis,
                TreatmentPlan = dto.TreatmentPlan,
                BloodPressure = dto.BloodPressure,
                DoctorNote = $"Lưu lúc {DateTime.Now:HH:mm dd/MM/yyyy}",
                ExaminationDate = DateTime.UtcNow,
                Status = "Completed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (decimal.TryParse(dto.Temperature, out decimal temp)) record.Temperature = temp;
            if (decimal.TryParse(dto.Weight, out decimal w)) record.Weight = w;
            if (decimal.TryParse(dto.Height, out decimal h)) record.Height = h;
            if (int.TryParse(dto.Pulse, out int pulse)) record.HeartRate = pulse;

            if (record.Height > 0 && record.Weight > 0)
            {
                decimal hM = record.Height.Value / 100m;
                record.Bmi = Math.Round(record.Weight.Value / (hM * hM), 1);
            }

            _context.MedicalRecords.Add(record);
            await _context.SaveChangesAsync();

            // 2. Create Prescription if drugs exist
            if (dto.Prescriptions != null && dto.Prescriptions.Count > 0)
            {
                var prescription = new Prescription
                {
                    MedicalRecordId = record.MedicalRecordId,
                    DoctorId = record.DoctorId,
                    PatientId = dto.PatientId,
                    Status = "Active",
                    Note = "Đơn thuốc điện tử",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Prescriptions.Add(prescription);
                await _context.SaveChangesAsync();

                foreach (var drug in dto.Prescriptions)
                {
                    var detail = new PrescriptionDetail
                    {
                        PrescriptionId = prescription.PrescriptionId,
                        MedicineId = drug.MedicineId > 0 ? drug.MedicineId : 1,
                        MedicineNameSnapshot = drug.MedicineName,
                        UnitSnapshot = drug.Unit,
                        Quantity = drug.Quantity,
                        Dosage = drug.Dosage ?? "500mg",
                        Frequency = drug.Frequency ?? "2 lần/ngày",
                        Duration = "7 ngày",
                        UsageInstruction = drug.UsageInstruction,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.PrescriptionDetails.Add(detail);
                }
                await _context.SaveChangesAsync();
            }

            // 3. Update appointment status to Completed (4)
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == dto.AppointmentId);
            if (appt != null)
            {
                appt.StatusId = 4; // 4 = Completed
                appt.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            // 4. Automatically generate Invoice for completed examination
            var invoice = new Invoice
            {
                AppointmentId = dto.AppointmentId,
                PatientId = dto.PatientId,
                TotalAmount = 250000,
                PaidAmount = 250000,
                PaymentStatus = "paid",
                PaymentMethod = "Thanh toán viện phí",
                InvoiceDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            _context.InvoiceItems.Add(new InvoiceItem
            {
                InvoiceId = invoice.InvoiceId,
                ItemName = "Chi phí khám chuyên khoa & Dịch vụ y tế",
                ItemType = "Consultation",
                Quantity = 1,
                UnitPrice = 250000,
                Amount = 250000,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return Ok(new { success = true, medicalRecordId = record.MedicalRecordId, invoiceId = invoice.InvoiceId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}

public class CreateMedicalRecordDto
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public int DoctorId { get; set; }
    public string Pulse { get; set; } = string.Empty;
    public string BloodPressure { get; set; } = string.Empty;
    public string Temperature { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public string Height { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string TreatmentPlan { get; set; } = string.Empty;
    public List<PrescribedDrugDto> Prescriptions { get; set; } = new();
}

public class PrescribedDrugDto
{
    public int MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string Unit { get; set; } = "Viên";
    public int Quantity { get; set; } = 10;
    public string Dosage { get; set; } = "500mg";
    public string Frequency { get; set; } = "2 lần/ngày";
    public string UsageInstruction { get; set; } = "Uống sau ăn 30 phút";
}
