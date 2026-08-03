using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;
using System.Linq;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly AppDbContext _context;

    public InvoicesController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/Invoices/estimate/{appointmentId}
    // Trả về phí khám + phí thuốc THẬT tính từ đơn thuốc điện tử (prescription_details x medicines.unit_price)
    // Dùng để hiển thị đúng số tiền trên màn Thanh Toán TRƯỚC khi Lễ Tân bấm xác nhận thu tiền.
    [HttpGet("estimate/{appointmentId}")]
    public async Task<IActionResult> GetEstimate(int appointmentId)
    {
        if (!await AccessControl.CanAccessAppointmentAsync(User, _context, appointmentId)) return this.ForbidJson();

        var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
        if (appt == null) return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });

        // Gói khám sức khỏe có giá TRỌN GÓI riêng (vd: 1.200.000đ) cho các hạng mục nằm TRONG gói,
        // khác hẳn mô hình "phí khám 250k" của ca khám chuyên khoa thông thường.
        // LƯU Ý: giá gói KHÔNG tự động bao gồm thuốc — nếu bác sĩ kê thêm thuốc ngoài phạm vi gói
        // (vd: giảm đau cho chẩn đoán phát sinh), vẫn phải cộng thêm đúng chi phí thuốc thật đó,
        // không được mặc định = 0 (nếu không sẽ vô tình phát thuốc miễn phí cho bệnh nhân).
        bool isPackage = appt.Note?.Contains("Gói khám:") == true;
        decimal examFee = isPackage ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
        decimal servicesFee = await ComputeClsFeeAsync(appointmentId);
        decimal medsFee = await ComputeMedsFeeAsync(appointmentId);

        return Ok(new
        {
            success = true,
            appointmentId,
            isPackage,
            examFee,
            servicesFee,
            medsFee,
            totalAmount = examFee + servicesFee + medsFee,
            hasPrescription = medsFee > 0
        });
    }

    // Đọc giá tiền THẬT được lưu trong appointments.note lúc đặt gói khám:
    // "Gói khám: {title} | {giá đã format vd 1.200.000đ} | Bệnh nhân: {tên}"
    private static decimal? ExtractPriceFromNote(string? note)
    {
        if (string.IsNullOrEmpty(note) || !note.Contains("|")) return null;
        foreach (var part in note.Split('|'))
        {
            var trimmed = part.Trim();
            if (trimmed.EndsWith("đ") || trimmed.EndsWith("VNĐ") || trimmed.Contains(".000"))
            {
                var digits = new string(trimmed.Where(char.IsDigit).ToArray());
                if (decimal.TryParse(digits, out decimal val) && val > 0) return val;
            }
        }
        return null;
    }

    // Tính tổng tiền thuốc thật từ đơn thuốc điện tử gắn với appointment (nếu có)
    private async Task<decimal> ComputeMedsFeeAsync(int appointmentId)
    {
        var medRecord = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == appointmentId);
        if (medRecord == null) return 0m;

        var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.MedicalRecordId == medRecord.MedicalRecordId);
        if (prescription == null) return 0m;

        var details = await _context.PrescriptionDetails.Where(d => d.PrescriptionId == prescription.PrescriptionId).ToListAsync();
        if (details.Count == 0) return 0m;

        var medIds = details.Select(d => d.MedicineId).ToList();
        var medDict = await _context.Medicines.Where(m => medIds.Contains(m.MedicineId)).ToDictionaryAsync(m => m.MedicineId);
        return details.Sum(d => d.Quantity * (medDict.ContainsKey(d.MedicineId) && medDict[d.MedicineId].Price > 0 ? medDict[d.MedicineId].Price : 15000m));
    }

    // Tính phí Xét nghiệm/Siêu âm THẬT đã được Bác sĩ chỉ định (clinical_services.unit_price qua service_id) —
    // trước đây servicesFee luôn = 0 cố định ở đây dù ConfirmPayment đã có sẵn tham số ServicesFee,
    // khiến phí CLS bị "biến mất" khỏi hóa đơn cuối cùng dù đã được cộng lúc bác sĩ hoàn tất khám.
    private async Task<decimal> ComputeClsFeeAsync(int appointmentId)
    {
        var medRecord = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == appointmentId);
        if (medRecord == null) return 0m;

        var serviceIds = new List<int>();
        serviceIds.AddRange(await _context.MedicalTests.Where(t => t.MedicalRecordId == medRecord.MedicalRecordId && t.ServiceId != null).Select(t => t.ServiceId!.Value).ToListAsync());
        serviceIds.AddRange(await _context.UltrasoundResults.Where(u => u.MedicalRecordId == medRecord.MedicalRecordId && u.ServiceId != null).Select(u => u.ServiceId!.Value).ToListAsync());
        if (serviceIds.Count == 0) return 0m;

        return await _context.ClinicalServices.Where(s => serviceIds.Contains(s.ServiceId)).SumAsync(s => s.UnitPrice);
    }

    // POST /api/Invoices/confirm-payment
    // Lễ Tân xác nhận đã thu tiền → Cập nhật Invoice pending → paid → Gửi thông báo App Mobile
    [HttpPost("confirm-payment")]
    public async Task<IActionResult> ConfirmPayment([FromBody] ConfirmPaymentDto dto)
    {
        // Chỉ Lễ Tân/nhân viên mới được xác nhận đã thu tiền — bệnh nhân không được tự đánh dấu
        // hóa đơn của mình là "đã thanh toán" mà không thực sự trả tiền tại quầy.
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            // 1. Tìm appointment và patient
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == dto.AppointmentId);
            if (appt == null) return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == (dto.PatientId > 0 ? dto.PatientId : appt.PatientId));

            // 2. Kiểm tra đã có invoice paid chưa
            var existingPaidInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == dto.AppointmentId && i.PaymentStatus == "paid");
            if (existingPaidInvoice != null)
            {
                return Ok(new { success = true, message = "Hóa đơn đã được thanh toán trước đó.", invoiceId = existingPaidInvoice.InvoiceId, alreadyPaid = true });
            }

            // 3. Tính tổng tiền từ các khoản (khám + CLS + thuốc) — LUÔN tính lại phía server từ dữ liệu
            // thật trong DB, KHÔNG tin số tiền client (Lễ Tân/WinForms) gửi lên, để tránh hóa đơn bị
            // chỉnh sửa tùy ý qua request giả mạo.
            bool isPackage = appt.Note?.Contains("Gói khám:") == true;
            decimal examFee = isPackage ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
            decimal servicesFee = await ComputeClsFeeAsync(dto.AppointmentId);
            decimal medsFee = await ComputeMedsFeeAsync(dto.AppointmentId);

            decimal totalAmount = examFee + servicesFee + medsFee;

            Invoice invoice;

            // 5. Tim invoice unpaid (tao tu MedicalRecordsController khi bac si hoan tat) — DB
            // chk_payment_status chi cho phep 'unpaid'/'partial'/'paid', khong co 'pending'.
            var pendingInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == dto.AppointmentId && i.PaymentStatus == "unpaid");
            if (pendingInvoice != null)
            {
                invoice = pendingInvoice;
                // Xoa cac items cu cua pending invoice de cap nhat items moi trung khep 100%
                var oldItems = await _context.InvoiceItems.Where(item => item.InvoiceId == invoice.InvoiceId).ToListAsync();
                if (oldItems.Count > 0)
                {
                    _context.InvoiceItems.RemoveRange(oldItems);
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                // Tao Invoice moi (truong hop le tan thu tien truoc khi bac si hoan tat)
                invoice = new Invoice
                {
                    AppointmentId = dto.AppointmentId,
                    PatientId = patient?.PatientId ?? appt.PatientId,
                    TotalAmount = totalAmount,
                    PaidAmount = totalAmount,
                    PaymentStatus = "paid",
                    PaymentMethod = dto.PaymentMethod ?? "cash",
                    InvoiceDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Invoices.Add(invoice);
                await _context.SaveChangesAsync();
            }

            // 6. Tạo các InvoiceItems chi tiết khớp 100% với tổng tiền
            var items = new List<InvoiceItem>();
            items.Add(new InvoiceItem
            {
                InvoiceId = invoice.InvoiceId,
                ItemName = "Công khám lâm sàng chuyên khoa",
                ItemType = "exam",
                Quantity = 1,
                UnitPrice = examFee,
                Amount = examFee,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            if (servicesFee > 0)
                items.Add(new InvoiceItem { InvoiceId = invoice.InvoiceId, ItemName = "Phí dịch vụ Cận lâm sàng (CLS)", ItemType = "service", Quantity = 1, UnitPrice = servicesFee, Amount = servicesFee, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            if (medsFee > 0)
                items.Add(new InvoiceItem { InvoiceId = invoice.InvoiceId, ItemName = "Phí thuốc theo Đơn thuốc điện tử", ItemType = "medicine", Quantity = 1, UnitPrice = medsFee, Amount = medsFee, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            
            _context.InvoiceItems.AddRange(items);
            await _context.SaveChangesAsync();

            // Cập nhật invoice pending → paid (đảm bảo TotalAmount = PaidAmount = sum of items)
            invoice.TotalAmount = totalAmount;
            invoice.PaidAmount = totalAmount;
            invoice.PaymentStatus = "paid";
            invoice.PaymentMethod = dto.PaymentMethod ?? "cash";
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // LƯU Ý: KHÔNG đổi appt.StatusId ở đây nữa. Trước đây code gán appt.StatusId = 5 với ý định
            // đánh dấu "Paid", nhưng trong bảng appointment_statuses thật, status_id=5 nghĩa là "Cancelled"!
            // Hậu quả: mọi nơi hiển thị (App Mobile, WinForms) đọc appointment status_id=5 sẽ hiểu nhầm
            // lịch hẹn đã bị HỦY. Đồng thời status_id đó rất dễ bị ghi đè lại (vd: bác sĩ lưu lại bệnh án
            // sẽ set về 4=Completed), khiến hóa đơn ĐÃ THANH TOÁN bị hiện lại như "chưa thu phí" trên màn
            // Lễ Tân. Trạng thái thanh toán giờ chỉ cần đọc từ invoices.payment_status (nguồn dữ liệu thật
            // duy nhất) — xem AppointmentResponseDto.PaymentStatus.

            // 7. Gửi thông báo lên App Mobile của bệnh nhân
            if (patient != null && patient.UserId != Guid.Empty)
            {
                string totalFormatted = totalAmount.ToString("N0") + " VNĐ";
                _context.Notifications.Add(new Notification
                {
                    UserId = patient.UserId,
                    Title = "Hóa Đơn Viện Phí Đã Được Xác Nhận",
                    Content = $"Hóa đơn khám chữa bệnh của bạn ({patient.FullName}) đã được xác nhận thanh toán thành công tại Quầy Thu Ngân DTT Healthcare.\n\nTỔNG TIỀN: {totalFormatted}\nHình thức: {(dto.PaymentMethod == "transfer" ? "Chuyển khoản" : "Tiền mặt tại quầy")}\n\nVui lòng vào mục Hồ Sơ Y Tế → Hóa Đơn để xem chi tiết.",
                    Type = "result",
                    RelatedId = invoice.InvoiceId,
                    RelatedType = "invoice",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã xác nhận thanh toán và cập nhật hóa đơn thành công!",
                invoiceId = invoice.InvoiceId,
                totalAmount,
                paymentStatus = "paid",
                notified = patient?.UserId != Guid.Empty
            });
        }
        catch (Exception ex)

        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // GET /api/Invoices/by-appointment/{appointmentId}
    // Lấy hóa đơn theo lịch hẹn (kiểm tra đã thanh toán chưa)
    [HttpGet("by-appointment/{appointmentId}")]
    public async Task<IActionResult> GetByAppointment(int appointmentId)
    {
        if (!await AccessControl.CanAccessAppointmentAsync(User, _context, appointmentId)) return this.ForbidJson();
        try
        {
            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == appointmentId);
            if (invoice == null) return Ok(new { success = true, invoice = (object?)null, isPaid = false });

            var items = await _context.InvoiceItems.Where(i => i.InvoiceId == invoice.InvoiceId).ToListAsync();

            return Ok(new
            {
                success = true,
                isPaid = invoice.PaymentStatus == "paid",
                invoice = new
                {
                    invoiceId = invoice.InvoiceId,
                    totalAmount = invoice.TotalAmount,
                    paidAmount = invoice.PaidAmount,
                    paymentStatus = invoice.PaymentStatus,
                    paymentMethod = invoice.PaymentMethod,
                    invoiceDate = invoice.InvoiceDate.ToString("dd/MM/yyyy HH:mm"),
                    items = items.Select(i => new { i.ItemName, i.ItemType, i.Quantity, i.UnitPrice, i.Amount })
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // POST /api/Invoices/register-walkin
    // Lễ Tân tạo hồ sơ bệnh nhân vãng lai → tạo Patient record + giả lập gửi SMS thông báo tài khoản App
    [HttpPost("register-walkin")]
    public async Task<IActionResult> RegisterWalkIn([FromBody] RegisterWalkInDto dto)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Phone))
                return BadRequest(new { success = false, message = "Vui lòng nhập đầy đủ Họ tên và Số điện thoại!" });

            if (string.IsNullOrWhiteSpace(dto.CccdNumber))
                return BadRequest(new { success = false, message = "Vui lòng nhập Số CCCD sau khi đối chiếu thẻ cứng!" });

            // Tạo mật khẩu tạm thời dựa trên SĐT (4 số cuối)
            string lastFour = dto.Phone.Length >= 4 ? dto.Phone.Substring(dto.Phone.Length - 4) : "0000";
            string randomPwd = $"DTT@{lastFour}";

            // Kiểm tra xem User đã tồn tại theo SĐT chưa để tránh trùng lặp
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
            Guid targetUserId;
            string csvPasswordHash = existingUser?.PasswordHash ?? string.Empty;
            int csvRoleId = existingUser?.RoleId ?? 3;
            if (existingUser != null)
            {
                targetUserId = existingUser.UserId;
            }
            else
            {
                var newUser = new User
                {
                    Email = $"{dto.Phone}@gmail.com", // Dùng @gmail.com để đáp ứng check constraint users_email_check
                    PhoneNumber = dto.Phone,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(randomPwd),
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var patientRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Patient");
                if (patientRole != null) newUser.RoleId = patientRole.RoleId;

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();
                targetUserId = newUser.UserId;
                csvPasswordHash = newUser.PasswordHash;
                csvRoleId = newUser.RoleId;
            }

            // Tìm hồ sơ Patient sẵn có hoặc tạo mới
            var existingPatient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == targetUserId || p.PhoneNumber == dto.Phone);
            int targetPatientId;
            if (existingPatient != null)
            {
                // DB có trigger trg_create_profile_on_user_insert tự tạo sẵn 1 dòng patients rỗng
                // (phone_number NULL) ngay khi tạo users mới — nhánh update này trước đây quên gán lại
                // PhoneNumber, nên bệnh nhân vãng lai luôn có users.phone_number đúng nhưng
                // patients.phone_number NULL vĩnh viễn (lộ ra ở màn "Lịch Sử Hồ Sơ Bệnh Án" — cột SĐT
                // trống dù đăng ký có nhập số điện thoại).
                existingPatient.FullName = dto.FullName;
                existingPatient.PhoneNumber = dto.Phone;
                existingPatient.CccdNumber = dto.CccdNumber;
                if (!string.IsNullOrEmpty(dto.BhytNumber)) existingPatient.HealthInsuranceNumber = dto.BhytNumber;
                existingPatient.VerificationStatus = "verified";
                existingPatient.VerifiedAt = DateTime.UtcNow;
                existingPatient.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                targetPatientId = existingPatient.PatientId;
            }
            else
            {
                var receptionistUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 4 || u.Email == "letan.minhchau@gmail.com");
                DateTime? dob = null;
                if (!string.IsNullOrEmpty(dto.DateOfBirth) && DateTime.TryParse(dto.DateOfBirth, out DateTime parsedDob))
                {
                    // Ngày sinh là optional (lễ tân có thể chưa hỏi kịp), nhưng NẾU có nhập thì phải hợp lý —
                    // chặn ngày tương lai và tuổi phi thực tế thay vì chấp nhận bất kỳ ngày nào parse được.
                    if (parsedDob.Date > DateTime.UtcNow.Date)
                        return BadRequest(new { success = false, message = "Ngày sinh không được ở tương lai." });
                    if (parsedDob.Date < DateTime.UtcNow.Date.AddYears(-120))
                        return BadRequest(new { success = false, message = "Ngày sinh không hợp lệ (quá 120 năm trước)." });
                    dob = parsedDob;
                }

                var newPatient = new Patient
                {
                    UserId = targetUserId,
                    FullName = dto.FullName,
                    PhoneNumber = dto.Phone,
                    Gender = dto.Gender ?? "Nam",
                    DateOfBirth = dob,
                    Address = dto.Address,
                    CccdNumber = dto.CccdNumber,
                    HealthInsuranceNumber = dto.BhytNumber,
                    VerificationStatus = "verified",
                    VerifiedBy = receptionistUser?.UserId ?? Guid.Parse("ddb25ca6-80c8-434d-a05a-d4231c25e95b"),
                    VerifiedAt = DateTime.UtcNow,
                    VerificationNote = $"Tạo hồ sơ vãng lai tại Quầy Lễ Tân. Đã đối chiếu CCCD thực tế. Ngày: {DateTime.Now:dd/MM/yyyy HH:mm}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Patients.Add(newPatient);
                await _context.SaveChangesAsync();
                targetPatientId = newPatient.PatientId;
            }

            // Tạo appointment vãng lai nếu có chuyên khoa
            int newAppointmentId = 0;
            if (dto.DoctorId > 0)
            {
                int validSlotId = 0;
                try
                {
                    var conn = _context.Database.GetDbConnection();
                    if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

                    // CHỈ chọn slot đang RẢNH (không bị appointment active nào chiếm) — trước đây dùng
                    // "ORDER BY slot_id DESC LIMIT 1" lấy bừa slot mới nhất của bác sĩ bất kể còn trống
                    // hay không, nên khi bác sĩ đã có 1 lịch hẹn active dùng đúng slot đó, lần đăng ký
                    // vãng lai tiếp theo cho cùng bác sĩ sẽ đụng UNIQUE constraint idx_appointments_slot_active.
                    using var slotCmd = conn.CreateCommand();
                    slotCmd.CommandText = @"
                        SELECT s.slot_id
                        FROM doctor_schedule_slots s
                        JOIN doctor_schedules ds ON s.schedule_id = ds.schedule_id
                        WHERE ds.doctor_id = @docId
                          AND s.slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL AND is_active = true)
                        ORDER BY s.slot_id DESC
                        LIMIT 1";
                    var pDoc = slotCmd.CreateParameter(); pDoc.ParameterName = "@docId"; pDoc.Value = dto.DoctorId; slotCmd.Parameters.Add(pDoc);
                    var val = await slotCmd.ExecuteScalarAsync();
                    if (val != null && val != DBNull.Value) validSlotId = Convert.ToInt32(val);

                    if (validSlotId == 0)
                    {
                        int scId = 0;
                        using var schedCheck = conn.CreateCommand();
                        schedCheck.CommandText = "SELECT schedule_id FROM doctor_schedules WHERE doctor_id = @docId LIMIT 1";
                        var pDoc2 = schedCheck.CreateParameter(); pDoc2.ParameterName = "@docId"; pDoc2.Value = dto.DoctorId; schedCheck.Parameters.Add(pDoc2);
                        var scVal = await schedCheck.ExecuteScalarAsync();
                        if (scVal != null && scVal != DBNull.Value) scId = Convert.ToInt32(scVal);

                        if (scId == 0)
                        {
                            using var schedCmd = conn.CreateCommand();
                            schedCmd.CommandText = "INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time) VALUES (@docId, CURRENT_DATE, '08:00:00', '17:00:00') RETURNING schedule_id";
                            var pDoc3 = schedCmd.CreateParameter(); pDoc3.ParameterName = "@docId"; pDoc3.Value = dto.DoctorId; schedCmd.Parameters.Add(pDoc3);
                            var scRes = await schedCmd.ExecuteScalarAsync();
                            if (scRes != null && scRes != DBNull.Value) scId = Convert.ToInt32(scRes);
                        }

                        if (scId > 0)
                        {
                            // Tính slot_order kế tiếp thay vì hardcode 1 — tránh đụng UNIQUE (schedule_id, slot_order)
                            // khi schedule đó đã có sẵn slot từ lần đăng ký trước.
                            using var insSlot = conn.CreateCommand();
                            insSlot.CommandText = @"
                                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                                VALUES (@scId, (SELECT COALESCE(MAX(slot_order), 0) + 1 FROM doctor_schedule_slots WHERE schedule_id = @scId), '08:00:00', '17:00:00', 'Available')
                                RETURNING slot_id";
                            var pSc = insSlot.CreateParameter(); pSc.ParameterName = "@scId"; pSc.Value = scId; insSlot.Parameters.Add(pSc);
                            var slRes = await insSlot.ExecuteScalarAsync();
                            if (slRes != null && slRes != DBNull.Value) validSlotId = Convert.ToInt32(slRes);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("RegisterWalkIn slot lookup exception: " + ex.Message);
                }

                var todayVn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
                var newAppt = new Appointment
                {
                    PatientId = targetPatientId,
                    DoctorId = dto.DoctorId,
                    // slot_id có FOREIGN KEY tới doctor_schedule_slots — để NULL nếu không tạo được slot nào
                    // hợp lệ, thay vì fallback cứng về 1 (gần như chắc chắn đã bị chiếm hoặc không tồn tại).
                    SlotId = validSlotId > 0 ? validSlotId : (int?)null,
                    StatusId = 7, // CheckedIn ngay vì lễ tân đã xác nhận
                    QueueNumber = await _context.Appointments.CountAsync(a => a.DoctorId == dto.DoctorId && a.StatusId == 7) + 1,
                    Reason = $"Khám vãng lai - {dto.SpecialtyName}",
                    Note = $"Bệnh nhân vãng lai đăng ký tại quầy lễ tân | CCCD: {dto.CccdNumber}",
                    AppointmentDate = todayVn,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                try
                {
                    _context.Appointments.Add(newAppt);
                    await _context.SaveChangesAsync();
                    newAppointmentId = newAppt.AppointmentId;
                }
                catch (Exception apptEx)
                {
                    Console.WriteLine("EF Appointment Add Exception: " + apptEx.Message);
                    // Raw SQL fallback if trigger fails
                    try
                    {
                        var conn = _context.Database.GetDbConnection();
                        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
                        using var rawCmd = conn.CreateCommand();
                        rawCmd.CommandText = @"
                            INSERT INTO appointments (patient_id, doctor_id, slot_id, status_id, queue_number, reason, note, is_active, appointment_date, created_at, updated_at) 
                            VALUES (@pId, @dId, @sId, 7, @qNum, @reason, @note, true, CURRENT_DATE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP) 
                            RETURNING appointment_id";

                        var p1 = rawCmd.CreateParameter(); p1.ParameterName = "@pId"; p1.Value = targetPatientId; rawCmd.Parameters.Add(p1);
                        var p2 = rawCmd.CreateParameter(); p2.ParameterName = "@dId"; p2.Value = dto.DoctorId; rawCmd.Parameters.Add(p2);
                        var p3 = rawCmd.CreateParameter(); p3.ParameterName = "@sId"; p3.Value = validSlotId > 0 ? validSlotId : (object)DBNull.Value; rawCmd.Parameters.Add(p3);
                        var p4 = rawCmd.CreateParameter(); p4.ParameterName = "@qNum"; p4.Value = newAppt.QueueNumber; rawCmd.Parameters.Add(p4);
                        var p5 = rawCmd.CreateParameter(); p5.ParameterName = "@reason"; p5.Value = $"Khám vãng lai - {dto.SpecialtyName}"; rawCmd.Parameters.Add(p5);
                        var p6 = rawCmd.CreateParameter(); p6.ParameterName = "@note"; p6.Value = $"Bệnh nhân vãng lai đăng ký tại quầy lễ tân | CCCD: {dto.CccdNumber}"; rawCmd.Parameters.Add(p6);

                        var rawId = await rawCmd.ExecuteScalarAsync();
                        if (rawId != null && rawId != DBNull.Value) newAppointmentId = Convert.ToInt32(rawId);
                    }
                    catch (Exception exRaw)
                    {
                        Console.WriteLine("Raw SQL appointment insert exception: " + exRaw.Message);
                    }
                }
            }

            // Gửi thông báo nội bộ (giải lập SMS)
            _context.Notifications.Add(new Notification
            {
                UserId = targetUserId,
                Title = "🏥 DTT Healthcare - Tài khoản App Mobile của bạn đã được tạo",
                Content = $"Kính gửi {dto.FullName}," +
                          $"\n\nLễ Tân Bệnh viện DTT Healthcare đã tạo sẵn tài khoản ứng dụng di động cho bạn." +
                          $"\n\n📱 Tải App: DTT Healthcare (Google Play / App Store)" +
                          $"\n📞 SĐT đăng nhập: {dto.Phone}" +
                          $"\n🔑 Mật khẩu tạm thời: {randomPwd}" +
                          $"\n\n⚠️ Vui lòng đổi mật khẩu ngay sau lần đăng nhập đầu tiên!" +
                          $"\n\nHồ sơ y tế và lịch sử khám bệnh của bạn sẽ được lưu trữ tự động trên ứng dụng.",
                Type = "system",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // Tự động ghi đồng bộ thông tin vào file static users.csv & patients.csv trên đĩa
            try
            {
                string dbDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "Chức năng của app bệnh nhân", "db");
                if (System.IO.Directory.Exists(dbDir))
                {
                    string usersCsv = System.IO.Path.Combine(dbDir, "users.csv");
                    if (System.IO.File.Exists(usersCsv))
                    {
                        string userRow = $"\"{targetUserId}\",\"{dto.Phone}\",\"{dto.Phone}@gmail.com\",\"{csvPasswordHash}\",{csvRoleId},\"Active\",NULL,\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}\"";
                        System.IO.File.AppendAllLines(usersCsv, new[] { userRow });
                    }

                    string patientsCsv = System.IO.Path.Combine(dbDir, "patients.csv");
                    if (System.IO.File.Exists(patientsCsv))
                    {
                        string patientRow = $"{targetPatientId},\"{targetUserId}\",\"{dto.FullName}\",NULL,\"{dto.Gender ?? "Nam"}\",NULL,NULL,\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}\",\"verified\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}\",NULL,NULL,\"{dto.CccdNumber}\",\"{dto.Phone}\",NULL";
                        System.IO.File.AppendAllLines(patientsCsv, new[] { patientRow });
                    }
                }
            }
            catch { }

            return Ok(new
            {
                success = true,
                message = "Đã tạo hồ sơ bệnh nhân vãng lai thành công!",
                patientId = targetPatientId,
                userId = targetUserId,
                appointmentId = newAppointmentId,
                tempPassword = randomPwd,
                note = $"Đã giả lập gửi SMS thông báo tài khoản đến SĐT {dto.Phone}"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi tạo hồ sơ vãng lai: " + ex.Message });
        }
    }
}

public class ConfirmPaymentDto
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public decimal ExamFee { get; set; } = 250000m;
    public decimal ServicesFee { get; set; } = 0m;
    public decimal MedsFee { get; set; } = 0m;
    public string? PaymentMethod { get; set; } = "cash";
}

public class RegisterWalkInDto
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? DateOfBirth { get; set; }
    public string? Gender { get; set; } = "Nam";
    public string CccdNumber { get; set; } = string.Empty;
    public string? BhytNumber { get; set; }
    public string? Address { get; set; }
    public int DoctorId { get; set; }
    public string? SpecialtyName { get; set; }
}
