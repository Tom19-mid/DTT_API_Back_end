using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthPackagesController : ControllerBase
{
    private readonly AppDbContext _context;

    public HealthPackagesController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/healthpackages — Danh sách gói khám (lọc theo gender nếu có)
    [HttpGet]
    public async Task<IActionResult> GetAllPackages([FromQuery] string? gender)
    {
        try
        {
            var query = _context.HealthPackages.Include(p => p.Details).AsNoTracking().Where(p => p.IsActive);

            if (!string.IsNullOrEmpty(gender) && gender.ToLower() != "all")
            {
                string g = gender.ToLower();
                query = query.Where(p => p.GenderTarget == "all" || p.GenderTarget == g);
            }

            var list = await query.OrderBy(p => p.PackageId).ToListAsync();

            var result = list.Select(p => new HealthPackageResponseDto
            {
                PackageId = p.PackageId,
                Title = p.Title,
                Description = p.Description,
                Price = p.Price,
                PriceFormatted = FormatPrice(p.Price),
                GenderTarget = p.GenderTarget,
                ImageUrl = p.ImageUrl,
                BookedCount = p.BookedCount,
                BookedCountFormatted = FormatBookedCount(p.BookedCount),
                IsActive = p.IsActive,
                Details = p.Details != null
                    ? p.Details.OrderBy(d => d.SortOrder).Select(d => d.ServiceName).ToList()
                    : new List<string>()
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error getting health packages: " + ex.Message);
            return StatusCode(500, new { message = "Lỗi khi tải danh sách gói khám." });
        }
    }

    // GET /api/healthpackages/{id} — Chi tiết gói khám
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPackageById(int id)
    {
        try
        {
            var pkg = await _context.HealthPackages.Include(p => p.Details).AsNoTracking()
                .FirstOrDefaultAsync(p => p.PackageId == id && p.IsActive);

            if (pkg == null)
                return NotFound(new { message = "Không tìm thấy gói khám." });

            var dto = new HealthPackageResponseDto
            {
                PackageId = pkg.PackageId,
                Title = pkg.Title,
                Description = pkg.Description,
                Price = pkg.Price,
                PriceFormatted = FormatPrice(pkg.Price),
                GenderTarget = pkg.GenderTarget,
                ImageUrl = pkg.ImageUrl,
                BookedCount = pkg.BookedCount,
                BookedCountFormatted = FormatBookedCount(pkg.BookedCount),
                IsActive = pkg.IsActive,
                Details = pkg.Details != null
                    ? pkg.Details.OrderBy(d => d.SortOrder).Select(d => d.ServiceName).ToList()
                    : new List<string>()
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error getting package detail: " + ex.Message);
            return StatusCode(500, new { message = "Lỗi khi tải chi tiết gói khám." });
        }
    }

    // POST /api/healthpackages/{id}/book — Đặt gói khám
    [HttpPost("{id}/book")]
    public async Task<IActionResult> BookPackage(int id, [FromBody] BookPackageRequest req)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, req.PatientId)) return this.ForbidJson();
        try
        {
            var pkg = await _context.HealthPackages.FirstOrDefaultAsync(p => p.PackageId == id && p.IsActive);
            if (pkg == null)
                return NotFound(new { message = "Không tìm thấy gói khám." });

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
                try { await _context.SaveChangesAsync(); } catch { }
            }

            // 2. Validate patient exists in PostgreSQL
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == req.PatientId);
            if (patient == null)
                return BadRequest(new { message = "Không tìm thấy bệnh nhân." });
            int validPatientId = patient.PatientId;

            // 2b. Đặt gói khám cho hồ sơ người thân (giống cơ chế MemberId ở AppointmentsController.
            // CreateAppointment) — xác thực hồ sơ người thân thực sự thuộc về tài khoản đang gọi.
            FamilyMember? familyMember = null;
            if (req.MemberId.HasValue && req.MemberId.Value > 0)
            {
                familyMember = await _context.FamilyMembers.FirstOrDefaultAsync(m => m.MemberId == req.MemberId.Value);
                if (familyMember == null || familyMember.OwnerPatientId != validPatientId)
                    return BadRequest(new { message = "Hồ sơ người thân không hợp lệ hoặc không thuộc tài khoản này." });
            }
            string bookingPatientName = familyMember?.FullName ?? (!string.IsNullOrWhiteSpace(req.PatientName) ? req.PatientName : (patient.FullName ?? "Bệnh nhân"));

            // 2c. Gói khám có thể giới hạn theo giới tính (GenderTarget: "male"/"female"/"all") — trước
            // đây chỉ dùng để LỌC danh sách hiển thị (GET /api/healthpackages?gender=...), backend không
            // hề kiểm tra lại khi thật sự ĐẶT gói, nên gọi thẳng API vẫn đặt được gói "Nữ" cho bệnh nhân
            // Nam (đã xác nhận qua test thật) — không hợp lý về mặt lâm sàng. Kiểm tra theo đúng giới
            // tính của NGƯỜI ĐƯỢC ĐẶT KHÁM (người thân nếu có memberId, không phải luôn chủ tài khoản).
            string bookingGender = familyMember?.Gender ?? patient.Gender ?? "";
            if (!string.IsNullOrEmpty(pkg.GenderTarget) &&
                !pkg.GenderTarget.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(bookingGender) &&
                !pkg.GenderTarget.Equals(bookingGender, StringComparison.OrdinalIgnoreCase))
            {
                string targetLabel = pkg.GenderTarget.Equals("male", StringComparison.OrdinalIgnoreCase) ? "Nam" : "Nữ";
                return BadRequest(new { message = $"Gói khám '{pkg.Title}' chỉ dành cho {targetLabel}, không phù hợp với giới tính của {bookingPatientName}." });
            }

            // 3. Obtain target specialty & doctor matching the health package domain
            int targetSpecialtyId = 1;
            string titleLower = pkg.Title.ToLower();
            if (titleLower.Contains("tim mạch")) targetSpecialtyId = 5;
            else if (titleLower.Contains("xương khớp") || titleLower.Contains("cột sống")) targetSpecialtyId = 4;
            else if (titleLower.Contains("sản") || titleLower.Contains("phụ khoa") || titleLower.Contains("nữ") || titleLower.Contains("vú")) targetSpecialtyId = 3;
            else if (titleLower.Contains("nhi") || titleLower.Contains("trẻ em")) targetSpecialtyId = 2;
            else if (titleLower.Contains("thần kinh")) targetSpecialtyId = 6;
            else if (titleLower.Contains("da liễu")) targetSpecialtyId = 7;
            else if (titleLower.Contains("x-quang") || titleLower.Contains("x quang") || titleLower.Contains("ct") || titleLower.Contains("mri") || titleLower.Contains("chẩn đoán hình ảnh") || titleLower.Contains("siêu âm")) targetSpecialtyId = 8;
            else if (titleLower.Contains("răng") || titleLower.Contains("hàm mặt") || titleLower.Contains("nha khoa")) targetSpecialtyId = 9;
            else if (titleLower.Contains("tai") || titleLower.Contains("mũi") || titleLower.Contains("họng") || titleLower.Contains("tmh")) targetSpecialtyId = 10;
            else if (titleLower.Contains("mắt") || titleLower.Contains("nhãn khoa")) targetSpecialtyId = 11;

            var targetSpecObj = await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == targetSpecialtyId);
            string specName = targetSpecObj?.SpecialtyName ?? "Nội tổng quát";

            // !IsTestData: tránh tự động xếp gói khám vào 1 hồ sơ bác sĩ "dữ liệu test" (Admin đánh dấu
            // để QA thử nghiệm, vd "BS. Điều trị") — các hồ sơ này thường vẫn Status="Active" nên trước
            // đây vẫn lọt vào được nhánh khớp đầu tiên nếu tình cờ đứng trước trong bảng.
            var matchedDoctor = await _context.Doctors.FirstOrDefaultAsync(d => d.SpecialtyId == targetSpecialtyId && d.Status == "Active" && !d.IsTestData)
                              ?? await _context.Doctors.FirstOrDefaultAsync(d => d.SpecialtyId == targetSpecialtyId && !d.IsTestData)
                              ?? await _context.Doctors.FirstOrDefaultAsync(d => d.Status == "Active" && !d.IsTestData)
                              ?? await _context.Doctors.FirstOrDefaultAsync(d => !d.IsTestData)
                              ?? await _context.Doctors.FirstOrDefaultAsync();
            if (matchedDoctor == null)
                return BadRequest(new { message = "Không tìm thấy bác sĩ phù hợp để xếp lịch." });
            int validDoctorId = matchedDoctor.DoctorId;

            // 4. Determine preferred date & preferred time
            DateOnly apptDateForPackage;
            if (!string.IsNullOrEmpty(req.PreferredDate) &&
                DateOnly.TryParseExact(req.PreferredDate, new[] { "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "M/d/yyyy" },
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedPkgDate))
            {
                apptDateForPackage = parsedPkgDate;
            }
            else
            {
                apptDateForPackage = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).AddDays(1)); // mặc định: ngày mai (giờ VN)
            }
            string preferredDateStr = apptDateForPackage.ToString("dd/MM/yyyy");

            TimeSpan reqStart = new TimeSpan(8, 30, 0);
            TimeSpan reqEnd = new TimeSpan(9, 30, 0);
            if (!string.IsNullOrWhiteSpace(req.PreferredTimeSlot))
            {
                var parts = req.PreferredTimeSlot.Split('-');
                if (parts.Length > 0 && TimeSpan.TryParse(parts[0].Trim(), out var parsedS))
                {
                    reqStart = parsedS;
                    reqEnd = reqStart.Add(TimeSpan.FromHours(1));
                }
                if (parts.Length > 1 && TimeSpan.TryParse(parts[1].Trim(), out var parsedE))
                {
                    reqEnd = parsedE;
                }
            }

            // 5. Ensure doctor_schedules and doctor_schedule_slots for validDoctorId on apptDateForPackage
            int validSlotId = 0;
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            DateTime workDateDb = apptDateForPackage.ToDateTime(TimeOnly.MinValue);

            using (var schedCmd = conn.CreateCommand())
            {
                // Một bác sĩ có thể có NHIỀU dòng doctor_schedules trong cùng 1 ngày (ca sáng + ca chiều
                // tách rời) — trước đây "LIMIT 1" lấy ĐẠI 1 dòng bất kỳ, có thể không phải ca chứa khung
                // giờ bệnh nhân mong muốn (reqStart/reqEnd), giống lỗi đã sửa ở AppointmentsController.
                // Ưu tiên đúng ca chứa reqStart nếu có, không thì tạm lấy dòng đầu tiên (giữ hành vi cũ
                // cho trường hợp không khớp ca nào — gói khám vốn không bắt buộc khớp giờ chính xác).
                schedCmd.CommandText = "SELECT schedule_id, start_time, end_time FROM doctor_schedules WHERE doctor_id = @dId AND work_date = @wDate ORDER BY start_time ASC";
                var pD = schedCmd.CreateParameter(); pD.ParameterName = "@dId"; pD.Value = validDoctorId; schedCmd.Parameters.Add(pD);
                var pW = schedCmd.CreateParameter(); pW.ParameterName = "@wDate"; pW.Value = workDateDb; schedCmd.Parameters.Add(pW);
                int scId = 0;
                using (var schedReader = await schedCmd.ExecuteReaderAsync())
                {
                    int? firstRowId = null;
                    while (await schedReader.ReadAsync())
                    {
                        int rowId = schedReader.GetInt32(0);
                        firstRowId ??= rowId;
                        var rowStart = GetTimeSpanValue(schedReader, 1);
                        var rowEnd = GetTimeSpanValue(schedReader, 2);
                        if (reqStart >= rowStart && reqStart < rowEnd)
                        {
                            scId = rowId;
                            break;
                        }
                    }
                    if (scId == 0 && firstRowId.HasValue) scId = firstRowId.Value;
                }

                if (scId == 0)
                {
                    using var insSc = conn.CreateCommand();
                    insSc.CommandText = "INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time, created_at, updated_at) VALUES (@dId, @wDate, '07:30:00', '17:00:00', NOW(), NOW()) RETURNING schedule_id";
                    var ipD = insSc.CreateParameter(); ipD.ParameterName = "@dId"; ipD.Value = validDoctorId; insSc.Parameters.Add(ipD);
                    var ipW = insSc.CreateParameter(); ipW.ParameterName = "@wDate"; ipW.Value = workDateDb; insSc.Parameters.Add(ipW);
                    var newSc = await insSc.ExecuteScalarAsync();
                    if (newSc != null && newSc != DBNull.Value) scId = Convert.ToInt32(newSc);
                }

                if (scId > 0)
                {
                    using var slotCmd = conn.CreateCommand();
                    slotCmd.CommandText = @"SELECT slot_id FROM doctor_schedule_slots 
                                            WHERE schedule_id = @sId AND status = 'Available' 
                                              AND slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL) 
                                            ORDER BY start_time ASC LIMIT 1";
                    var pS = slotCmd.CreateParameter(); pS.ParameterName = "@sId"; pS.Value = scId; slotCmd.Parameters.Add(pS);
                    var slVal = await slotCmd.ExecuteScalarAsync();
                    if (slVal != null && slVal != DBNull.Value) validSlotId = Convert.ToInt32(slVal);

                    if (validSlotId == 0)
                    {
                        using var insSlot = conn.CreateCommand();
                        insSlot.CommandText = @"INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status, created_at, updated_at) 
                                                VALUES (@sId, (SELECT COALESCE(MAX(slot_order), 0) + 1 FROM doctor_schedule_slots WHERE schedule_id = @sId), @sTime, @eTime, 'Available', NOW(), NOW()) 
                                                RETURNING slot_id";
                        var ipS = insSlot.CreateParameter(); ipS.ParameterName = "@sId"; ipS.Value = scId; insSlot.Parameters.Add(ipS);
                        var ipSt = insSlot.CreateParameter(); ipSt.ParameterName = "@sTime"; ipSt.Value = reqStart; insSlot.Parameters.Add(ipSt);
                        var ipEt = insSlot.CreateParameter(); ipEt.ParameterName = "@eTime"; ipEt.Value = reqEnd; insSlot.Parameters.Add(ipEt);
                        var newSl = await insSlot.ExecuteScalarAsync();
                        if (newSl != null && newSl != DBNull.Value) validSlotId = Convert.ToInt32(newSl);
                    }
                }
            }

            if (validSlotId == 0)
                return BadRequest(new { success = false, message = "Không thể khởi tạo khung giờ khám cho gói dịch vụ." });

            // Tăng số lượt đặt CHỈ sau khi đã qua hết các bước xác thực có thể BadRequest ở trên (gói
            // không tồn tại, bệnh nhân/người thân không hợp lệ, sai giới tính, không có bác sĩ/slot phù
            // hợp) — trước đây tăng ngay từ đầu hàm, nên mọi lượt đặt THẤT BẠI cũng làm BookedCount (hiển
            // thị công khai cho bệnh nhân khác) tăng lên sai lệch.
            pkg.BookedCount += 1;
            pkg.UpdatedAt = DateTime.UtcNow;
            try { await _context.SaveChangesAsync(); } catch { }

            // 6. Create appointment record
            // Số thứ tự hàng đợi phải tính theo hàng đợi THẬT của bác sĩ trong ngày khám đó
            // (giống cách lễ tân tính khi check-in), KHÔNG phải tổng số lịch hẹn lịch sử của bệnh nhân.
            int queueNum = (await _context.Appointments.CountAsync(a =>
                a.DoctorId == validDoctorId && a.AppointmentDate == apptDateForPackage &&
                a.StatusId != 5 && a.StatusId != 6)) + 1; // loại Cancelled(5)/NoShow(6)
            int newAppointmentId = 0;

            string timeSlotSuffix = !string.IsNullOrWhiteSpace(req.PreferredTimeSlot) ? $" ({req.PreferredTimeSlot})" : "";
            string bookingReason = $"{specName} - Gói: {pkg.Title} - {preferredDateStr}{timeSlotSuffix}";
            string noteContent = $"Gói khám: {pkg.Title} | {FormatPrice(pkg.Price)} | Khung giờ: {req.PreferredTimeSlot ?? "08:00 - 17:00"} | Bệnh nhân: {bookingPatientName}";

            var appointment = new Appointment
            {
                PatientId = validPatientId,
                MemberId = familyMember?.MemberId,
                DoctorId = validDoctorId,
                SlotId = validSlotId,
                Reason = bookingReason,
                Note = noteContent,
                StatusId = 1, // Confirmed
                QueueNumber = queueNum,
                AppointmentDate = apptDateForPackage,
                CreatedAt = DateTime.UtcNow
            };

            _context.Appointments.Add(appointment);
            await _context.SaveChangesAsync();
            newAppointmentId = appointment.AppointmentId;

            // queueNum (tính trước lúc INSERT) chỉ là giá trị GỬI XUỐNG — trigger CSDL
            // trg_set_queue_number (BEFORE INSERT) tự ý gán lại queue_number bằng slot_order của
            // slot đã chọn, ĐÈ LÊN giá trị C# vừa gửi, và EF không tự đọc lại giá trị đã bị trigger
            // sửa. Đọc lại trực tiếp để số thứ tự trả về khớp đúng số thật trong DB (giống lỗi đã sửa
            // ở AppointmentsController.CreateAppointment).
            int finalQueueNum = queueNum;
            if (newAppointmentId > 0)
            {
                try
                {
                    using var qCmd = conn.CreateCommand();
                    qCmd.CommandText = "SELECT queue_number FROM appointments WHERE appointment_id = @id";
                    var qP = qCmd.CreateParameter(); qP.ParameterName = "@id"; qP.Value = newAppointmentId; qCmd.Parameters.Add(qP);
                    var qVal = await qCmd.ExecuteScalarAsync();
                    if (qVal != null && qVal != DBNull.Value) finalQueueNum = Convert.ToInt32(qVal);
                }
                catch (Exception qEx)
                {
                    Console.WriteLine("Reload queue_number after insert failed: " + qEx.Message);
                }
            }

            // 7. Push notification
            var pkgPatient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == validPatientId);
            if (pkgPatient != null && pkgPatient.UserId != Guid.Empty)
            {
                string timeNotice = !string.IsNullOrWhiteSpace(req.PreferredTimeSlot) ? $", khung giờ {req.PreferredTimeSlot}" : "";
                _context.Notifications.Add(new Notification
                {
                    UserId = pkgPatient.UserId,
                    Title = "✅ Đặt Gói Khám Sức Khỏe Thành Công",
                    Content = $"Bạn đã đặt thành công gói khám '{pkg.Title}' cho {(familyMember != null ? bookingPatientName : "bạn")} (giá {FormatPrice(pkg.Price)}), dự kiến khám ngày {apptDateForPackage:dd/MM/yyyy}{timeNotice}. Số thứ tự dự kiến: {finalQueueNum}.\n\nVui lòng đến bệnh viện đúng ngày hẹn để làm thủ tục tiếp đón.",
                    Type = "appointment",
                    RelatedId = newAppointmentId > 0 ? newAppointmentId : (int?)null,
                    RelatedType = "appointment",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                try { await _context.SaveChangesAsync(); } catch { }
            }

            return Ok(new
            {
                success = true,
                message = $"Đã đặt gói khám '{pkg.Title}' thành công!",
                appointmentId = newAppointmentId > 0 ? newAppointmentId : queueNum,
                packageTitle = pkg.Title,
                priceFormatted = FormatPrice(pkg.Price),
                preferredDate = apptDateForPackage.ToString("dd/MM/yyyy"),
                queueNumber = finalQueueNum
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error booking package: " + ex.Message);
            return StatusCode(500, new { message = "Lỗi đặt gói khám: " + ex.Message });
        }
    }

    // Helper: đọc cột time (start_time/end_time) an toàn bất kể driver trả về TimeSpan hay DateTime
    private static TimeSpan GetTimeSpanValue(System.Data.Common.DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        if (value is TimeSpan ts) return ts;
        if (value is DateTime dt) return dt.TimeOfDay;
        return TimeSpan.TryParse(value?.ToString(), out var parsed) ? parsed : TimeSpan.Zero;
    }

    // Helper: Format price as Vietnamese currency string
    private static string FormatPrice(decimal price)
    {
        return price.ToString("N0").Replace(",", ".") + "đ";
    }

    // Helper: Format booked count with k+ notation
    private static string FormatBookedCount(int count)
    {
        if (count >= 1000)
            return $"{count / 1000}.{(count % 1000) / 100}k+";
        return $"{count}+";
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class HealthPackageResponseDto
{
    public int PackageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public string GenderTarget { get; set; } = "all";
    public string? ImageUrl { get; set; }
    public int BookedCount { get; set; }
    public string BookedCountFormatted { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Details { get; set; } = new();
}

public class BookPackageRequest
{
    public int PatientId { get; set; }
    /// <summary>Đặt gói khám cho hồ sơ người thân (family_members.member_id) thay vì chính chủ tài khoản.</summary>
    public int? MemberId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PreferredDate { get; set; }
    public string? PreferredTimeSlot { get; set; }
    public string? PriceFormatted { get; set; }
}