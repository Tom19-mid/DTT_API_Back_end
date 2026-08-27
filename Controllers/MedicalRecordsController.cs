using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MedicalRecordsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MedicalRecordsController(AppDbContext context)
        {
            _context = context;
        }

                // GET /api/MedicalRecords/all?search={search}&doctorId={doctorId}&patientId={patientId}
        [HttpGet("all")]
        public async Task<IActionResult> GetAllMedicalRecords([FromQuery] string? search, [FromQuery] int? doctorId, [FromQuery] int? patientId)
        {
            try
            {
                var query = _context.MedicalRecords.AsQueryable();

                if (doctorId.HasValue && doctorId.Value > 0)
                {
                    query = query.Where(r => r.DoctorId == doctorId.Value);
                }

                if (patientId.HasValue && patientId.Value > 0)
                {
                    query = query.Where(r => r.PatientId == patientId.Value);
                }

                var records = await query
                    .OrderByDescending(r => r.ExaminationDate)
                    .ToListAsync();

                if (records.Count == 0)
                {
                    return Ok(new List<object>());
                }

                var patientIds = records.Select(r => r.PatientId).Distinct().ToList();
                var doctorIds = records.Select(r => r.DoctorId).Distinct().ToList();
                var recordIds = records.Select(r => r.MedicalRecordId).ToList();

                var patients = await _context.Patients
                    .Where(p => patientIds.Contains(p.PatientId))
                    .ToDictionaryAsync(p => p.PatientId);

                var doctors = await _context.Doctors
                    .Where(d => doctorIds.Contains(d.DoctorId))
                    .ToDictionaryAsync(d => d.DoctorId);

                var prescriptions = await _context.Prescriptions
                    .Where(p => recordIds.Contains(p.MedicalRecordId))
                    .ToListAsync();

                var pIds = prescriptions.Select(p => p.PrescriptionId).ToList();
                var prescriptionDetails = await _context.PrescriptionDetails
                    .Where(d => pIds.Contains(d.PrescriptionId))
                    .ToListAsync();

                var detailsByPrescriptionId = prescriptionDetails
                    .GroupBy(d => d.PrescriptionId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var result = new List<dynamic>();

                foreach (var r in records)
                {
                    patients.TryGetValue(r.PatientId, out var patient);
                    doctors.TryGetValue(r.DoctorId, out var doctor);

                    var rx = prescriptions.FirstOrDefault(p => p.MedicalRecordId == r.MedicalRecordId);
                    var details = rx != null && detailsByPrescriptionId.TryGetValue(rx.PrescriptionId, out var dList)
                        ? dList
                        : new List<PrescriptionDetail>();

                    string presSummary = details.Count > 0
                        ? string.Join(", ", details.Select(d => $"{d.MedicineNameSnapshot} ({d.Quantity} {d.UnitSnapshot})"))
                        : (rx != null && !string.IsNullOrEmpty(rx.Note) ? rx.Note : "");

                    string patientName = patient?.FullName ?? "Bá»‡nh nhÃ¢n";
                    string phone = patient?.PhoneNumber ?? "";
                    string doctorName = doctor != null ? $"{doctor.Degree} {doctor.FullName}".Trim() : "BÃ¡c sÄ©";

                    // Filter search if provided
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        string s = search.Trim().ToLower();
                        bool match = patientName.ToLower().Contains(s) ||
                                     phone.ToLower().Contains(s) ||
                                     (r.IcdCode != null && r.IcdCode.ToLower().Contains(s)) ||
                                     (r.Diagnosis != null && r.Diagnosis.ToLower().Contains(s));
                        if (!match) continue;
                    }

                    result.Add(new
                    {
                        medicalRecordId = r.MedicalRecordId,
                        appointmentId = r.AppointmentId,
                        patientId = r.PatientId,
                        patientName = patientName,
                        phoneNumber = phone,
                        gender = patient?.Gender ?? "",
                        dateOfBirth = patient?.DateOfBirth?.ToString("dd/MM/yyyy") ?? "",
                        doctorId = r.DoctorId,
                        doctorName = doctorName,
                        examinationDate = r.ExaminationDate.ToString("dd/MM/yyyy HH:mm"),
                        bloodPressure = r.BloodPressure ?? "",
                        pulse = r.HeartRate?.ToString() ?? "",
                        temperature = r.Temperature?.ToString() ?? "",
                        weight = r.Weight?.ToString() ?? "",
                        height = r.Height?.ToString() ?? "",
                        bmi = r.Bmi?.ToString() ?? "",
                        symptoms = r.Symptoms ?? "",
                        diagnosis = r.Diagnosis ?? "",
                        icdCode = r.IcdCode ?? "",
                        icdDescription = r.IcdDescription ?? "",
                        treatmentPlan = r.TreatmentPlan ?? "",
                        doctorNote = r.DoctorNote ?? "",
                        status = r.Status,
                        prescriptionsSummary = presSummary,
                        prescriptionId = rx?.PrescriptionId ?? 0,
                        prescriptionStatus = rx?.Status ?? ""
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // =========================================================================
        // [Old code - GetMedicalRecordsByPatient chỉ trả về mảng phẳng thô của bảng MedicalRecords]:
        // [HttpGet("patient/{patientId}")]
        // public async Task<IActionResult> GetMedicalRecordsByPatient(int patientId)
        // {
        //     try
        //     {
        //         var records = await _context.MedicalRecords
        //             .Where(r => r.PatientId == patientId)
        //             .OrderByDescending(r => r.ExaminationDate)
        //             .ToListAsync();
        // 
        //         return Ok(records);
        //     }
        //     catch (Exception ex)
        //     {
        //         return StatusCode(500, new { message = ex.Message });
        //     }
        // }
        // =========================================================================

        // [New code - GET /api/MedicalRecords/patient/{patientId}: Trả về đầy đủ dữ liệu 5 danh mục cho Mobile App]:
        [HttpGet("patient/{patientId}")]
        public async Task<IActionResult> GetMedicalRecordsByPatient(int patientId)
        {
            try
            {
                var records = await _context.MedicalRecords
                    .AsNoTracking()
                    .Where(r => r.PatientId == patientId)
                    .OrderByDescending(r => r.ExaminationDate)
                    .ToListAsync();

                var recordIds = records.Select(r => r.MedicalRecordId).ToList();
                var appointmentIds = records.Select(r => r.AppointmentId).Distinct().ToList();

                var appts = await _context.Appointments
                    .AsNoTracking()
                    .Where(a => a.PatientId == patientId || appointmentIds.Contains(a.AppointmentId))
                    .ToListAsync();
                var allApptIds = appts.Select(a => a.AppointmentId).Union(appointmentIds).Distinct().ToList();

                var patient = await _context.Patients
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PatientId == patientId);
                string patientName = patient?.FullName ?? "Bệnh nhân";

                var doctorIds = records.Select(r => r.DoctorId).Union(appts.Select(a => a.DoctorId)).Distinct().ToList();
                var doctors = await _context.Doctors
                    .AsNoTracking()
                    .Where(d => doctorIds.Contains(d.DoctorId))
                    .ToDictionaryAsync(d => d.DoctorId);

                var specialtyIds = doctors.Values.Where(d => d.SpecialtyId.HasValue).Select(d => d.SpecialtyId!.Value).Distinct().ToList();
                var specialties = await _context.Specialties
                    .AsNoTracking()
                    .Where(s => specialtyIds.Contains(s.SpecialtyId))
                    .ToDictionaryAsync(s => s.SpecialtyId);

                var prescriptions = await _context.Prescriptions
                    .AsNoTracking()
                    .Where(p => p.PatientId == patientId || recordIds.Contains(p.MedicalRecordId))
                    .OrderByDescending(p => p.CreatedAt)
                    .ToListAsync();

                var pIds = prescriptions.Select(p => p.PrescriptionId).ToList();
                var prescriptionDetails = await _context.PrescriptionDetails
                    .AsNoTracking()
                    .Where(d => pIds.Contains(d.PrescriptionId))
                    .ToListAsync();
                var detailsByPrescriptionId = prescriptionDetails
                    .GroupBy(d => d.PrescriptionId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var medicalTests = await _context.MedicalTests
                    .AsNoTracking()
                    .Where(t => recordIds.Contains(t.MedicalRecordId))
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();

                var ultrasoundResults = await _context.UltrasoundResults
                    .AsNoTracking()
                    .Where(u => recordIds.Contains(u.MedicalRecordId))
                    .OrderByDescending(u => u.CreatedAt)
                    .ToListAsync();

                var invoices = await _context.Invoices
                    .AsNoTracking()
                    .Where(i => i.PatientId == patientId || allApptIds.Contains(i.AppointmentId))
                    .OrderByDescending(i => i.CreatedAt)
                    .ToListAsync();

                var invIds = invoices.Select(i => i.InvoiceId).ToList();
                var invoiceItems = await _context.InvoiceItems
                    .AsNoTracking()
                    .Where(item => invIds.Contains(item.InvoiceId))
                    .ToListAsync();
                var itemsByInvoiceId = invoiceItems
                    .GroupBy(item => item.InvoiceId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                string GetDoctorName(int docId)
                {
                    if (doctors.TryGetValue(docId, out var doc))
                    {
                        string deg = !string.IsNullOrWhiteSpace(doc.Degree) ? doc.Degree.Trim() : "BS.";
                        return $"{deg} {doc.FullName}".Trim();
                    }
                    return "Bác sĩ";
                }

                string GetSpecialtyName(int docId, int apptId)
                {
                    if (doctors.TryGetValue(docId, out var doc) && doc.SpecialtyId.HasValue && specialties.TryGetValue(doc.SpecialtyId.Value, out var spec))
                    {
                        return spec.SpecialtyName;
                    }
                    var appt = appts.FirstOrDefault(a => a.AppointmentId == apptId);
                    if (appt != null && !string.IsNullOrEmpty(appt.Reason) && !appt.Reason.Contains(":"))
                    {
                        return appt.Reason;
                    }
                    return "Chuyên khoa";
                }

                // 1. Phieu kham
                var phieuKhamList = records.Select(r =>
                {
                    var docName = GetDoctorName(r.DoctorId);
                    var specName = GetSpecialtyName(r.DoctorId, r.AppointmentId);
                    var rx = prescriptions.FirstOrDefault(p => p.MedicalRecordId == r.MedicalRecordId);
                    var details = rx != null && detailsByPrescriptionId.TryGetValue(rx.PrescriptionId, out var dList) ? dList : new List<PrescriptionDetail>();
                    var rxItems = details.Select(d => new
                    {
                        name = $"{d.MedicineNameSnapshot} - SL: {d.Quantity} {d.UnitSnapshot}",
                        usage = $"Liều: {d.Dosage}, {d.Frequency}. {d.UsageInstruction}".Trim()
                    }).ToList();

                    return new
                    {
                        id = r.MedicalRecordId,
                        medicalRecordId = r.MedicalRecordId,
                        appointmentId = r.AppointmentId,
                        patientId = r.PatientId,
                        patientName = patientName,
                        code = $"PK-{r.ExaminationDate:yyyyMMdd}-{r.MedicalRecordId:D3}",
                        date = r.ExaminationDate.ToString("dd/MM/yyyy HH:mm"),
                        specialtyName = specName,
                        clinicKey = specName,
                        doctor = docName,
                        doctorId = r.DoctorId,
                        symptoms = r.Symptoms ?? "",
                        diagnosis = r.Diagnosis ?? "",
                        conclusion = r.Conclusion ?? "",
                        treatmentPlan = r.TreatmentPlan ?? "",
                        doctorNote = r.DoctorNote ?? "",
                        bloodPressure = r.BloodPressure ?? "",
                        heartRate = r.HeartRate,
                        temperature = r.Temperature,
                        weight = r.Weight,
                        height = r.Height,
                        bmi = r.Bmi,
                        icdCode = r.IcdCode ?? "",
                        icdDescription = r.IcdDescription ?? "",
                        status = r.Status,
                        prescriptionItems = rxItems
                    };
                }).ToList();

                // 2. Toa thuoc
                var toaThuocList = prescriptions.Select(rx =>
                {
                    var rec = records.FirstOrDefault(r => r.MedicalRecordId == rx.MedicalRecordId);
                    int docId = rx.DoctorId > 0 ? rx.DoctorId : (rec?.DoctorId ?? 0);
                    int apptId = rec?.AppointmentId ?? 0;
                    var docName = GetDoctorName(docId);
                    var specName = GetSpecialtyName(docId, apptId);
                    var details = detailsByPrescriptionId.TryGetValue(rx.PrescriptionId, out var dList) ? dList : new List<PrescriptionDetail>();

                    string summary = details.Count > 0
                        ? string.Join(", ", details.Select(d => $"{d.MedicineNameSnapshot} ({d.Quantity} {d.UnitSnapshot})"))
                        : (rx.Note ?? "Đơn thuốc theo chỉ định");

                    var rxItems = details.Select(d => new
                    {
                        name = $"{d.MedicineNameSnapshot} ({d.Quantity} {d.UnitSnapshot})",
                        usage = $"Liều: {d.Dosage}, {d.Frequency}. {d.UsageInstruction}".Trim()
                    }).ToList();

                    string dispensedBy = "DS. Trịnh Mai Phương";
                    string pharmacistNote = "";
                    if (!string.IsNullOrEmpty(rx.Note))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(rx.Note, @"\[(?:Đã phát bởi\s+|Đã cấp phát bởi\s+|Dược sĩ ghi chú:\s+|Dược sĩ:\s+|Dược sĩ\s+)?([^\]:]+)\](?:\s*:\s*(.+))?");
                        if (m.Success)
                        {
                            string rawName = m.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(rawName) && rawName != "Dược sĩ" && rawName != "Dược sĩ ghi chú")
                            {
                                dispensedBy = rawName.StartsWith("DS") || rawName.StartsWith("Dược sĩ") ? rawName : $"DS. {rawName}";
                            }
                            if (m.Groups.Count > 2 && !string.IsNullOrWhiteSpace(m.Groups[2].Value))
                            {
                                pharmacistNote = m.Groups[2].Value.Trim();
                            }
                        }
                        else
                        {
                            pharmacistNote = rx.Note.Trim();
                        }
                    }

                    return new
                    {
                        id = rx.PrescriptionId,
                        prescriptionId = rx.PrescriptionId,
                        medicalRecordId = rx.MedicalRecordId,
                        appointmentId = apptId,
                        code = $"DT-{rx.CreatedAt:yyyyMMdd}-{rx.PrescriptionId:D3}",
                        date = rx.CreatedAt.ToString("dd/MM/yyyy"),
                        patientName = patientName,
                        doctor = docName,
                        specialtyName = specName,
                        clinicKey = specName,
                        items = summary,
                        treatmentPlan = rec?.TreatmentPlan ?? "Uống thuốc đúng giờ, đúng liều theo chỉ dẫn.",
                        prescriptionItems = rxItems,
                        status = rx.Status,
                        note = rx.Note ?? "",
                        pharmacistName = dispensedBy,
                        pharmacistNote = !string.IsNullOrEmpty(pharmacistNote) ? pharmacistNote : "Uống thuốc đúng liều lượng, đúng giờ theo chỉ dẫn. Bảo quản thuốc nơi khô ráo, thoáng mát."
                    };
                }).ToList();

                // 3. Xet nghiem
                var xetNghiemList = medicalTests.Select(t =>
                {
                    var rec = records.FirstOrDefault(r => r.MedicalRecordId == t.MedicalRecordId);
                    int docId = rec?.DoctorId ?? 0;
                    int apptId = rec?.AppointmentId ?? 0;
                    var docName = GetDoctorName(docId);
                    var specName = GetSpecialtyName(docId, apptId);
                    var testDate = t.PerformedAt ?? t.CreatedAt;

                    string resultDisplay = !string.IsNullOrWhiteSpace(t.ResultValue)
                        ? t.ResultValue
                        : (t.ResultStatus == "Pending" ? "Đang chờ kết quả" : (t.ResultStatus == "Normal" ? "Bình thường" : (t.ResultStatus == "Abnormal" ? "Bất thường" : t.ResultStatus)));

                    return new
                    {
                        id = t.TestId,
                        testId = t.TestId,
                        medicalRecordId = t.MedicalRecordId,
                        appointmentId = apptId,
                        code = $"XN-{t.CreatedAt:yyyyMMdd}-{t.TestId:D3}",
                        date = testDate.ToString("dd/MM/yyyy"),
                        type = t.TestName,
                        testType = t.TestType ?? "Laboratory",
                        result = resultDisplay,
                        status = t.ResultStatus,
                        unit = t.Unit ?? "",
                        referenceRange = t.ReferenceRange ?? "",
                        patientName = patientName,
                        doctor = docName,
                        specialtyName = specName,
                        clinicKey = "Xét nghiệm",
                        clinicalNote = t.ClinicalNote ?? "",
                        resultFileUrl = t.ResultFileUrl ?? ""
                    };
                }).ToList();

                // 4. Sieu am
                var sieuAmList = ultrasoundResults.Select(u =>
                {
                    var rec = records.FirstOrDefault(r => r.MedicalRecordId == u.MedicalRecordId);
                    int docId = rec?.DoctorId ?? 0;
                    int apptId = rec?.AppointmentId ?? 0;
                    var docName = GetDoctorName(docId);
                    var specName = GetSpecialtyName(docId, apptId);
                    var usDate = u.PerformedAt ?? u.CreatedAt;

                    string resultDisplay = !string.IsNullOrWhiteSpace(u.Conclusion)
                        ? u.Conclusion
                        : (!string.IsNullOrWhiteSpace(u.Description)
                            ? u.Description
                            : (u.ResultStatus == "Pending" ? "Đang chờ kết quả" : (u.ResultStatus == "Completed" ? "Đã hoàn tất" : u.ResultStatus)));

                    return new
                    {
                        id = u.UltrasoundId,
                        ultrasoundId = u.UltrasoundId,
                        medicalRecordId = u.MedicalRecordId,
                        appointmentId = apptId,
                        code = $"SA-{u.CreatedAt:yyyyMMdd}-{u.UltrasoundId:D3}",
                        date = usDate.ToString("dd/MM/yyyy"),
                        type = u.UltrasoundType ?? "Siêu âm",
                        result = resultDisplay,
                        status = u.ResultStatus,
                        imageUrls = u.ImageUrls ?? Array.Empty<string>(),
                        patientName = patientName,
                        doctor = docName,
                        specialtyName = specName,
                        clinicKey = "Chẩn đoán hình ảnh",
                        clinicalNote = u.ClinicalNote ?? ""
                    };
                }).ToList();

                // 5. Hoa don
                var hoaDonList = invoices.Select(inv =>
                {
                    var rec = records.FirstOrDefault(r => r.AppointmentId == inv.AppointmentId);
                    int docId = rec?.DoctorId ?? 0;
                    var docName = docId > 0 ? GetDoctorName(docId) : "Bác sĩ";
                    var specName = docId > 0 ? GetSpecialtyName(docId, inv.AppointmentId) : "Quầy thu ngân";

                    var items = itemsByInvoiceId.TryGetValue(inv.InvoiceId, out var iList) ? iList : new List<InvoiceItem>();
                    string summary = items.Count > 0
                        ? string.Join(", ", items.Select(i => i.ItemName))
                        : $"Hóa đơn viện phí ({inv.TotalAmount:N0}đ)";

                    var formattedItems = items.Select(i => new
                    {
                        name = i.ItemName,
                        amount = i.Amount
                    }).ToList();

                    return new
                    {
                        id = inv.InvoiceId,
                        invoiceId = inv.InvoiceId,
                        appointmentId = inv.AppointmentId,
                        code = $"HD-{inv.CreatedAt:yyyyMMdd}-{inv.InvoiceId:D3}",
                        date = inv.InvoiceDate.ToString("dd/MM/yyyy"),
                        totalAmount = inv.TotalAmount,
                        paidAmount = inv.PaidAmount,
                        paymentStatus = inv.PaymentStatus,
                        paymentMethod = inv.PaymentMethod ?? "cash",
                        patientName = patientName,
                        doctor = docName,
                        specialtyName = specName,
                        clinicKey = specName,
                        items = summary,
                        invoiceItems = formattedItems
                    };
                }).ToList();

                return Ok(new
                {
                    phieu_kham = phieuKhamList,
                    toa_thuoc = toaThuocList,
                    xet_nghiem = xetNghiemList,
                    sieu_am = sieuAmList,
                    hoa_don = hoaDonList
                });
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException != null ? $"{ex.Message} --> {ex.InnerException.Message}" : ex.Message;
                if (ex.InnerException?.InnerException != null) msg += $" --> {ex.InnerException.InnerException.Message}";
                return StatusCode(500, new { message = msg });
            }
        }

        // GET /api/MedicalRecords/appointment/{appointmentId}
        [HttpGet("appointment/{appointmentId}")]
        public async Task<IActionResult> GetMedicalRecordByAppointment(int appointmentId)
        {
            try
            {
                var record = await _context.MedicalRecords
                    .FirstOrDefaultAsync(r => r.AppointmentId == appointmentId);

                if (record == null)
                {
                    return NotFound(new { message = "KhÃ´ng tÃ¬m tháº¥y há»“ sÆ¡ bá»‡nh Ã¡n." });
                }

                var prescriptions = await _context.Prescriptions
                    .Where(p => p.MedicalRecordId == record.MedicalRecordId)
                    .ToListAsync();

                var pIds = prescriptions.Select(p => p.PrescriptionId).ToList();
                var prescriptionDetails = await _context.PrescriptionDetails
                    .Where(d => pIds.Contains(d.PrescriptionId))
                    .ToListAsync();

                return Ok(new
                {
                    medicalRecord = record,
                    prescriptions = prescriptions,
                    prescriptionDetails = prescriptionDetails
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // POST /api/MedicalRecords
        [HttpPost]
        public async Task<IActionResult> CreateMedicalRecord([FromBody] CreateMedicalRecordDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Dá»¯ liá»‡u khÃ´ng há»£p lá»‡." });
            }

            try
            {
                var record = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == dto.AppointmentId);
                if (record == null)
                {
                    record = new MedicalRecord
                    {
                        AppointmentId = dto.AppointmentId,
                        PatientId = dto.PatientId,
                        DoctorId = dto.DoctorId,
                        ExaminationDate = DateTime.UtcNow,
                        Status = "Completed",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.MedicalRecords.Add(record);
                }

                record.Symptoms = dto.Symptoms;
                record.Diagnosis = dto.Diagnosis;
                record.IcdCode = dto.IcdCode;
                record.IcdDescription = dto.IcdDescription;
                record.TreatmentPlan = dto.TreatmentPlan;
                record.UpdatedAt = DateTime.UtcNow;

                if (!string.IsNullOrWhiteSpace(dto.Pulse) && int.TryParse(dto.Pulse, out var p)) record.HeartRate = p;
                if (!string.IsNullOrWhiteSpace(dto.BloodPressure)) record.BloodPressure = dto.BloodPressure;
                if (!string.IsNullOrWhiteSpace(dto.Temperature) && decimal.TryParse(dto.Temperature, out var t)) record.Temperature = t;
                if (!string.IsNullOrWhiteSpace(dto.Weight) && decimal.TryParse(dto.Weight, out var w)) record.Weight = w;
                if (!string.IsNullOrWhiteSpace(dto.Height) && decimal.TryParse(dto.Height, out var h)) record.Height = h;

                if (record.Weight.HasValue && record.Height.HasValue && record.Height.Value > 0)
                {
                    decimal hMeter = record.Height.Value / 100m;
                    record.Bmi = Math.Round(record.Weight.Value / (hMeter * hMeter), 1);
                }

                await _context.SaveChangesAsync();

                // 2. Táº¡o Prescription
                var insufficientStock = new List<string>();
                if (dto.Prescriptions != null && dto.Prescriptions.Count > 0)
                {
                    var prescription = new Prescription
                    {
                        MedicalRecordId = record.MedicalRecordId,
                        DoctorId = dto.DoctorId,
                        PatientId = dto.PatientId,
                        Status = "Active",
                        // [Old code]: Note = dto.TreatmentPlan,
                        // [New code - Cột Note của đơn thuốc chỉ dùng riêng cho Ghi chú của Dược sĩ]:
                        Note = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.Prescriptions.Add(prescription);
                    await _context.SaveChangesAsync();

                    foreach (var pItem in dto.Prescriptions)
                    {
                        _context.PrescriptionDetails.Add(new PrescriptionDetail
                        {
                            PrescriptionId = prescription.PrescriptionId,
                            MedicineId = pItem.MedicineId,
                            MedicineNameSnapshot = pItem.MedicineName,
                            UnitSnapshot = pItem.Unit,
                            Quantity = pItem.Quantity,
                            Dosage = pItem.Dosage,
                            Frequency = pItem.Frequency,
                            Duration = "7 ngày",
                            UsageInstruction = pItem.UsageInstruction
                        });

                        var med = await _context.Medicines.FirstOrDefaultAsync(m => m.MedicineId == pItem.MedicineId);
                        if (med != null)
                        {
                            if (med.StockQuantity >= pItem.Quantity)
                            {
                                med.StockQuantity -= pItem.Quantity;
                            }
                            else
                            {
                                insufficientStock.Add(med.MedicineName);
                            }
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                // 3. Update appointment status: Náº¿u cÃ³ kÃª Ä‘Æ¡n thuá»‘c -> chuyá»ƒn sang StatusId = 10 (PendingDispensing) Ä‘á»ƒ Dược sĩ phÃ¡t thuá»‘c
                var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == dto.AppointmentId);
                if (appt != null)
                {
                    // [Old code]: appt.StatusId = 4; // 4 = Completed
                    // [New code]:
                    appt.StatusId = (dto.Prescriptions != null && dto.Prescriptions.Count > 0) ? 10 : 4;
                    appt.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                return Ok(new
                {
                    success = true,
                    medicalRecordId = record.MedicalRecordId,
                    insufficientStock = insufficientStock
                });
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException != null ? $"{ex.Message} --> {ex.InnerException.Message}" : ex.Message;
                if (ex.InnerException?.InnerException != null) msg += $" --> {ex.InnerException.InnerException.Message}";
                return StatusCode(500, new { message = msg });
            }
        }

        // =========================================================================
        // â”€â”€ PHÃ‚N Há»† DÆ¯á»¢C SÄ¨ (PHARMACY DISPENSING) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // =========================================================================

        // =========================================================================
        // [Old code - GetPharmacyQueue khÃ´ng lá»c ngÃ y hÃ´m nay]:
        // [HttpGet("pharmacy-queue")]
        // public async Task<IActionResult> GetPharmacyQueue()
        // {
        //     var appointments = await _context.Appointments.Where(a => a.StatusId == 10).OrderBy(a => a.CreatedAt).ToListAsync();
        //     ...
        // }
        // =========================================================================

        // [New code - GET /api/MedicalRecords/pharmacy-queue: Tối ưu hóa truy vấn hàng loạt (Bulk Query) siêu tốc]:
        [HttpGet("pharmacy-queue")]
        public async Task<IActionResult> GetPharmacyQueue([FromQuery] bool todayOnly = true)
        {
            try
            {
                var query = _context.Appointments
                    .AsNoTracking()
                    .Where(a => a.StatusId == 10 || a.StatusId == 3);

                if (todayOnly)
                {
                    var nowVn        = DateTime.UtcNow.AddHours(7);
                    var todayVn      = DateOnly.FromDateTime(nowVn);
                    var todayVnStart = nowVn.Date.AddHours(-7);
                    var todayVnEnd   = todayVnStart.AddDays(1);
                    query = query.Where(a =>
                        (a.AppointmentDate != null && a.AppointmentDate == todayVn) ||
                        (a.AppointmentDate == null  && a.CreatedAt >= todayVnStart && a.CreatedAt < todayVnEnd));
                }

                var appointments = await query
                    .OrderBy(a => a.CreatedAt)
                    .ToListAsync();

                if (appointments.Count == 0) return Ok(new List<object>());

                var apptIds = appointments.Select(a => a.AppointmentId).ToList();
                var patientIds = appointments.Select(a => a.PatientId).Distinct().ToList();
                var doctorIds = appointments.Select(a => a.DoctorId).Distinct().ToList();

                var records = await _context.MedicalRecords.AsNoTracking().Where(r => apptIds.Contains(r.AppointmentId)).ToListAsync();
                var recordMap = records.ToDictionary(r => r.AppointmentId, r => r);
                var recordIds = records.Select(r => r.MedicalRecordId).ToList();

                var prescriptions = await _context.Prescriptions.AsNoTracking().Where(p => recordIds.Contains(p.MedicalRecordId)).ToListAsync();
                var rxMap = prescriptions.ToDictionary(p => p.MedicalRecordId, p => p);
                var rxIds = prescriptions.Select(p => p.PrescriptionId).ToList();

                var allDetails = await _context.PrescriptionDetails.AsNoTracking().Where(d => rxIds.Contains(d.PrescriptionId)).ToListAsync();
                var detailsGroup = allDetails.GroupBy(d => d.PrescriptionId).ToDictionary(g => g.Key, g => g.ToList());

                var docList = await _context.Doctors.AsNoTracking().Where(d => doctorIds.Contains(d.DoctorId)).ToListAsync();
                var docMap = docList.ToDictionary(d => d.DoctorId, d => d.FullName ?? "Bác sĩ");
                var docDegreeMap = docList.ToDictionary(d => d.DoctorId, d => d.Degree ?? "Bác sĩ");

                var patientList = await _context.Patients.AsNoTracking().Where(p => patientIds.Contains(p.PatientId)).ToListAsync();
                var patientMap = patientList.ToDictionary(p => p.PatientId, p => p);

                var medList = await _context.Medicines.AsNoTracking().ToListAsync();
                var medDict = medList.ToDictionary(m => m.MedicineId, m => m);

                var result = new List<object>();

                foreach (var appt in appointments)
                {
                    if (!recordMap.TryGetValue(appt.AppointmentId, out var record)) continue;
                    if (!rxMap.TryGetValue(record.MedicalRecordId, out var rx)) continue;

                    detailsGroup.TryGetValue(rx.PrescriptionId, out var details);
                    if (details == null) details = new List<PrescriptionDetail>();

                    patientMap.TryGetValue(appt.PatientId, out var pInfo);
                    string pName = pInfo?.FullName ?? "Bệnh nhân";
                    int age = pInfo?.DateOfBirth.HasValue == true ? DateTime.Today.Year - pInfo.DateOfBirth.Value.Year : 30;
                    string gender = pInfo?.Gender ?? "Nam";
                    string dName = docMap.ContainsKey(appt.DoctorId) ? docMap[appt.DoctorId] : "BS. Nguyễn Văn A";
                    string dDegree = docDegreeMap.ContainsKey(appt.DoctorId) ? docDegreeMap[appt.DoctorId] : "Bác sĩ";

                    var drugItems = details.Select(d =>
                    {
                        int stock = medDict.ContainsKey(d.MedicineId) ? medDict[d.MedicineId].StockQuantity : 100;
                        return new
                        {
                            prescriptionDetailId = d.PrescriptionDetailId,
                            medicineId = d.MedicineId,
                            medicineName = d.MedicineNameSnapshot,
                            unit = d.UnitSnapshot,
                            quantity = d.Quantity,
                            dosage = d.Dosage,
                            frequency = d.Frequency,
                            duration = d.Duration,
                            usageInstruction = d.UsageInstruction,
                            stockQuantity = stock,
                            stockStatus = stock >= d.Quantity ? "Khả dụng" : "Hết hàng"
                        };
                    }).ToList();

                    result.Add(new
                    {
                        appointmentId = appt.AppointmentId,
                        medicalRecordId = record.MedicalRecordId,
                        prescriptionId = rx.PrescriptionId,
                        patientId = appt.PatientId,
                        patientName = pName,
                        patientAge = age,
                        patientGender = gender,
                        doctorName = dName,
                        doctorDegree = dDegree,
                        diagnosis = record.Diagnosis ?? "",
                        symptoms = record.Symptoms ?? "",
                        prescriptionNote = rx.Note ?? "",
                        createdAt = rx.CreatedAt,
                        timeSlot = !string.IsNullOrEmpty(appt.Reason) && appt.Reason.Contains(":") ? appt.Reason : "14:30 - 15:00",
                        items = drugItems
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException != null ? $"{ex.Message} --> {ex.InnerException.Message}" : ex.Message;
                if (ex.InnerException?.InnerException != null) msg += $" --> {ex.InnerException.InnerException.Message}";
                return StatusCode(500, new { message = msg });
            }
        }

        // =========================================================================
        // [Old code - GetPharmacyHistory khÃ´ng lá»c tÃ¬m kiáº¿m vÃ  khÃ´ng tráº£ vá» items chi tiáº¿t]:
        // [HttpGet("pharmacy-history")]
        // public async Task<IActionResult> GetPharmacyHistory([FromQuery] string? date) { ... }
        // =========================================================================

        // [New code - GET /api/MedicalRecords/pharmacy-history: Tìm kiếm thông minh theo SĐT, Họ tên, Mã đơn, Bác sĩ, Triệu chứng...]:
        [HttpGet("pharmacy-history")]
        public async Task<IActionResult> GetPharmacyHistory([FromQuery] string? date, [FromQuery] string? search)
        {
            try
            {
                var query = _context.Prescriptions.AsNoTracking().Where(p => p.Status == "Completed" || p.Status == "Dispensed");

                // Nếu có từ khóa tìm kiếm và không chọn "all", nếu tìm kiếm theo ngày không thấy thì sẽ mở rộng tìm kiếm
                if (!string.IsNullOrEmpty(date) && date != "all")
                {
                    if (date == "today")
                    {
                        var nowVn        = DateTime.UtcNow.AddHours(7);
                        var todayVnStart = nowVn.Date.AddHours(-7);
                        var todayVnEnd   = todayVnStart.AddDays(1);
                        query = query.Where(p => p.UpdatedAt >= todayVnStart && p.UpdatedAt < todayVnEnd);
                    }
                    else if (DateTime.TryParse(date, out var parsedDate))
                    {
                        var startUtc = parsedDate.Date.AddHours(-7);
                        var endUtc   = startUtc.AddDays(1);
                        query = query.Where(p => p.UpdatedAt >= startUtc && p.UpdatedAt < endUtc);
                    }
                }

                var rxList = await query
                    .OrderByDescending(p => p.UpdatedAt)
                    .ToListAsync();

                // Nếu tìm kiếm theo ngày cụ thể mà không có kết quả, tự động tìm kiếm trên toàn bộ lịch sử nếu người dùng có nhập từ khóa
                if (rxList.Count == 0 && !string.IsNullOrWhiteSpace(search) && date != "all")
                {
                    rxList = await _context.Prescriptions.AsNoTracking()
                        .Where(p => p.Status == "Completed" || p.Status == "Dispensed")
                        .OrderByDescending(p => p.UpdatedAt)
                        .ToListAsync();
                }

                if (rxList.Count == 0) return Ok(new List<object>());

                var rxIds = rxList.Select(r => r.PrescriptionId).ToList();
                var recordIds = rxList.Select(r => r.MedicalRecordId).Distinct().ToList();
                var patientIds = rxList.Select(r => r.PatientId).Distinct().ToList();
                var doctorIds = rxList.Select(r => r.DoctorId).Distinct().ToList();

                var records = await _context.MedicalRecords.AsNoTracking().Where(r => recordIds.Contains(r.MedicalRecordId)).ToListAsync();
                var recordMap = records.ToDictionary(r => r.MedicalRecordId, r => r);

                var allDetails = await _context.PrescriptionDetails.AsNoTracking().Where(d => rxIds.Contains(d.PrescriptionId)).ToListAsync();
                var detailsGroup = allDetails.GroupBy(d => d.PrescriptionId).ToDictionary(g => g.Key, g => g.ToList());

                var docList = await _context.Doctors.AsNoTracking().Where(d => doctorIds.Contains(d.DoctorId)).ToListAsync();
                var docMap = docList.ToDictionary(d => d.DoctorId, d => d.FullName ?? "Bác sĩ");
                var docDegreeMap = docList.ToDictionary(d => d.DoctorId, d => d.Degree ?? "Bác sĩ");

                var patientList = await _context.Patients.AsNoTracking().Where(p => patientIds.Contains(p.PatientId)).ToListAsync();
                var patientMap = patientList.ToDictionary(p => p.PatientId, p => p);

                var userList = await _context.Users.AsNoTracking().ToListAsync();
                var userPhoneMap = userList.ToDictionary(u => u.UserId, u => u.PhoneNumber ?? "");

                var medList = await _context.Medicines.AsNoTracking().ToListAsync();
                var medDict = medList.ToDictionary(m => m.MedicineId, m => m);

                var result = new List<object>();

                foreach (var rx in rxList)
                {
                    recordMap.TryGetValue(rx.MedicalRecordId, out var record);
                    detailsGroup.TryGetValue(rx.PrescriptionId, out var details);
                    if (details == null) details = new List<PrescriptionDetail>();

                    patientMap.TryGetValue(rx.PatientId, out var pInfo);
                    string pName = pInfo?.FullName ?? "Bệnh nhân";
                    string pPhone = !string.IsNullOrEmpty(pInfo?.PhoneNumber) ? pInfo.PhoneNumber : (pInfo != null && userPhoneMap.ContainsKey(pInfo.UserId) ? userPhoneMap[pInfo.UserId] : "");
                    string pCccd = pInfo?.CccdNumber ?? "";
                    string pInsurance = pInfo?.HealthInsuranceNumber ?? "";
                    int age = pInfo?.DateOfBirth.HasValue == true ? DateTime.Today.Year - pInfo.DateOfBirth.Value.Year : 30;
                    string gender = pInfo?.Gender ?? "Nam";
                    string dName = docMap.ContainsKey(rx.DoctorId) ? docMap[rx.DoctorId] : "BS. Nguyễn Văn A";
                    string dDegree = docDegreeMap.ContainsKey(rx.DoctorId) ? docDegreeMap[rx.DoctorId] : "Bác sĩ";
                    string summary = string.Join(", ", details.Select(d => $"{d.MedicineNameSnapshot} ({d.Quantity} {d.UnitSnapshot})"));
                    string rxCode = $"RX-2026-{rx.PrescriptionId:D4}";

                    string dispensedBy = "DS. Trịnh Mai Phương";
                    if (!string.IsNullOrEmpty(rx.Note))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(rx.Note, @"\[(?:Đã phát bởi\s+|Đã cấp phát bởi\s+|Dược sĩ ghi chú:\s+|Dược sĩ:\s+|Dược sĩ\s+)?([^\]:]+)\]");
                        if (m.Success)
                        {
                            string raw = m.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(raw) && raw != "Dược sĩ" && raw != "Dược sĩ ghi chú")
                            {
                                dispensedBy = raw.StartsWith("DS") || raw.StartsWith("Dược sĩ") ? raw : $"DS. {raw}";
                            }
                        }
                    }

                    var drugItems = details.Select(d =>
                    {
                        int stock = medDict.ContainsKey(d.MedicineId) ? medDict[d.MedicineId].StockQuantity : 100;
                        return new
                        {
                            prescriptionDetailId = d.PrescriptionDetailId,
                            medicineId = d.MedicineId,
                            medicineName = d.MedicineNameSnapshot,
                            unit = d.UnitSnapshot,
                            quantity = d.Quantity,
                            dosage = d.Dosage,
                            frequency = d.Frequency,
                            duration = d.Duration,
                            usageInstruction = d.UsageInstruction,
                            stockQuantity = stock,
                            stockStatus = stock >= d.Quantity ? "Khả dụng" : "Hết hàng"
                        };
                    }).ToList();

                    result.Add(new
                    {
                        prescriptionId = rx.PrescriptionId,
                        prescriptionCode = rxCode,
                        medicalRecordId = rx.MedicalRecordId,
                        appointmentId = record?.AppointmentId ?? 0,
                        patientId = rx.PatientId,
                        patientName = pName,
                        patientPhone = pPhone,
                        patientCccd = pCccd,
                        patientInsurance = pInsurance,
                        patientAge = age,
                        patientGender = gender,
                        doctorName = dName,
                        doctorDegree = dDegree,
                        diagnosis = record?.Diagnosis ?? "",
                        symptoms = record?.Symptoms ?? "",
                        dispensedAt = rx.UpdatedAt,
                        dispensedByName = dispensedBy,
                        note = rx.Note ?? "",
                        prescriptionNote = rx.Note ?? "",
                        itemCount = details.Count,
                        summary = summary,
                        items = drugItems
                    });
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s = search.Trim().ToLower();
                    result = result.Where(r => {
                        dynamic item = r;
                        string pn = ((string)item.patientName).ToLower();
                        string ph = ((string)item.patientPhone).ToLower();
                        string cd = ((string)item.patientCccd).ToLower();
                        string ins = ((string)item.patientInsurance).ToLower();
                        string dn = ((string)item.doctorName).ToLower();
                        string diag = ((string)item.diagnosis).ToLower();
                        string sym = ((string)item.symptoms).ToLower();
                        string sm = ((string)item.summary).ToLower();
                        string nt = ((string)item.note).ToLower();
                        string disp = ((string)item.dispensedByName).ToLower();
                        string apId = item.appointmentId.ToString();
                        string rxId = item.prescriptionId.ToString();
                        string code = ((string)item.prescriptionCode).ToLower();
                        return pn.Contains(s) || ph.Contains(s) || cd.Contains(s) || ins.Contains(s) ||
                               dn.Contains(s) || diag.Contains(s) || sym.Contains(s) || sm.Contains(s) ||
                               nt.Contains(s) || disp.Contains(s) || apId.Contains(s) || rxId.Contains(s) || code.Contains(s);
                    }).ToList();
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException != null ? $"{ex.Message} --> {ex.InnerException.Message}" : ex.Message;
                if (ex.InnerException?.InnerException != null) msg += $" --> {ex.InnerException.InnerException.Message}";
                return StatusCode(500, new { message = msg });
            }
        }

            // GET /api/MedicalRecords/fix-rx43-note
    [HttpGet("fix-rx43-note")]
    public async Task<IActionResult> FixRx43Note()
    {
        try
        {
            var rx = await _context.Prescriptions.FirstOrDefaultAsync(p => p.PrescriptionId == 43);
            if (rx != null)
            {
                rx.Note = "Nghá»‰ ngÆ¡i | [Dược sĩ]: Ä‚n uá»‘ng Ä‘iá»u Ä‘á»™, uá»‘ng thuá»‘c Ä‘áº§y Ä‘á»§";
                rx.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // POST /api/MedicalRecords/{appointmentId}/dispense
        [HttpPost("{appointmentId}/dispense")]
        public async Task<IActionResult> DispensePrescription(int appointmentId, [FromBody] DispenseDto dto)
        {
            try
            {
                var record = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == appointmentId);
                if (record == null)
                {
                    return NotFound(new { success = false, message = "KhÃ´ng tÃ¬m tháº¥y há»“ sÆ¡ bá»‡nh Ã¡n cho ca khÃ¡m nÃ y." });
                }

                var rx = await _context.Prescriptions.FirstOrDefaultAsync(p => p.MedicalRecordId == record.MedicalRecordId);
                if (rx == null)
                {
                    return NotFound(new { success = false, message = "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n thuá»‘c cho ca khÃ¡m nÃ y." });
                }

                // Cáº­p nháº­t tráº¡ng thÃ¡i Ä‘Æ¡n thuá»‘c
                // [Old code]: rx.Status = "Dispensed";
                // [New code - GÃ¡n Completed phÃ¹ há»£p vá»›i PostgreSQL prescriptions_status_check]:
                rx.Status = "Completed";
                string pName = !string.IsNullOrWhiteSpace(dto?.PharmacistName) 
                    ? dto.PharmacistName.Trim() 
                    : "DS. Trịnh Mai Phương";

                if (!string.IsNullOrWhiteSpace(dto?.PharmacistNote))
                {
                    rx.Note = string.IsNullOrEmpty(rx.Note)
                        ? $"[{pName}]: {dto.PharmacistNote.Trim()}"
                        : $"{rx.Note} | [{pName}]: {dto.PharmacistNote.Trim()}";
                }
                else
                {
                    rx.Note = string.IsNullOrEmpty(rx.Note)
                        ? $"[Đã phát bởi {pName}]"
                        : $"{rx.Note} | [Đã phát bởi {pName}]";
                }
                rx.UpdatedAt = DateTime.UtcNow;

                // Cập nhật cuộc hẹn sang StatusId = 4 (Completed)
                var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
                if (appt != null)
                {
                    appt.StatusId = 4; // 4 = Completed
                    appt.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Xác nhận phát thuốc thành công!",
                    prescriptionId = rx.PrescriptionId
                });
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException != null ? $"{ex.Message} --> {ex.InnerException.Message}" : ex.Message;
                if (ex.InnerException?.InnerException != null) msg += $" --> {ex.InnerException.InnerException.Message}";
                return StatusCode(500, new { success = false, message = msg });
            }
        }
        // GET /api/MedicalRecords/icd10?specialtyId={specialtyId}&search={search}
        [HttpGet("icd10")]
        public async Task<IActionResult> GetIcd10Catalog([FromQuery] int? specialtyId, [FromQuery] string? search)
        {
            try
            {
                var query = _context.Icd10Catalogs.AsQueryable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s = search.Trim().ToLower();
                    query = query.Where(x => x.IcdCode.ToLower().Contains(s) || x.DiseaseName.ToLower().Contains(s));
                }

                var list = await query.ToListAsync();

                if (list != null && list.Count > 0)
                {
                    var orderedList = list
                        .OrderByDescending(x => specialtyId.HasValue && x.SpecialtyId == specialtyId.Value)
                        .ThenByDescending(x => x.IsCommon)
                        .ThenBy(x => x.IcdCode)
                        .Select(x => new
                        {
                            icdCode = x.IcdCode,
                            diseaseName = x.DiseaseName,
                            chapterName = x.ChapterName,
                            isCommon = x.IsCommon,
                            specialtyId = x.SpecialtyId,
                            isPreferred = specialtyId.HasValue && x.SpecialtyId == specialtyId.Value
                        })
                        .ToList();

                    return Ok(new
                    {
                        success = true,
                        total = orderedList.Count,
                        items = orderedList
                    });
                }

                // Danh sÃ¡ch dá»± phÃ²ng náº¿u báº£ng cÆ¡ sá»Ÿ dá»¯ liá»‡u chÆ°a náº¡p Ä‘á»§ mÃ£
                var fallbacks = new List<dynamic>
                {
                    // Da liá»…u (SpecialtyId = 4 / Da liá»…u)
                    new { icdCode = "L20", diseaseName = "ViÃªm da cÆ¡ Ä‘á»‹a (Atopic Dermatitis)", chapterName = "Bá»‡nh da vÃ  mÃ´ dÆ°á»›i da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },
                    new { icdCode = "L70", diseaseName = "Má»¥n trá»©ng cÃ¡ (Acne Vulgaris)", chapterName = "Bá»‡nh da vÃ  mÃ´ dÆ°á»›i da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },
                    new { icdCode = "L30", diseaseName = "ViÃªm da khÃ¡c (Eczema)", chapterName = "Bá»‡nh da vÃ  mÃ´ dÆ°á»›i da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },
                    new { icdCode = "L50", diseaseName = "MÃ y Ä‘ay (Urticaria)", chapterName = "Bá»‡nh da vÃ  mÃ´ dÆ°á»›i da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },
                    new { icdCode = "B35", diseaseName = "Bá»‡nh náº¥m da (Dermatophytosis)", chapterName = "Bá»‡nh nhiá»…m trÃ¹ng da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },
                    new { icdCode = "B02", diseaseName = "Bá»‡nh Zona (Herpes zoster)", chapterName = "Bá»‡nh nhiá»…m virus da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },
                    new { icdCode = "L40", diseaseName = "Bá»‡nh váº£y náº¿n (Psoriasis)", chapterName = "Bá»‡nh da vÃ  mÃ´ dÆ°á»›i da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },
                    new { icdCode = "L23", diseaseName = "ViÃªm da tiáº¿p xÃºc dá»‹ á»©ng", chapterName = "Bá»‡nh da vÃ  mÃ´ dÆ°á»›i da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },
                    new { icdCode = "L80", diseaseName = "Bá»‡nh báº¡ch biáº¿n (Vitiligo)", chapterName = "Bá»‡nh da vÃ  mÃ´ dÆ°á»›i da", isCommon = true, specialtyId = 4, isPreferred = specialtyId == 4 },

                    // HÃ´ háº¥p / Tai mÅ©i há»ng (SpecialtyId = 1)
                    new { icdCode = "J00", diseaseName = "ViÃªm mÅ©i há»ng cáº¥p tÃ­nh (Cáº£m láº¡nh)", chapterName = "Bá»‡nh há»‡ hÃ´ háº¥p", isCommon = true, specialtyId = 1, isPreferred = specialtyId == 1 },
                    new { icdCode = "J02", diseaseName = "ViÃªm há»ng cáº¥p", chapterName = "Bá»‡nh há»‡ hÃ´ háº¥p", isCommon = true, specialtyId = 1, isPreferred = specialtyId == 1 },
                    new { icdCode = "J03", diseaseName = "ViÃªm amiÄ‘an cáº¥p", chapterName = "Bá»‡nh há»‡ hÃ´ háº¥p", isCommon = true, specialtyId = 1, isPreferred = specialtyId == 1 },
                    new { icdCode = "J20", diseaseName = "ViÃªm pháº¿ quáº£n cáº¥p", chapterName = "Bá»‡nh há»‡ hÃ´ háº¥p", isCommon = true, specialtyId = 1, isPreferred = specialtyId == 1 },
                    new { icdCode = "J01", diseaseName = "ViÃªm xoang cáº¥p", chapterName = "Bá»‡nh há»‡ hÃ´ háº¥p", isCommon = true, specialtyId = 1, isPreferred = specialtyId == 1 },
                    new { icdCode = "J45", diseaseName = "Hen pháº¿ quáº£n (Suyá»…n)", chapterName = "Bá»‡nh há»‡ hÃ´ háº¥p", isCommon = true, specialtyId = 1, isPreferred = specialtyId == 1 },

                    // Tim máº¡ch / Ná»™i tá»•ng quÃ¡t (SpecialtyId = 2)
                    new { icdCode = "I10", diseaseName = "TÄƒng huyáº¿t Ã¡p vÃ´ cÄƒn (nguyÃªn phÃ¡t)", chapterName = "Bá»‡nh há»‡ tuáº§n hoÃ n", isCommon = true, specialtyId = 2, isPreferred = specialtyId == 2 },
                    new { icdCode = "E11", diseaseName = "ÄÃ¡i thÃ¡o Ä‘Æ°á»ng Type 2", chapterName = "Bá»‡nh ná»™i tiáº¿t, chuyá»ƒn hÃ³a", isCommon = true, specialtyId = 2, isPreferred = specialtyId == 2 },
                    new { icdCode = "E78", diseaseName = "Rá»‘i loáº¡n chuyá»ƒn hÃ³a lipoprotein (Má»¡ mÃ¡u)", chapterName = "Bá»‡nh ná»™i tiáº¿t, chuyá»ƒn hÃ³a", isCommon = true, specialtyId = 2, isPreferred = specialtyId == 2 },
                    
                    // TiÃªu hÃ³a (SpecialtyId = 3)
                    new { icdCode = "K21", diseaseName = "TrÃ o ngÆ°á»£c dáº¡ dÃ y - thá»±c quáº£n (GERD)", chapterName = "Bá»‡nh há»‡ tiÃªu hÃ³a", isCommon = true, specialtyId = 3, isPreferred = specialtyId == 3 },
                    new { icdCode = "K29", diseaseName = "ViÃªm dáº¡ dÃ y vÃ  tÃ¡ trÃ ng", chapterName = "Bá»‡nh há»‡ tiÃªu hÃ³a", isCommon = true, specialtyId = 3, isPreferred = specialtyId == 3 },
                    new { icdCode = "K58", diseaseName = "Há»™i chá»©ng ruá»™t kÃ­ch thÃ­ch (IBS)", chapterName = "Bá»‡nh há»‡ tiÃªu hÃ³a", isCommon = true, specialtyId = 3, isPreferred = specialtyId == 3 },

                    // CÆ¡ xÆ°Æ¡ng khá»›p (SpecialtyId = 5)
                    new { icdCode = "M54.5", diseaseName = "Äau lÆ°ng dÆ°á»›i (Tháº¯t lÆ°ng)", chapterName = "Bá»‡nh há»‡ cÆ¡ xÆ°Æ¡ng khá»›p", isCommon = true, specialtyId = 5, isPreferred = specialtyId == 5 },
                    new { icdCode = "M17", diseaseName = "ThoÃ¡i hÃ³a khá»›p gá»‘i", chapterName = "Bá»‡nh há»‡ cÆ¡ xÆ°Æ¡ng khá»›p", isCommon = true, specialtyId = 5, isPreferred = specialtyId == 5 },
                    new { icdCode = "M10", diseaseName = "Bá»‡nh GÃºt (Gout)", chapterName = "Bá»‡nh há»‡ cÆ¡ xÆ°Æ¡ng khá»›p", isCommon = true, specialtyId = 5, isPreferred = specialtyId == 5 },

                    // Triá»‡u chá»©ng chung
                    new { icdCode = "R50", diseaseName = "Sá»‘t khÃ´ng rÃµ nguyÃªn nhÃ¢n", chapterName = "Triá»‡u chá»©ng chung", isCommon = true, specialtyId = (int?)null, isPreferred = false },
                    new { icdCode = "R51", diseaseName = "Äau Ä‘áº§u", chapterName = "Triá»‡u chá»©ng chung", isCommon = true, specialtyId = (int?)null, isPreferred = false },
                    new { icdCode = "R10", diseaseName = "Äau bá»¥ng vÃ  vÃ¹ng cháº­u", chapterName = "Triá»‡u chá»©ng chung", isCommon = true, specialtyId = (int?)null, isPreferred = false }
                };

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s = search.Trim().ToLower();
                    fallbacks = fallbacks.Where(x => ((string)x.icdCode).ToLower().Contains(s) || ((string)x.diseaseName).ToLower().Contains(s)).ToList();
                }

                var sortedFallbacks = fallbacks
                    .OrderByDescending(x => specialtyId.HasValue && x.specialtyId == specialtyId.Value)
                    .ThenByDescending(x => (bool)x.isCommon)
                    .ThenBy(x => (string)x.icdCode)
                    .ToList();

                return Ok(new
                {
                    success = true,
                    total = sortedFallbacks.Count,
                    items = sortedFallbacks
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
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
        public string? IcdCode { get; set; }
        public string? IcdDescription { get; set; }
        public string TreatmentPlan { get; set; } = string.Empty;
        public List<PrescribedDrugDto> Prescriptions { get; set; } = new();
    }

    public class PrescribedDrugDto
    {
        public int MedicineId { get; set; }
        public string MedicineName { get; set; } = string.Empty;
        public string Unit { get; set; } = "ViÃªn";
        public int Quantity { get; set; } = 10;
        public string Dosage { get; set; } = "500mg";
        public string Frequency { get; set; } = "2 lần/ngày";
        public string UsageInstruction { get; set; } = "Uá»‘ng sau Äƒn 30 phÃºt";
    }

    public class DispenseDto
    {
        public Guid? PharmacistUserId { get; set; }
        public string? PharmacistName { get; set; }
        public string? PharmacistNote { get; set; }
    }
}