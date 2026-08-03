using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;
using System.Linq;

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

    // GET /api/medicalrecords/icd10?specialtyId=X&search=Y
    // Trả về danh mục ICD-10 — nếu có specialtyId, mã thuộc đúng chuyên khoa của bác sĩ được xếp
    // lên đầu danh sách (gợi ý thông minh), nhưng vẫn trả về đủ toàn bộ để bác sĩ tìm mã khác nếu cần.
    [HttpGet("icd10")]
    public async Task<IActionResult> GetIcd10Catalog([FromQuery] int? specialtyId, [FromQuery] string? search)
    {
        try
        {
            var query = _context.Icd10Catalogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim().ToLower();
                query = query.Where(c => c.IcdCode.ToLower().Contains(s) || c.DiseaseName.ToLower().Contains(s));
            }

            var all = await query.ToListAsync();

            var ordered = specialtyId.HasValue && specialtyId.Value > 0
                ? all.OrderByDescending(c => c.SpecialtyId == specialtyId.Value)
                     .ThenBy(c => c.DiseaseName)
                     .ToList()
                : all.OrderBy(c => c.DiseaseName).ToList();

            var result = ordered.Select(c => new
            {
                icdCode = c.IcdCode,
                diseaseName = c.DiseaseName,
                chapterName = c.ChapterName,
                isCommon = c.IsCommon,
                specialtyId = c.SpecialtyId,
                matchesSpecialty = specialtyId.HasValue && c.SpecialtyId == specialtyId.Value
            });

            return Ok(new { success = true, items = result });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // GET /api/medicalrecords/patient/{patientId}
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientMedicalRecords(int patientId)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, patientId)) return this.ForbidJson();
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
                    // Chưa có kết quả (KTV chưa thực hiện) — KHÔNG được ngầm định là "Bình thường",
                    // dễ gây hiểu lầm cho bệnh nhân là đã có kết quả tốt trong khi thực ra chưa làm.
                    string testResult = t.ResultStatus == "Pending" ? "Đang chờ kết quả" : (t.ResultValue ?? "Bình thường");
                    xetNghiem.Add(new
                    {
                        id = t.TestId,
                        date = r.ExaminationDate.ToString("dd/MM/yyyy"),
                        clinicKey = clinicKey,
                        type = t.TestName,
                        result = testResult,
                        status = t.ResultStatus,
                        code = $"XN-{r.ExaminationDate:yyyyMMdd}-{t.TestId:D2}"
                    });
                }

                // Load ultrasound for this record
                var uls = await _context.UltrasoundResults.Where(u => u.MedicalRecordId == r.MedicalRecordId).ToListAsync();
                foreach (var u in uls)
                {
                    string ulsResult = u.ResultStatus == "Pending" ? "Đang chờ kết quả" : (u.Conclusion ?? "Bình thường");
                    sieuAm.Add(new
                    {
                        id = u.UltrasoundId,
                        date = (u.PerformedAt ?? r.ExaminationDate).ToString("dd/MM/yyyy"),
                        clinicKey = clinicKey,
                        type = u.UltrasoundType ?? "Siêu âm tổng quát",
                        result = ulsResult,
                        status = u.ResultStatus,
                        imageUrls = u.ImageUrls ?? Array.Empty<string>(),
                        code = $"SA-{(u.PerformedAt ?? r.ExaminationDate):yyyyMMdd}-{u.UltrasoundId:D2}"
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

            // 3. Hoa don (Invoices) - Chi hien thi hoa don DA THANH TOAN (paid)
            // Invoice "pending" chi hien o man hinh Le Tan, khong hien tren App Mobile
            var invoices = await _context.Invoices
                .Where(i => i.PatientId == patientId && i.PaymentStatus == "paid")
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();


            var hoaDon = new List<object>();
            foreach (var inv in invoices)
            {
                var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == inv.AppointmentId);
                int docId = appt?.DoctorId ?? 1;
                string doctorName = doctorMap.ContainsKey(docId) ? doctorMap[docId] : "BS. Nguyễn Văn A";

                // Trả về CHI TIẾT dòng hóa đơn thật (invoice_items) — trước đây chỉ có 1 dòng tóm tắt,
                // khiến app Mobile phải TỰ ĐOÁN cách chia (luôn giả định 250k phí khám + phần còn lại
                // là "thuốc"), sai hoàn toàn với gói khám hoặc ca có phí khác 250k.
                var invoiceItems = await _context.InvoiceItems
                    .Where(ii => ii.InvoiceId == inv.InvoiceId)
                    .OrderBy(ii => ii.ItemId)
                    .Select(ii => new { name = ii.ItemName, amount = ii.Amount })
                    .ToListAsync();

                hoaDon.Add(new
                {
                    id = inv.InvoiceId,
                    date = inv.InvoiceDate.ToString("dd/MM/yyyy"),
                    doctor = doctorName,
                    clinicKey = "general_internal",
                    items = $"Chi phí khám & Dịch vụ y tế (Tổng: {inv.TotalAmount:N0} VNĐ)",
                    invoiceItems,
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

    // GET /api/MedicalRecords/all
    [HttpGet("all")]
    public async Task<IActionResult> GetAllMedicalRecords([FromQuery] string? search, [FromQuery] int? doctorId)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var query = _context.MedicalRecords.AsQueryable();
            if (doctorId.HasValue && doctorId.Value > 0)
            {
                query = query.Where(r => r.DoctorId == doctorId.Value);
            }
            var records = await query.OrderByDescending(r => r.ExaminationDate).ToListAsync();

            var result = new List<object>();
            var docMap = await _context.Doctors.ToDictionaryAsync(d => d.DoctorId, d => d.FullName ?? "Bác sĩ");
            var patientMap = await _context.Patients.ToDictionaryAsync(p => p.PatientId, p => p.FullName ?? "Bệnh nhân");
            var patientPhoneMap = await _context.Patients.ToDictionaryAsync(p => p.PatientId, p => p.PhoneNumber ?? "");

            foreach (var r in records)
            {
                string pName = patientMap.ContainsKey(r.PatientId) ? patientMap[r.PatientId] : "Bệnh nhân";
                string pPhone = patientPhoneMap.ContainsKey(r.PatientId) ? patientPhoneMap[r.PatientId] : "";
                string dName = docMap.ContainsKey(r.DoctorId) ? docMap[r.DoctorId] : "BS. Nguyễn Văn A";
                var rx = await _context.Prescriptions.FirstOrDefaultAsync(p => p.MedicalRecordId == r.MedicalRecordId);
                var rxDetails = rx != null ? await _context.PrescriptionDetails.Where(d => d.PrescriptionId == rx.PrescriptionId).ToListAsync() : new List<PrescriptionDetail>();

                if (!string.IsNullOrEmpty(search))
                {
                    string s = search.ToLower();
                    bool matchName = pName.ToLower().Contains(s);
                    bool matchPhone = pPhone.ToLower().Contains(s);
                    bool matchDiag = (r.Diagnosis ?? "").ToLower().Contains(s);
                    bool matchCode = (r.IcdCode ?? "").ToLower().Contains(s);
                    if (!matchName && !matchPhone && !matchDiag && !matchCode) continue;
                }

                result.Add(new
                {
                    medicalRecordId = r.MedicalRecordId,
                    appointmentId = r.AppointmentId,
                    patientId = r.PatientId,
                    patientName = pName,
                    phoneNumber = pPhone,
                    doctorId = r.DoctorId,
                    doctorName = dName,
                    examinationDate = r.ExaminationDate.ToString("dd/MM/yyyy HH:mm"),
                    symptoms = r.Symptoms ?? "",
                    diagnosis = r.Diagnosis ?? "",
                    treatmentPlan = r.TreatmentPlan ?? "",
                    icdCode = r.IcdCode ?? "",
                    pulse = r.HeartRate?.ToString() ?? "",
                    bloodPressure = r.BloodPressure ?? "",
                    temperature = r.Temperature?.ToString() ?? "",
                    weight = r.Weight?.ToString() ?? "",
                    bmi = r.Bmi?.ToString() ?? "",
                    prescriptionsCount = rxDetails.Count,
                    prescriptionsSummary = string.Join(", ", rxDetails.Select(d => $"{d.MedicineNameSnapshot} ({d.Quantity} {d.UnitSnapshot})"))
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
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
                StatusId = 4, // Status 4 = Completed
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
        // Chỉ Bác sĩ/nhân viên mới được tạo hồ sơ khám bệnh — hành động này thật sự trừ tồn kho
        // thuốc và tạo hóa đơn, bệnh nhân không được tự bịa hồ sơ khám cho bất kỳ ai.
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        // Trước đây DoctorId/MedicineId không hợp lệ (<=0, vd do TokenVault.DoctorId chưa kịp nạp) bị âm
        // thầm thay bằng "#1" thay vì báo lỗi — hồ sơ khám/đơn thuốc có thể bị gắn nhầm cho bác sĩ/thuốc
        // hoàn toàn khác, và trừ tồn kho nhầm thuốc #1 thay vì thuốc bác sĩ thực sự kê.
        if (dto.DoctorId <= 0)
            return BadRequest(new { success = false, message = "Thiếu thông tin bác sĩ khám (DoctorId không hợp lệ)." });
        if (dto.Prescriptions != null && dto.Prescriptions.Any(p => p.MedicineId <= 0))
            return BadRequest(new { success = false, message = "Có thuốc trong đơn thiếu MedicineId hợp lệ." });
        try
        {
            // 1. Tái sử dụng phiếu khám Draft đã tạo sẵn (vd: do đã chỉ định CLS qua
            //    ClinicalOrdersController trước khi hoàn tất khám) thay vì tạo trùng bản ghi mới.
            var record = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == dto.AppointmentId);
            bool isNewRecord = record == null;
            if (record == null) record = new MedicalRecord { AppointmentId = dto.AppointmentId, CreatedAt = DateTime.UtcNow };

            record.PatientId = dto.PatientId;
            record.DoctorId = dto.DoctorId;
            record.Symptoms = dto.Symptoms;
            record.Diagnosis = dto.Diagnosis;
            record.IcdCode = dto.IcdCode;
            record.IcdDescription = dto.IcdDescription;
            record.TreatmentPlan = dto.TreatmentPlan;
            record.BloodPressure = dto.BloodPressure;
            record.DoctorNote = $"Lưu lúc {DateTime.Now:HH:mm dd/MM/yyyy}";
            record.ExaminationDate = DateTime.UtcNow;
            record.Status = "Completed";
            record.UpdatedAt = DateTime.UtcNow;

            if (decimal.TryParse(dto.Temperature, out decimal temp)) record.Temperature = temp;
            if (decimal.TryParse(dto.Weight, out decimal w)) record.Weight = w;
            if (decimal.TryParse(dto.Height, out decimal h)) record.Height = h;
            if (int.TryParse(dto.Pulse, out int pulse)) record.HeartRate = pulse;

            if (record.Height > 0 && record.Weight > 0)
            {
                decimal hM = record.Height.Value / 100m;
                record.Bmi = Math.Round(record.Weight.Value / (hM * hM), 1);
            }

            if (isNewRecord) _context.MedicalRecords.Add(record);
            await _context.SaveChangesAsync();

            // Tự động trừ tồn kho thuốc khi bác sĩ kê đơn — nếu không đủ hàng, trừ về 0 và
            // ghi nhận lại để báo cho bác sĩ biết (KHÔNG chặn lưu đơn, chỉ cảnh báo).
            var insufficientStock = new List<string>();

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
                        MedicineId = drug.MedicineId,
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

                // Trừ tồn kho thật theo đơn vừa kê
                var medIdsForStock = dto.Prescriptions.Select(p => p.MedicineId > 0 ? p.MedicineId : 1).Distinct().ToList();
                var medsForStock = await _context.Medicines.Where(m => medIdsForStock.Contains(m.MedicineId)).ToDictionaryAsync(m => m.MedicineId);
                foreach (var drug in dto.Prescriptions)
                {
                    int mid = drug.MedicineId > 0 ? drug.MedicineId : 1;
                    if (!medsForStock.TryGetValue(mid, out var med)) continue;

                    if (med.StockQuantity < drug.Quantity)
                    {
                        insufficientStock.Add($"{med.MedicineName} (còn {med.StockQuantity} {med.Unit}, cần {drug.Quantity})");
                        med.StockQuantity = 0;
                    }
                    else
                    {
                        med.StockQuantity -= drug.Quantity;
                    }
                    med.UpdatedAt = DateTime.UtcNow;
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

            // 4. Tao Invoice PENDING (cho thu phi) - khong tao invoice da thanh toan
            // Le tan phai bam "Xac nhan thu tien" moi chuyen sang "paid" va hien tren App Mobile
            var existingInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == dto.AppointmentId);
            if (existingInvoice == null)
            {
                // Tinh tong tien tu don thuoc (uoc tinh 15.000/vien - Medicine entity chua co truong Price)
                decimal medFee = 0;
                if (dto.Prescriptions != null && dto.Prescriptions.Count > 0)
                {
                    var medIds = dto.Prescriptions.Select(p => p.MedicineId).ToList();
                    var medDict = await _context.Medicines.Where(m => medIds.Contains(m.MedicineId)).ToDictionaryAsync(m => m.MedicineId);
                    foreach (var presc in dto.Prescriptions)
                    {
                        decimal price = medDict.ContainsKey(presc.MedicineId) ? medDict[presc.MedicineId].Price : 15000m;
                        if (price <= 0) price = 15000m;
                        medFee += price * presc.Quantity;
                    }
                }

                // Phí Xét nghiệm/Siêu âm đã chỉ định cho phiếu khám này (clinical_services.unit_price qua service_id)
                var clsServiceIds = new List<int>();
                clsServiceIds.AddRange(await _context.MedicalTests.Where(t => t.MedicalRecordId == record.MedicalRecordId && t.ServiceId != null).Select(t => t.ServiceId!.Value).ToListAsync());
                clsServiceIds.AddRange(await _context.UltrasoundResults.Where(u => u.MedicalRecordId == record.MedicalRecordId && u.ServiceId != null).Select(u => u.ServiceId!.Value).ToListAsync());
                decimal clsFee = 0;
                if (clsServiceIds.Count > 0)
                {
                    var clsServices = await _context.ClinicalServices.Where(s => clsServiceIds.Contains(s.ServiceId)).ToListAsync();
                    clsFee = clsServices.Sum(s => s.UnitPrice);
                }

                decimal examFee = 250000; // Cong kham chuyen khoa chieu chuot 250.000d
                decimal totalAmount = examFee + medFee + clsFee;

                var pendingInvoice = new Invoice
                {
                    AppointmentId = dto.AppointmentId,
                    PatientId = dto.PatientId,
                    TotalAmount = totalAmount,
                    PaidAmount = 0,           // Chua thu tien
                    // DB chk_payment_status chỉ cho phép 'unpaid'/'partial'/'paid' — KHÔNG có 'pending'.
                    // PaidAmount=0 ở trên đã đúng nghĩa "unpaid", trước đây dùng "pending" (giá trị không
                    // hợp lệ) khiến INSERT bị PostgreSQL từ chối (23514) mỗi lần có tạo invoice ở đây.
                    PaymentStatus = "unpaid", // Le tan chua xac nhan
                    PaymentMethod = null,
                    InvoiceDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Invoices.Add(pendingInvoice);
                await _context.SaveChangesAsync();

                _context.InvoiceItems.Add(new InvoiceItem
                {
                    InvoiceId = pendingInvoice.InvoiceId,
                    ItemName = "Chi phi kham chuyen khoa & Dich vu y te",
                    ItemType = "Consultation",
                    Quantity = 1,
                    UnitPrice = examFee,
                    Amount = examFee,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                if (medFee > 0)
                {
                    _context.InvoiceItems.Add(new InvoiceItem
                    {
                        InvoiceId = pendingInvoice.InvoiceId,
                        ItemName = "Phi thuoc theo Don thuoc dien tu",
                        ItemType = "Medicine",
                        Quantity = dto.Prescriptions?.Count ?? 0,
                        UnitPrice = medFee,
                        Amount = medFee,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                if (clsFee > 0)
                {
                    _context.InvoiceItems.Add(new InvoiceItem
                    {
                        InvoiceId = pendingInvoice.InvoiceId,
                        ItemName = "Phi Xet nghiem & Sieu am chi dinh",
                        ItemType = "ClinicalService",
                        Quantity = clsServiceIds.Count,
                        UnitPrice = clsFee,
                        Amount = clsFee,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                await _context.SaveChangesAsync();
            }
            // Neu da co invoice (vi du chay lai), khong cap nhat gi - giu nguyen trang thai


            return Ok(new { success = true, medicalRecordId = record.MedicalRecordId, insufficientStock });

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
    public string? IcdCode { get; set; }
    public string? IcdDescription { get; set; }
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
