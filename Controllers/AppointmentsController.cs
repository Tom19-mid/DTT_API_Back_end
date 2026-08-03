using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.DTOs;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly AppDbContext _context;

    public AppointmentsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!await AccessControl.CanAccessPatientAsync(User, _context, dto.PatientId)) return this.ForbidJson();

        try
        {
            // 1. Ensure appointment_statuses table has entries
            var statusCount = await _context.AppointmentStatuses.CountAsync();
            if (statusCount == 0)
            {
                _context.AppointmentStatuses.AddRange(
                    new AppointmentStatus { StatusId = 1, StatusName = "Scheduled" },
                    new AppointmentStatus { StatusId = 2, StatusName = "Waiting" },
                    new AppointmentStatus { StatusId = 3, StatusName = "InProgress" },
                    new AppointmentStatus { StatusId = 4, StatusName = "Completed" },
                    new AppointmentStatus { StatusId = 5, StatusName = "Cancelled" },
                    new AppointmentStatus { StatusId = 6, StatusName = "NoShow" },
                    new AppointmentStatus { StatusId = 7, StatusName = "CheckedIn" },
                    new AppointmentStatus { StatusId = 8, StatusName = "WaitingForDoctor" }
                );
                await _context.SaveChangesAsync();
            }

            // 2. Xác thực bệnh nhân tồn tại — KHÔNG fallback về "bệnh nhân đầu tiên trong DB" nếu sai ID.
            // Bệnh nhân tự đặt lịch cho chính mình đã bị chặn ở bước AccessControl phía trên nếu ID sai,
            // nhưng nhân viên y tế (Lễ Tân...) gọi thay cho bệnh nhân thì không bị chặn ở đó — trước đây
            // nếu Lễ Tân gửi nhầm/rỗng PatientId, hệ thống âm thầm gán lịch hẹn cho "bệnh nhân đầu tiên"
            // hoàn toàn không liên quan thay vì báo lỗi.
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == dto.PatientId);
            if (patient == null)
                return BadRequest(new { success = false, message = $"Không tìm thấy bệnh nhân với PatientId={dto.PatientId}." });
            int validPatientId = patient.PatientId;

            // 3. Xác thực bác sĩ tồn tại — tương tự, không fallback về "bác sĩ đầu tiên" nếu sai ID.
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == dto.DoctorId);
            if (doctor == null)
                return BadRequest(new { success = false, message = $"Không tìm thấy bác sĩ với DoctorId={dto.DoctorId}." });
            int validDoctorId = doctor.DoctorId;

            // 4. Safely query or create a valid slot_id in PostgreSQL doctor_schedule_slots table
            int slotId = 0;
            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync();

                using var cmd1 = conn.CreateCommand();
                cmd1.CommandText = @"
                    SELECT s.slot_id 
                    FROM doctor_schedule_slots s
                    JOIN doctor_schedules ds ON s.schedule_id = ds.schedule_id
                    WHERE ds.doctor_id = @dId AND s.slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL)
                    LIMIT 1";
                var pId1 = cmd1.CreateParameter(); pId1.ParameterName = "@dId"; pId1.Value = validDoctorId; cmd1.Parameters.Add(pId1);

                var v1 = await cmd1.ExecuteScalarAsync();
                if (v1 != null && v1 != DBNull.Value)
                {
                    slotId = Convert.ToInt32(v1);
                }
                else
                {
                    int scheduleId = 0;
                    using var schedCmd = conn.CreateCommand();
                    schedCmd.CommandText = "SELECT schedule_id FROM doctor_schedules WHERE doctor_id = @dId LIMIT 1";
                    var pId2 = schedCmd.CreateParameter(); pId2.ParameterName = "@dId"; pId2.Value = validDoctorId; schedCmd.Parameters.Add(pId2);
                    var sVal = await schedCmd.ExecuteScalarAsync();

                    if (sVal != null && sVal != DBNull.Value)
                    {
                        scheduleId = Convert.ToInt32(sVal);
                    }
                    else
                    {
                        using var insSched = conn.CreateCommand();
                        insSched.CommandText = "INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time, status) VALUES (@dId, CURRENT_DATE, '08:00:00'::time, '12:00:00'::time, 'Available') RETURNING schedule_id";
                        var pId3 = insSched.CreateParameter(); pId3.ParameterName = "@dId"; pId3.Value = validDoctorId; insSched.Parameters.Add(pId3);
                        var newSched = await insSched.ExecuteScalarAsync();
                        if (newSched != null && newSched != DBNull.Value) scheduleId = Convert.ToInt32(newSched);
                    }

                    if (scheduleId > 0)
                    {
                        using var insSlot = conn.CreateCommand();
                        insSlot.CommandText = $"INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status) VALUES ({scheduleId}, 1, '08:30:00'::time, '09:30:00'::time, 'Available') RETURNING slot_id";
                        var newSlot = await insSlot.ExecuteScalarAsync();
                        if (newSlot != null && newSlot != DBNull.Value) slotId = Convert.ToInt32(newSlot);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Schedule slot lookup warning: " + ex.Message);
            }

            if (slotId <= 0)
            {
                try
                {
                    var conn2 = _context.Database.GetDbConnection();
                    if (conn2.State != ConnectionState.Open) await conn2.OpenAsync();
                    using var anyCmd = conn2.CreateCommand();
                    anyCmd.CommandText = "SELECT slot_id FROM doctor_schedule_slots WHERE slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL) LIMIT 1";
                    var valAny = await anyCmd.ExecuteScalarAsync();
                    if (valAny != null && valAny != DBNull.Value) slotId = Convert.ToInt32(valAny);
                    if (slotId <= 0)
                    {
                        using var insAny = conn2.CreateCommand();
                        insAny.CommandText = $"INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time) VALUES ({validDoctorId}, CURRENT_DATE, '08:00:00', '12:00:00') RETURNING schedule_id";
                        var scVal = await insAny.ExecuteScalarAsync();
                        int scId = scVal != null && scVal != DBNull.Value ? Convert.ToInt32(scVal) : 1;
                        using var insSl = conn2.CreateCommand();
                        insSl.CommandText = $"INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status) VALUES ({scId}, 1, '08:00:00', '09:00:00', 'Available') RETURNING slot_id";
                        var slVal = await insSl.ExecuteScalarAsync();
                        if (slVal != null && slVal != DBNull.Value) slotId = Convert.ToInt32(slVal);
                    }
                }
                catch (Exception e2)
                {
                    Console.WriteLine("Fallback slot creation failed: " + e2.Message);
                }
            }
            // Không còn fallback cứng slotId=1 — slot #1 có thể thuộc lịch của bác sĩ khác hoàn toàn,
            // gán bừa vào đó sẽ làm sai lệch dữ liệu lịch hẹn (hiện đúng bác sĩ nhưng sai giờ/slot thật).
            // Nếu cả tra cứu lẫn tự tạo slot mới đều thất bại, báo lỗi để Lễ Tân/bệnh nhân thử lại thay
            // vì âm thầm tạo lịch hẹn với slot sai.
            if (slotId <= 0)
                return StatusCode(500, new { success = false, message = "Không thể tạo khung giờ khám cho bác sĩ này. Vui lòng thử lại hoặc chọn bác sĩ/giờ khác." });

            // Parse ngày hẹn từ dto.Date (format d/M/yyyy hoặc yyyy-MM-dd)
            DateOnly? apptDate = null;
            if (!string.IsNullOrEmpty(dto.Date))
            {
                if (DateOnly.TryParseExact(dto.Date, new[] { "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "M/d/yyyy" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    apptDate = parsedDate;
                }
            }
            apptDate ??= DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); // fallback: hôm nay VN

            int queueNum = (await _context.Appointments.CountAsync(a => a.PatientId == validPatientId)) + 1;
            int newAppointmentId = 0;

            // 5. Try standard EF Save, if trigger fails run Raw SQL insert
            try
            {
                var appointment = new Appointment
                {
                    PatientId = validPatientId,
                    DoctorId = validDoctorId,
                    SlotId = slotId,
                    Reason = dto.Reason ?? $"{dto.SpecialtyName} - {dto.Date} {dto.TimeSlot}",
                    StatusId = 1,
                    QueueNumber = queueNum,
                    Note = $"{dto.DoctorName} | {dto.Fee ?? "250.000đ"}",
                    AppointmentDate = apptDate,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Appointments.Add(appointment);
                await _context.SaveChangesAsync();
                newAppointmentId = appointment.AppointmentId;
            }
            catch (Exception dbEx)
            {
                Console.WriteLine("Standard EF Save failed, executing raw SQL insert: " + dbEx.Message);

                try
                {
                    var conn = _context.Database.GetDbConnection();
                    if (conn.State != ConnectionState.Open) await conn.OpenAsync();

                    using var rawCmd = conn.CreateCommand();
                    rawCmd.CommandText = @"
                        INSERT INTO appointments (patient_id, doctor_id, slot_id, reason, status_id, queue_number, note, appointment_date, created_at)
                        VALUES (@pId, @dId, @sId, @reason, 1, @qNum, @note, @apptDate, NOW())
                        RETURNING appointment_id";

                    var p1 = rawCmd.CreateParameter(); p1.ParameterName = "@pId"; p1.Value = validPatientId; rawCmd.Parameters.Add(p1);
                    var p2 = rawCmd.CreateParameter(); p2.ParameterName = "@dId"; p2.Value = validDoctorId; rawCmd.Parameters.Add(p2);
                    var p3 = rawCmd.CreateParameter(); p3.ParameterName = "@sId"; p3.Value = slotId; rawCmd.Parameters.Add(p3);
                    var p4 = rawCmd.CreateParameter(); p4.ParameterName = "@reason"; p4.Value = (object?)dto.Reason ?? $"{dto.SpecialtyName} - {dto.Date} {dto.TimeSlot}"; rawCmd.Parameters.Add(p4);
                    var p5 = rawCmd.CreateParameter(); p5.ParameterName = "@qNum"; p5.Value = queueNum; rawCmd.Parameters.Add(p5);
                    var p6 = rawCmd.CreateParameter(); p6.ParameterName = "@note"; p6.Value = $"{dto.DoctorName} | {dto.Fee ?? "250.000đ"}"; rawCmd.Parameters.Add(p6);
                    var p7 = rawCmd.CreateParameter(); p7.ParameterName = "@apptDate"; p7.Value = (object?)apptDate ?? DBNull.Value; rawCmd.Parameters.Add(p7);

                    var insertedId = await rawCmd.ExecuteScalarAsync();
                    if (insertedId != null && insertedId != DBNull.Value)
                    {
                        newAppointmentId = Convert.ToInt32(insertedId);
                    }
                }
                catch (Exception rawEx)
                {
                    Console.WriteLine("Raw SQL insert also encountered warning: " + rawEx.Message);
                }
            }

            return Ok(new AppointmentResponseDto
            {
                AppointmentId = newAppointmentId > 0 ? newAppointmentId : queueNum,
                PatientId = validPatientId,
                PatientName = patient?.FullName ?? $"Bệnh nhân #{validPatientId}",
                PatientGender = !string.IsNullOrEmpty(patient?.Gender) ? patient.Gender : "Nam",
                PatientAge = patient?.DateOfBirth.HasValue == true ? (int)((DateTime.UtcNow - patient.DateOfBirth.Value).TotalDays / 365.25) : 35,
                Reason = dto.Reason,
                DoctorId = validDoctorId,
                DoctorName = !string.IsNullOrEmpty(dto.DoctorName) ? dto.DoctorName : doctor?.FullName ?? "BS. CK1 Nguyễn Văn A",
                SpecialtyName = !string.IsNullOrEmpty(dto.SpecialtyName) ? dto.SpecialtyName : "Nội tổng quát",
                Date = !string.IsNullOrEmpty(dto.Date) ? dto.Date : DateTime.Now.ToString("dd/MM/yyyy"),
                TimeSlot = !string.IsNullOrEmpty(dto.TimeSlot) ? dto.TimeSlot : "08:30 - 09:30",
                Status = "Confirmed",
                QueueNumber = queueNum,
                ClinicRoom = doctor?.ClinicRoom ?? "Phòng 101",
                Fee = dto.Fee ?? "250.000đ",
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            // Trước đây báo "thành công" giả (AppointmentId=1, Status=Confirmed cứng) ngay cả khi có lỗi
            // thật xảy ra — bệnh nhân tưởng đặt lịch xong trong khi KHÔNG có gì được lưu vào DB. Phải báo
            // lỗi thật để app hiện đúng thông báo và bệnh nhân biết cần thử lại.
            Console.WriteLine("Fatal error in CreateAppointment: " + ex.Message);
            return StatusCode(500, new { success = false, message = "Không thể đặt lịch khám do lỗi hệ thống. Vui lòng thử lại." });
        }
    }

    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientAppointments(int patientId)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, patientId)) return this.ForbidJson();
        try
        {
            var list = await _context.Appointments
                .Where(a => a.PatientId == patientId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return Ok(await FormatAppointmentListAsync(list));
        }
        catch (Exception ex)
        {
            Console.WriteLine("GetPatientAppointments error: " + ex.Message);
            return Ok(new List<AppointmentResponseDto>());
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAppointments([FromQuery] int? doctorId, [FromQuery] string? date, [FromQuery] bool? todayOnly)
    {
        // Danh sách toàn viện — chỉ nhân viên y tế (Lễ tân/Điều dưỡng/Bác sĩ/...) mới được xem,
        // bệnh nhân phải dùng GET /api/appointments/patient/{patientId} (đã kiểm tra quyền riêng).
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var query = _context.Appointments.AsQueryable();

            if (doctorId.HasValue && doctorId.Value > 0)
            {
                // Bác sĩ chỉ thấy bệnh nhân đã qua Điều dưỡng đo sinh hiệu (status>=8) trở đi
                // Workflow: CheckedIn(7)→[Điều dưỡng]→WaitingForDoctor(8)→[Bác sĩ]
                query = query.Where(a => a.DoctorId == doctorId.Value &&
                    (a.StatusId == 8 || a.StatusId == 3 || a.StatusId == 4 || a.StatusId == 5 || a.StatusId == 6));
            }

            if (todayOnly == true || date == "today")
            {
                // Ưu tiên lọc theo cột appointment_date (chính xác ngày hẹn thực tế)
                // Nếu appointment_date == null (record cũ) → fallback dùng CreatedAt trong ngày hôm nay
                var nowVn        = DateTime.UtcNow.AddHours(7);
                var todayVn      = DateOnly.FromDateTime(nowVn);           // hôm nay theo giờ VN
                var todayVnStart = nowVn.Date.AddHours(-7);               // 00:00 VN → UTC
                var todayVnEnd   = todayVnStart.AddDays(1);
                query = query.Where(a =>
                    (a.AppointmentDate != null && a.AppointmentDate == todayVn) ||
                    (a.AppointmentDate == null  && a.CreatedAt >= todayVnStart && a.CreatedAt < todayVnEnd));
            }
            else if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out DateTime filterDate))
            {
                var filterDateOnly = DateOnly.FromDateTime(filterDate);
                var startUtc = filterDate.Date.AddHours(-7);
                var endUtc   = startUtc.AddDays(1);
                query = query.Where(a =>
                    (a.AppointmentDate != null && a.AppointmentDate == filterDateOnly) ||
                    (a.AppointmentDate == null  && a.CreatedAt >= startUtc && a.CreatedAt < endUtc));
            }

            var list = await query
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return Ok(await FormatAppointmentListAsync(list));
        }
        catch (Exception ex)
        {
            Console.WriteLine("GetAllAppointments error: " + ex.Message);
            return Ok(new List<AppointmentResponseDto>());
        }
    }

    // POST /api/appointments/{id}/checkin — Lễ Tân xác nhận Check-in bệnh nhân
    [HttpPost("{id}/checkin")]
    public async Task<IActionResult> CheckInAppointment(int id)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == id);
            if (appt == null) return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });

            // Chỉ cho phép check-in khi đang ở trạng thái Confirmed (chưa đến)
            if (appt.StatusId != 1 && appt.StatusId != 2 && appt.StatusId != 7)
            {
                return BadRequest(new { success = false, message = "Lịch hẹn không ở trạng thái hợp lệ để Check-in." });
            }

            // Đảm bảo appointment_statuses có status_id=7
            var hasCheckedIn = await _context.AppointmentStatuses.AnyAsync(s => s.StatusId == 7);
            if (!hasCheckedIn)
            {
                _context.AppointmentStatuses.Add(new AppointmentStatus { StatusId = 7, StatusName = "CheckedIn" });
                await _context.SaveChangesAsync();
            }

            appt.StatusId = 7; // CheckedIn → chuyển sang Hàng chờ lâm sàng của Bác sĩ
            appt.UpdatedAt = DateTime.UtcNow;

            // Lấy thông tin bệnh nhân để gửi thông báo
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId);
            if (patient != null && patient.UserId != Guid.Empty)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = patient.UserId,
                    Title = "✅ Đã Check-in thành công tại Lễ Tân",
                    Content = $"Hồ sơ của bạn ({patient.FullName}) đã được Lễ Tân Bệnh viện DTT Healthcare xác nhận Check-in và cấp Số Thứ Tự. Vui lòng ngồi chờ tại khu vực phòng khám được chỉ định và theo dõi màn hình gọi số.",
                    Type = "appointment",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Check-in thành công! Bệnh nhân đã được đưa vào Hàng chờ lâm sàng của Bác sĩ.",
                appointmentId = id,
                status = "CheckedIn",
                queueNumber = appt.QueueNumber
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private async Task<List<AppointmentResponseDto>> FormatAppointmentListAsync(List<Appointment> list)
    {
        var result = new List<AppointmentResponseDto>();
        if (list == null || list.Count == 0) return result;

        var doctorIds = list.Select(a => a.DoctorId).Distinct().ToList();
        var doctors = await _context.Doctors.Where(d => doctorIds.Contains(d.DoctorId)).ToDictionaryAsync(d => d.DoctorId);

        var patientIds = list.Select(a => a.PatientId).Distinct().ToList();
        var patients = await _context.Patients.Where(p => patientIds.Contains(p.PatientId)).ToDictionaryAsync(p => p.PatientId);

        var specialtyIds = doctors.Values.Where(d => d.SpecialtyId.HasValue).Select(d => d.SpecialtyId!.Value).Distinct().ToList();
        var specialties = await _context.Specialties.Where(s => specialtyIds.Contains(s.SpecialtyId)).ToDictionaryAsync(s => s.SpecialtyId);

        var apptIds = list.Select(a => a.AppointmentId).ToList();
        var invoiceMap = await _context.Invoices.Where(i => apptIds.Contains(i.AppointmentId)).ToDictionaryAsync(i => i.AppointmentId);

        foreach (var appt in list)
        {
            doctors.TryGetValue(appt.DoctorId, out var doctor);
            patients.TryGetValue(appt.PatientId, out var patient);
            Specialty? specialty = null;
            if (doctor?.SpecialtyId != null) specialties.TryGetValue(doctor.SpecialtyId.Value, out specialty);

            string patientName = patient?.FullName ?? $"Bệnh nhân #{appt.PatientId}";
            string patientGender = !string.IsNullOrEmpty(patient?.Gender) ? patient.Gender : "Nam";
            int patientAge = 0;
            if (patient?.DateOfBirth.HasValue == true)
            {
                patientAge = (int)((DateTime.UtcNow - patient.DateOfBirth.Value).TotalDays / 365.25);
                if (patientAge <= 0) patientAge = 0;
            }

            string specName = specialty?.SpecialtyName ?? (appt.Reason?.Contains("-") == true ? appt.Reason.Split('-')[0].Trim() : "Khám tổng quát");
            string docName = doctor?.FullName ?? "BS. CKII Nguyễn Văn A";

            bool isPkg = appt.Note?.Contains("Gói khám:") == true || docName == "Gói Khám Sức Khỏe" || appt.Reason?.Contains("Tầm soát") == true || appt.Reason?.Contains("Khám Tổng Quát") == true;

            // Lấy tổng viện phí thực tế từ Hóa đơn (bao gồm Công khám + Phí thuốc do Bác sĩ kê)
            string feeStr = "250.000đ";
            if (invoiceMap.ContainsKey(appt.AppointmentId))
            {
                feeStr = $"{invoiceMap[appt.AppointmentId].TotalAmount:N0}đ";
            }
            else if (!string.IsNullOrEmpty(appt.Note) && appt.Note.Contains("|"))
            {
                var parts = appt.Note.Split('|');
                foreach (var p in parts)
                {
                    if (p.Trim().EndsWith("đ") || p.Trim().EndsWith("VNĐ") || p.Trim().Contains(".000"))
                    {
                        feeStr = p.Trim();
                        break;
                    }
                }
            }

            if (isPkg)
            {
                if (appt.Reason?.Contains("-") == true)
                {
                    specName = appt.Reason.Split('-')[0].Trim();
                }
                else
                {
                    specName = "Gói Khám Sức Khỏe";
                }
                docName = ""; // Hide doctor for health packages
            }

            string dateStr = appt.AppointmentDate.HasValue
                ? appt.AppointmentDate.Value.ToString("dd/MM/yyyy")
                : appt.CreatedAt.AddHours(7).ToString("dd/MM/yyyy");
            string timeStr = "08:30 - 09:30";

            if (!string.IsNullOrEmpty(appt.Reason))
            {
                if (!appt.AppointmentDate.HasValue)
                {
                    var dateMatch = System.Text.RegularExpressions.Regex.Match(appt.Reason, @"(\d{1,2}/\d{1,2}/\d{4})");
                    if (dateMatch.Success) dateStr = dateMatch.Value;
                }

                var rangeMatch = System.Text.RegularExpressions.Regex.Match(appt.Reason, @"(\d{1,2}:\d{2}\s*-\s*\d{1,2}:\d{2})");
                if (rangeMatch.Success)
                {
                    timeStr = rangeMatch.Value;
                }
                else
                {
                    var singleTimeMatch = System.Text.RegularExpressions.Regex.Match(appt.Reason, @"(\d{1,2}:\d{2})");
                    if (singleTimeMatch.Success)
                    {
                        timeStr = FormatTimeSlot(singleTimeMatch.Value);
                    }
                }
            }

            if (isPkg && !string.IsNullOrEmpty(appt.Note) && appt.Note.Contains("Bệnh nhân:"))
            {
                var parts = appt.Note.Split('|');
                foreach (var p in parts)
                {
                    if (p.Trim().StartsWith("Bệnh nhân:"))
                    {
                        string extracted = p.Trim().Substring("Bệnh nhân:".Length).Trim();
                        if (!string.IsNullOrEmpty(extracted)) patientName = extracted;
                        break;
                    }
                }
            }

            string statusStr = "Confirmed";
            if (appt.StatusId == 6) statusStr = "NoShow";
            else if (appt.StatusId == 5) statusStr = "Cancelled";
            else if (appt.StatusId == 4) statusStr = "Completed";
            else if (appt.StatusId == 9) statusStr = "AwaitingTestResults"; // BS đã chỉ định CLS → đang ở phòng XN/SA
            else if (appt.StatusId == 3) statusStr = "InProgress";
            else if (appt.StatusId == 8) statusStr = "WaitingForDoctor"; // Điều dưỡng đã đo sinh hiệu → chờ BS khám
            else if (appt.StatusId == 7) statusStr = "CheckedIn"; // Lễ Tân đã Check-in → chờ điều dưỡng
            else if (appt.StatusId == 2 || appt.StatusId == 1) statusStr = "Confirmed"; // Chờ bệnh nhân đến Lễ Tân

            if ((appt.StatusId == 1 || appt.StatusId == 2 || appt.StatusId == 3) && !string.IsNullOrEmpty(dateStr))
            {
                if (DateTime.TryParseExact(dateStr, "d/M/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                {
                    if (parsedDate.Date < DateTime.Today)
                    {
                        statusStr = "NoShow";
                    }
                }
            }

            result.Add(new AppointmentResponseDto
            {
                AppointmentId = appt.AppointmentId,
                PatientId = appt.PatientId,
                PatientName = patientName,
                PatientGender = patientGender,
                PatientAge = patientAge,
                Reason = appt.Reason,
                DoctorId = appt.DoctorId,
                DoctorName = docName,
                SpecialtyName = specName,
                Date = dateStr,
                TimeSlot = timeStr,
                Status = statusStr,
                PaymentStatus = invoiceMap.TryGetValue(appt.AppointmentId, out var apptInvoice) ? apptInvoice.PaymentStatus : "unpaid",
                QueueNumber = appt.QueueNumber,
                ClinicRoom = isPkg ? "" : (doctor?.ClinicRoom ?? "Phòng 101"),
                Fee = feeStr,
                IsPackage = isPkg,
                CreatedAt = appt.CreatedAt,
                NurseNote = appt.NurseNote   // Truyền bộ sinh hiệu cho WinForms BS & Điều dưỡng
            });
        }
        return result;
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelAppointment(int id, [FromBody] CancelAppointmentRequest? req = null)
    {
        // Bệnh nhân được tự hủy ĐÚNG lịch hẹn của mình (App Mobile); nhân viên được hủy bất kỳ lịch nào.
        if (!await AccessControl.CanAccessAppointmentAsync(User, _context, id)) return this.ForbidJson();
        try
        {
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == id);
            if (appt != null)
            {
                appt.StatusId = 5; // 5 = Cancelled
                appt.CancelledAt = DateTime.UtcNow;
                // Store who cancelled inside CancelReason (CancelledBy is uuid type, cannot store text)
                var cancellerInfo = !string.IsNullOrEmpty(req?.CancelledBy)
                    ? $"Hủy bởi: {req.CancelledBy}"
                    : "Bệnh nhân hủy lịch qua ứng dụng";
                appt.CancelReason = !string.IsNullOrEmpty(req?.CancelReason)
                    ? $"{req.CancelReason} | {cancellerInfo}"
                    : cancellerInfo;
                // Do NOT write to CancelledBy — it's uuid type in PostgreSQL

                // App Mobile luôn gửi cancelledBy="patient" khi bệnh nhân tự hủy (xem apiService.ts) —
                // chỉ gửi thông báo khi KHÔNG PHẢI bệnh nhân tự hủy (vd: Lễ Tân hủy tại quầy), vì bệnh
                // nhân tự hủy thì không cần báo lại chính họ. Trước đây endpoint này hoàn toàn không gửi
                // thông báo trong mọi trường hợp — bệnh nhân không biết lịch của mình vừa bị hủy bởi nhân viên.
                bool isStaffCancelled = !string.IsNullOrEmpty(req?.CancelledBy) &&
                    !req.CancelledBy.Equals("patient", StringComparison.OrdinalIgnoreCase);
                if (isStaffCancelled)
                {
                    var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId);
                    if (patient != null && patient.UserId != Guid.Empty)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = patient.UserId,
                            Title = "⚠️ Lịch Khám Của Bạn Đã Bị Hủy",
                            Content = $"Lịch hẹn khám ngày hôm nay của bạn đã được Lễ Tân Bệnh viện DTT Healthcare hủy." +
                                      (!string.IsNullOrEmpty(req?.CancelReason) ? $"\n\nLý do: {req.CancelReason}" : "") +
                                      "\n\nVui lòng liên hệ Bệnh viện hoặc đặt lại lịch mới trên ứng dụng nếu cần.",
                            Type = "appointment",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Đã hủy lịch khám thành công." });
            }
            return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error cancelling appointment: " + ex.Message);
            return StatusCode(500, new { success = false, message = "Lỗi khi hủy lịch: " + ex.Message });
        }
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest? req = null)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == id);
            if (appt != null)
            {
                string status = req?.Status ?? "Confirmed";
                var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId);
                Guid targetUserId = patient?.UserId ?? Guid.Empty;

                if (status == "Completed" || status == "4")
                {
                    appt.StatusId = 4; // 4 = Completed
                    if (targetUserId != Guid.Empty)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = targetUserId,
                            Title = "✅ Hoàn Tất Khám Lâm Sàng",
                            Content = "Ca khám bệnh của bạn đã hoàn tất thành công. Vui lòng kiểm tra kết quả hoặc đơn thuốc chỉ định trong hồ sơ y tế.",
                            Type = "result",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                else if (status == "Cancelled" || status == "5")
                {
                    appt.StatusId = 5; // 5 = Cancelled
                    appt.CancelledAt = DateTime.UtcNow;
                    appt.CancelReason = "Bác sĩ trực hủy lịch từ giao diện Desktop";
                    if (targetUserId != Guid.Empty)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = targetUserId,
                            Title = "⚠️ Bác Sĩ Đã Hủy Lịch Khám",
                            Content = "Lịch hẹn khám bệnh của bạn đã được bác sĩ trực chủ động hủy và cập nhật hệ thống do lịch làm việc thay đổi. Vui lòng đặt lại lịch mới!",
                            Type = "appointment",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                else if (status == "InProgress" || status == "3")
                {
                    appt.StatusId = 3; // 3 = InProgress
                    if (targetUserId != Guid.Empty)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = targetUserId,
                            Title = "🔔 Bác Sĩ Đang Gọi Khám",
                            Content = "Bác sĩ trực đang mời bạn vào phòng khám! Vui lòng di chuyển ngay tới trước khu vực phòng khám để bắt đầu.",
                            Type = "appointment",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                else if (status == "CheckedIn" || status == "7")
                {
                    appt.StatusId = 7; // 7 = CheckedIn
                }
                else if (status == "NoShow" || status == "6")
                {
                    appt.StatusId = 6; // 6 = NoShow
                    if (targetUserId != Guid.Empty)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = targetUserId,
                            Title = "⏰ Ghi Nhận Bỏ Khám",
                            Content = "Hệ thống ghi nhận bạn đã không đến khám theo lịch hẹn hôm nay. Nếu vẫn còn nhu cầu khám, vui lòng đặt lại lịch mới trên ứng dụng.",
                            Type = "appointment",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    appt.StatusId = 1; // 1 = Scheduled / Confirmed
                }
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = $"Cập nhật trạng thái thành [{status}] trực tiếp vào CSDL." });
            }

            return NotFound(new { success = false, message = "Không tìm thấy lịch khám trong CSDL." });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error updating status: " + ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private static string FormatTimeSlot(string rawTime)
    {
        if (string.IsNullOrWhiteSpace(rawTime)) return "08:30 - 09:30";
        rawTime = rawTime.Trim();
        if (rawTime.Contains("-")) return rawTime;

        if (TimeSpan.TryParse(rawTime, out TimeSpan ts))
        {
            TimeSpan endTs = ts.Add(TimeSpan.FromHours(1));
            return $"{ts:hh\\:mm} - {endTs:hh\\:mm}";
        }
        return rawTime;
    }

    // ── Điều Dưỡng: Lưu sinh hiệu & chuyển trạng thái sang WaitingForDoctor ──────
    // PUT /api/appointments/{id}/nurse-vitals
    [HttpPut("{id}/nurse-vitals")]
    public async Task<IActionResult> SaveNurseVitals(int id, [FromBody] NurseVitalsRequest req)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == id);
            if (appt == null)
                return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });

            // Chỉ cho phép đo sinh hiệu khi bệnh nhân đã CheckedIn (status=7)
            if (appt.StatusId != 7)
                return BadRequest(new { success = false, message = $"Bệnh nhân chưa Check-in (trạng thái hiện tại: {appt.StatusId}). Điều dưỡng chỉ đo được khi status = CheckedIn (7)." });

            // Tự tính BMI nếu có chiều cao và cân nặng
            double? bmi = null;
            if (req.Height > 0 && req.Weight > 0)
            {
                double heightM = req.Height.Value / 100.0;
                bmi = Math.Round(req.Weight.Value / (heightM * heightM), 1);
            }

            // Đảm bảo status_id=8 'WaitingForDoctor' tồn tại
            bool hasStatus8 = await _context.AppointmentStatuses.AnyAsync(s => s.StatusId == 8);
            if (!hasStatus8)
            {
                _context.AppointmentStatuses.Add(new AppointmentStatus { StatusId = 8, StatusName = "WaitingForDoctor" });
                await _context.SaveChangesAsync();
            }

            // Lưu bộ sinh hiệu dưới dạng JSON vào cột nurse_note
            var nursePayload = new
            {
                bloodPressure  = req.BloodPressure ?? "",
                heartRate      = req.HeartRate ?? 0,
                temperature    = req.Temperature ?? 0,
                weight         = req.Weight ?? 0,
                height         = req.Height ?? 0,
                bmi            = bmi ?? 0,
                nurseNote      = req.NurseNote ?? "",
                measuredAt     = DateTime.UtcNow.ToString("o")
            };
            appt.NurseNote = System.Text.Json.JsonSerializer.Serialize(nursePayload);

            // Chuyển trạng thái → WaitingForDoctor (8)
            appt.StatusId  = 8;
            appt.UpdatedAt = DateTime.UtcNow;

            // Thông báo cho bệnh nhân đã đo xong sinh hiệu, đang chờ bác sĩ gọi vào khám — trước đây thiếu.
            var vitalsPatient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId);
            if (vitalsPatient != null && vitalsPatient.UserId != Guid.Empty)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = vitalsPatient.UserId,
                    Title = "🩺 Đã Đo Sinh Hiệu Xong",
                    Content = "Điều dưỡng đã đo xong chỉ số sinh hiệu của bạn. Vui lòng tiếp tục ngồi chờ tại khu vực phòng khám, Bác sĩ sẽ gọi bạn vào khám trong ít phút nữa.",
                    Type = "appointment",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success  = true,
                message  = $"Đã lưu sinh hiệu và chuyển bệnh nhân sang trạng thái 'Chờ Bác sĩ khám'.",
                bmi      = bmi,
                statusId = 8,
                statusName = "WaitingForDoctor"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error saving nurse vitals: " + ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}

public class CancelAppointmentRequest
{
    public string? CancelReason { get; set; }
    public string? CancelledBy { get; set; }
}

public class UpdateStatusRequest
{
    public string? Status { get; set; }
}

public class NurseVitalsRequest
{
    /// <summary>Huyết áp, ví dụ "120/80"</summary>
    public string? BloodPressure { get; set; }
    /// <summary>Nhịp tim (bpm)</summary>
    public int?    HeartRate     { get; set; }
    /// <summary>Thân nhiệt (°C)</summary>
    public double? Temperature   { get; set; }
    /// <summary>Cân nặng (kg)</summary>
    public double? Weight        { get; set; }
    /// <summary>Chiều cao (cm)</summary>
    public double? Height        { get; set; }
    /// <summary>Ghi chú điều dưỡng tự do</summary>
    public string? NurseNote     { get; set; }
}
