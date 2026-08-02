using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.DTOs;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

// Luồng Bác sĩ chỉ định Xét nghiệm/Siêu âm khi "Đang Khám" (status=3) và
// Kỹ thuật viên (LAB_TECH) xử lý + trả kết quả cận lâm sàng (CLS).
[ApiController]
[Route("api/[controller]")]
public class ClinicalOrdersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;

    public ClinicalOrdersController(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    // DbUpdateException.Message chỉ nói chung chung "An error occurred while saving...";
    // lỗi thật (vd chi tiết FK/constraint từ Postgres) nằm ở tầng InnerException sâu nhất.
    private static string GetErrorDetail(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    // "Imaging" (Siêu âm/X-quang/CT) → nhóm "Ultrasound" phía client; mọi service_type khác
    // (Laboratory, Functional...) → nhóm "Test" — giữ nguyên hợp đồng API cũ (Test|Ultrasound)
    // trong khi dữ liệu thật dùng service_type theo đúng thiết kế DB gốc (clinical_services).
    private static string DisplayCategory(ClinicalService s) => s.ServiceType == "Imaging" ? "Ultrasound" : "Test";

    // GET /api/clinicalorders/services?category=Test|Ultrasound
    // Danh mục dịch vụ để Bác sĩ chọn khi chỉ định (clinical_services — bảng thật của thiết kế DB gốc).
    [HttpGet("services")]
    public async Task<IActionResult> GetServices([FromQuery] string? category)
    {
        try
        {
            var all = await _context.ClinicalServices.Where(s => s.IsActive).OrderBy(s => s.ServiceType).ThenBy(s => s.ServiceName).ToListAsync();
            var items = string.IsNullOrWhiteSpace(category) ? all : all.Where(s => DisplayCategory(s) == category).ToList();

            return Ok(new
            {
                success = true,
                items = items.Select(s => new
                {
                    serviceId = s.ServiceId,
                    serviceCode = s.ServiceCode,
                    serviceName = s.ServiceName,
                    categoryType = DisplayCategory(s),
                    price = s.UnitPrice
                })
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // POST /api/clinicalorders
    // Bác sĩ chỉ định 1..N dịch vụ (Xét nghiệm và/hoặc Siêu âm) cho 1 lượt khám đang diễn ra.
    [HttpPost]
    public async Task<IActionResult> CreateClinicalOrders([FromBody] CreateClinicalOrdersDto dto)
    {
        try
        {
            if (dto.ServiceIds == null || dto.ServiceIds.Count == 0)
                return BadRequest(new { success = false, message = "Chưa chọn dịch vụ Xét nghiệm/Siêu âm nào để chỉ định." });

            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == dto.AppointmentId);
            if (appt == null)
                return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });

            // Tái sử dụng phiếu khám Draft đang mở của lượt khám này (nếu có), thay vì tạo bản ghi mới —
            // tránh trùng lặp khi bác sĩ chỉ định CLS trước khi hoàn tất chẩn đoán/kê đơn.
            var record = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == dto.AppointmentId);
            if (record == null)
            {
                record = new MedicalRecord
                {
                    AppointmentId = dto.AppointmentId,
                    PatientId = dto.PatientId,
                    DoctorId = dto.DoctorId,
                    ExaminationDate = DateTime.UtcNow,
                    Status = "Draft",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.MedicalRecords.Add(record);
                await _context.SaveChangesAsync();
            }

            var services = await _context.ClinicalServices
                .Where(s => dto.ServiceIds.Contains(s.ServiceId))
                .ToListAsync();

            // Phát hiện sớm ServiceId không tồn tại/không khớp trong clinical_services — trả lỗi rõ ràng
            // thay vì để văng FK violation khó hiểu ở bước SaveChanges bên dưới.
            var missingIds = dto.ServiceIds.Except(services.Select(s => s.ServiceId)).ToList();
            if (missingIds.Count > 0)
                return BadRequest(new { success = false, message = $"Dịch vụ với ServiceId [{string.Join(", ", missingIds)}] không tồn tại trong danh mục clinical_services. Vui lòng làm mới danh sách dịch vụ và chọn lại." });

            int createdTests = 0, createdUltrasounds = 0;
            foreach (var svc in services)
            {
                if (svc.ServiceType == "Imaging")
                {
                    _context.UltrasoundResults.Add(new UltrasoundResult
                    {
                        MedicalRecordId = record.MedicalRecordId,
                        UltrasoundType = svc.ServiceName,
                        ResultStatus = "Pending",
                        ServiceId = svc.ServiceId,
                        OrderedBy = dto.OrderedByUserId,
                        OrderedAt = DateTime.UtcNow,
                        IsUrgent = dto.IsUrgent,
                        ClinicalNote = dto.ClinicalNote,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    createdUltrasounds++;
                }
                else
                {
                    _context.MedicalTests.Add(new MedicalTest
                    {
                        MedicalRecordId = record.MedicalRecordId,
                        TestName = svc.ServiceName,
                        TestType = svc.ServiceType,
                        ResultStatus = "Pending",
                        ServiceId = svc.ServiceId,
                        OrderedBy = dto.OrderedByUserId,
                        OrderedAt = DateTime.UtcNow,
                        IsUrgent = dto.IsUrgent,
                        ClinicalNote = dto.ClinicalNote,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    createdTests++;
                }
            }
            await _context.SaveChangesAsync();

            // Đảm bảo status_id=9 'AwaitingTestResults' tồn tại, rồi chuyển appointment sang trạng thái này
            // để hàng đợi Lễ Tân/Bác sĩ biết bệnh nhân đang ở phòng CLS, chưa quay lại phòng khám.
            bool hasStatus9 = await _context.AppointmentStatuses.AnyAsync(s => s.StatusId == 9);
            if (!hasStatus9)
            {
                _context.AppointmentStatuses.Add(new AppointmentStatus { StatusId = 9, StatusName = "AwaitingTestResults" });
                await _context.SaveChangesAsync();
            }
            // Luôn chuyển sang "Chờ Kết Quả CLS" khi vừa tạo chỉ định mới còn Pending — bất kể trạng thái
            // trước đó là gì (kể cả lỡ đã "Completed" từ trước, ví dụ do bấm nhầm Ctrl+S) — MIỄN LÀ ca
            // chưa bị Hủy(5)/Bỏ khám(6). Trước đây chỉ áp dụng khi status==3 (InProgress), nên nếu ca đã
            // lỡ ở trạng thái Completed thì việc chỉ định CLS không hề "kéo" nó về lại hàng chờ, khiến
            // hàng chờ hiện "Đã hoàn thành" dù rõ ràng còn 1 chỉ định đang Pending.
            if (appt.StatusId != 5 && appt.StatusId != 6 && appt.StatusId != 9)
            {
                appt.StatusId = 9;
                appt.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, medicalRecordId = record.MedicalRecordId, createdTests, createdUltrasounds });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // GET /api/clinicalorders/queue?type=Test|Ultrasound
    // Hàng đợi của Kỹ thuật viên: mọi chỉ định CLS còn 'Pending', không phụ thuộc appointment.status
    // (Bác sĩ có thể đã hoàn tất lượt khám trong khi CLS vẫn chưa xong).
    // done=false (mặc định): mọi chỉ định còn 'Pending'. done=true: chỉ định đã có kết quả TRONG HÔM NAY.
    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue([FromQuery] string? type, [FromQuery] bool done = false)
    {
        try
        {
            var patientMap = await _context.Patients.ToDictionaryAsync(p => p.PatientId, p => p);
            var doctorMap = await _context.Doctors.ToDictionaryAsync(d => d.DoctorId, d => d.FullName ?? "Bác sĩ");
            var records = await _context.MedicalRecords.ToDictionaryAsync(r => r.MedicalRecordId, r => r);
            var today = DateTime.UtcNow.Date;

            var result = new List<ClinicalOrderItemDto>();

            if (string.IsNullOrEmpty(type) || type == "Test")
            {
                var testsQuery = done
                    ? _context.MedicalTests.Where(t => t.ResultStatus != "Pending" && t.PerformedAt != null && t.PerformedAt.Value.Date == today)
                    : _context.MedicalTests.Where(t => t.ResultStatus == "Pending");
                var tests = await testsQuery.ToListAsync();
                foreach (var t in tests)
                {
                    if (!records.TryGetValue(t.MedicalRecordId, out var rec)) continue;
                    var item = BuildItem("Test", t.TestId, rec, t.TestName, t.OrderedAt, t.IsUrgent, t.ClinicalNote, patientMap, doctorMap);
                    item.Status = t.ResultStatus;
                    result.Add(item);
                }
            }
            if (string.IsNullOrEmpty(type) || type == "Ultrasound")
            {
                var ulsQuery = done
                    ? _context.UltrasoundResults.Where(u => u.ResultStatus == "Completed" && u.PerformedAt != null && u.PerformedAt.Value.Date == today)
                    : _context.UltrasoundResults.Where(u => u.ResultStatus == "Pending");
                var uls = await ulsQuery.ToListAsync();
                foreach (var u in uls)
                {
                    if (!records.TryGetValue(u.MedicalRecordId, out var rec)) continue;
                    var item = BuildItem("Ultrasound", u.UltrasoundId, rec, u.UltrasoundType ?? "Siêu âm", u.OrderedAt, u.IsUrgent, u.ClinicalNote, patientMap, doctorMap);
                    item.Status = u.ResultStatus;
                    item.ImageCount = u.ImageUrls?.Length ?? 0;
                    result.Add(item);
                }
            }

            // Ca Khẩn (cito) luôn được xếp lên đầu hàng đợi, trong nhóm đó xếp theo giờ chỉ định
            var ordered = result.OrderByDescending(i => i.IsUrgent).ThenBy(i => i.OrderedAt);
            return Ok(new { success = true, items = ordered });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // GET /api/clinicalorders/by-appointment/{appointmentId}
    // TOÀN BỘ chỉ định CLS (mọi trạng thái: Pending/Normal/Abnormal/Completed/Cancelled) của 1 lượt khám —
    // để Bác sĩ XEM LẠI kết quả Xét nghiệm/Siêu âm ngay trong Phiếu Khám trước khi kê đơn & hoàn tất.
    [HttpGet("by-appointment/{appointmentId}")]
    public async Task<IActionResult> GetByAppointment(int appointmentId)
    {
        try
        {
            var record = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == appointmentId);
            if (record == null) return Ok(new { success = true, items = new List<ClinicalOrderItemDto>() });

            var patientMap = await _context.Patients.ToDictionaryAsync(p => p.PatientId, p => p);
            var doctorMap = await _context.Doctors.ToDictionaryAsync(d => d.DoctorId, d => d.FullName ?? "Bác sĩ");
            var records = new Dictionary<int, MedicalRecord> { [record.MedicalRecordId] = record };

            var result = new List<ClinicalOrderItemDto>();

            var tests = await _context.MedicalTests.Where(t => t.MedicalRecordId == record.MedicalRecordId).ToListAsync();
            foreach (var t in tests)
            {
                var item = BuildItem("Test", t.TestId, record, t.TestName, t.OrderedAt, t.IsUrgent, t.ClinicalNote, patientMap, doctorMap);
                item.Status = t.ResultStatus;
                item.ResultValue = t.ResultValue;
                item.Unit = t.Unit;
                item.ReferenceRange = t.ReferenceRange;
                result.Add(item);
            }

            var uls = await _context.UltrasoundResults.Where(u => u.MedicalRecordId == record.MedicalRecordId).ToListAsync();
            foreach (var u in uls)
            {
                var item = BuildItem("Ultrasound", u.UltrasoundId, record, u.UltrasoundType ?? "Siêu âm", u.OrderedAt, u.IsUrgent, u.ClinicalNote, patientMap, doctorMap);
                item.Status = u.ResultStatus;
                item.ImageCount = u.ImageUrls?.Length ?? 0;
                item.Description = u.Description;
                item.Conclusion = u.Conclusion;
                result.Add(item);
            }

            return Ok(new { success = true, items = result.OrderBy(i => i.OrderedAt) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    private ClinicalOrderItemDto BuildItem(string kind, int id, MedicalRecord rec, string serviceName, DateTime orderedAt,
        bool isUrgent, string? clinicalNote, Dictionary<int, Patient> patientMap, Dictionary<int, string> doctorMap)
    {
        patientMap.TryGetValue(rec.PatientId, out var patient);
        int age = 0;
        if (patient?.DateOfBirth != null)
            age = DateTime.Today.Year - patient.DateOfBirth.Value.Year;

        return new ClinicalOrderItemDto
        {
            Kind = kind,
            Id = id,
            MedicalRecordId = rec.MedicalRecordId,
            AppointmentId = rec.AppointmentId,
            PatientId = rec.PatientId,
            PatientName = patient?.FullName ?? "Bệnh nhân",
            PatientAge = age,
            PatientGender = patient?.Gender ?? "",
            DoctorName = doctorMap.TryGetValue(rec.DoctorId, out var dName) ? dName : "BS. Nguyễn Văn A",
            ServiceName = serviceName,
            Status = "Pending",
            IsUrgent = isUrgent,
            ClinicalNote = clinicalNote,
            OrderedAt = orderedAt
        };
    }

    // PUT /api/clinicalorders/tests/{id}/result
    [HttpPut("tests/{id}/result")]
    public async Task<IActionResult> SubmitTestResult(int id, [FromBody] SubmitTestResultDto dto)
    {
        try
        {
            var test = await _context.MedicalTests.FirstOrDefaultAsync(t => t.TestId == id);
            if (test == null) return NotFound(new { success = false, message = "Không tìm thấy chỉ định xét nghiệm." });

            test.ResultValue = dto.ResultValue;
            test.Unit = dto.Unit;
            test.ReferenceRange = dto.ReferenceRange;
            test.ResultStatus = dto.ResultStatus == "Abnormal" ? "Abnormal" : "Normal";
            test.ResultFileUrl = dto.ResultFileUrl;
            test.PerformedAt = DateTime.UtcNow;
            test.PerformedBy = dto.PerformedByUserId;
            test.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await TryReturnToDoctorAsync(test.MedicalRecordId);

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // PUT /api/clinicalorders/ultrasound/{id}/result
    [HttpPut("ultrasound/{id}/result")]
    public async Task<IActionResult> SubmitUltrasoundResult(int id, [FromBody] SubmitUltrasoundResultDto dto)
    {
        try
        {
            var uls = await _context.UltrasoundResults.FirstOrDefaultAsync(u => u.UltrasoundId == id);
            if (uls == null) return NotFound(new { success = false, message = "Không tìm thấy chỉ định siêu âm." });

            uls.Description = dto.Description;
            uls.Conclusion = dto.Conclusion;
            // KHÔNG đụng vào ImageUrls ở đây — ảnh được quản lý riêng qua 2 endpoint
            // upload/xóa ảnh (POST|DELETE .../images), gán đè ở đây sẽ xóa mất ảnh KTV vừa đính kèm
            // vì dto.ImageUrls luôn null (client không hề gửi trường này khi lưu kết quả).
            uls.ResultStatus = "Completed";
            uls.PerformedAt = DateTime.UtcNow;
            uls.PerformedBy = dto.PerformedByUserId;
            uls.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await TryReturnToDoctorAsync(uls.MedicalRecordId);

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // PUT /api/clinicalorders/tests/{id}/cancel — Bác sĩ/KTV hủy 1 chỉ định xét nghiệm đã tạo nhầm
    [HttpPut("tests/{id}/cancel")]
    public async Task<IActionResult> CancelTestOrder(int id)
    {
        try
        {
            var test = await _context.MedicalTests.FirstOrDefaultAsync(t => t.TestId == id);
            if (test == null) return NotFound(new { success = false, message = "Không tìm thấy chỉ định xét nghiệm." });
            if (test.ResultStatus != "Pending")
                return BadRequest(new { success = false, message = "Chỉ định đã có kết quả, không thể hủy." });

            test.ResultStatus = "Cancelled";
            test.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await TryReturnToDoctorAsync(test.MedicalRecordId);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // PUT /api/clinicalorders/ultrasound/{id}/cancel
    [HttpPut("ultrasound/{id}/cancel")]
    public async Task<IActionResult> CancelUltrasoundOrder(int id)
    {
        try
        {
            var uls = await _context.UltrasoundResults.FirstOrDefaultAsync(u => u.UltrasoundId == id);
            if (uls == null) return NotFound(new { success = false, message = "Không tìm thấy chỉ định siêu âm." });
            if (uls.ResultStatus != "Pending")
                return BadRequest(new { success = false, message = "Chỉ định đã có kết quả, không thể hủy." });

            uls.ResultStatus = "Cancelled";
            uls.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await TryReturnToDoctorAsync(uls.MedicalRecordId);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // GET /api/clinicalorders/ultrasound/{id} — chi tiết 1 chỉ định siêu âm (mô tả/kết luận/ảnh hiện có),
    // dùng để hiển thị trong cửa sổ nhập kết quả riêng của KTV (kèm xem trước ảnh đã đính kèm).
    [HttpGet("ultrasound/{id}")]
    public async Task<IActionResult> GetUltrasoundDetail(int id)
    {
        try
        {
            var uls = await _context.UltrasoundResults.FirstOrDefaultAsync(u => u.UltrasoundId == id);
            if (uls == null) return NotFound(new { success = false, message = "Không tìm thấy chỉ định siêu âm." });

            return Ok(new
            {
                success = true,
                ultrasoundId = uls.UltrasoundId,
                description = uls.Description,
                conclusion = uls.Conclusion,
                status = uls.ResultStatus,
                imageUrls = uls.ImageUrls ?? Array.Empty<string>()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // POST /api/clinicalorders/ultrasound/{id}/images — KTV đính kèm ảnh siêu âm thật (multipart/form-data, field "file")
    [HttpPost("ultrasound/{id}/images")]
    public async Task<IActionResult> UploadUltrasoundImage(int id, IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Chưa chọn file ảnh." });

            var uls = await _context.UltrasoundResults.FirstOrDefaultAsync(u => u.UltrasoundId == id);
            if (uls == null) return NotFound(new { success = false, message = "Không tìm thấy chỉ định siêu âm." });

            string ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            string fileName = $"{Guid.NewGuid()}{ext}";

            string webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
            string uploadDir = Path.Combine(webRoot, "uploads", "ultrasound", id.ToString());
            Directory.CreateDirectory(uploadDir);
            string fullPath = Path.Combine(uploadDir, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
                await file.CopyToAsync(stream);

            string url = $"/uploads/ultrasound/{id}/{fileName}";
            var current = uls.ImageUrls?.ToList() ?? new List<string>();
            current.Add(url);
            uls.ImageUrls = current.ToArray();
            uls.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, url, imageUrls = uls.ImageUrls });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // DELETE /api/clinicalorders/ultrasound/{id}/images?index=0 — bỏ 1 ảnh đính kèm nhầm
    [HttpDelete("ultrasound/{id}/images")]
    public async Task<IActionResult> RemoveUltrasoundImage(int id, [FromQuery] int index)
    {
        try
        {
            var uls = await _context.UltrasoundResults.FirstOrDefaultAsync(u => u.UltrasoundId == id);
            if (uls == null) return NotFound(new { success = false, message = "Không tìm thấy chỉ định siêu âm." });

            var current = uls.ImageUrls?.ToList() ?? new List<string>();
            if (index < 0 || index >= current.Count)
                return BadRequest(new { success = false, message = "Chỉ số ảnh không hợp lệ." });

            current.RemoveAt(index);
            uls.ImageUrls = current.ToArray();
            uls.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, imageUrls = uls.ImageUrls });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = GetErrorDetail(ex) });
        }
    }

    // Khi TẤT CẢ chỉ định CLS của 1 phiếu khám đã có kết quả, đưa bệnh nhân quay lại
    // hàng đợi Bác sĩ (status 9 → 3) và báo cho bệnh nhân biết đã có kết quả mới.
    private async Task TryReturnToDoctorAsync(int medicalRecordId)
    {
        bool stillPending = await _context.MedicalTests.AnyAsync(t => t.MedicalRecordId == medicalRecordId && t.ResultStatus == "Pending")
            || await _context.UltrasoundResults.AnyAsync(u => u.MedicalRecordId == medicalRecordId && u.ResultStatus == "Pending");
        if (stillPending) return;

        var record = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.MedicalRecordId == medicalRecordId);
        if (record == null) return;

        var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == record.AppointmentId);
        if (appt == null || appt.StatusId != 9) return;

        appt.StatusId = 3; // Quay lại InProgress — Bác sĩ tiếp tục xem kết quả & hoàn tất khám
        appt.UpdatedAt = DateTime.UtcNow;

        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId);
        if (patient != null && patient.UserId != Guid.Empty)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = patient.UserId,
                Title = "🔬 Đã Có Kết Quả Cận Lâm Sàng",
                Content = "Kết quả xét nghiệm/siêu âm của bạn đã có. Vui lòng quay lại phòng khám để Bác sĩ tư vấn kết quả.",
                Type = "result",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }
}
