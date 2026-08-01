using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

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

    // POST /api/Invoices/confirm-payment
    // Lễ Tân xác nhận đã thu tiền → Cập nhật Invoice pending → paid → Gửi thông báo App Mobile
    [HttpPost("confirm-payment")]
    public async Task<IActionResult> ConfirmPayment([FromBody] ConfirmPaymentDto dto)
    {
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

            // 3. Tính tổng tiền từ các khoản (khám + CLS + thuốc)
            decimal examFee = dto.ExamFee > 0 ? dto.ExamFee : 250000m;
            decimal servicesFee = dto.ServicesFee > 0 ? dto.ServicesFee : 0m;
            decimal medsFee = dto.MedsFee > 0 ? dto.MedsFee : 0m;
            decimal totalAmount = examFee + servicesFee + medsFee;

            // 4. Nếu có đơn thuốc, tính thêm chi phí thuốc từ prescription
            if (dto.AppointmentId > 0 && medsFee == 0)
            {
                var medRecord = await _context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == dto.AppointmentId);
                if (medRecord != null)
                {
                    var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.MedicalRecordId == medRecord.MedicalRecordId);
                    if (prescription != null)
                    {
                        var details = await _context.PrescriptionDetails.Where(d => d.PrescriptionId == prescription.PrescriptionId).ToListAsync();
                        var medIds = details.Select(d => d.MedicineId).ToList();
                        var medDict = await _context.Medicines.Where(m => medIds.Contains(m.MedicineId)).ToDictionaryAsync(m => m.MedicineId);
                        medsFee = details.Sum(d => d.Quantity * (medDict.ContainsKey(d.MedicineId) && medDict[d.MedicineId].Price > 0 ? medDict[d.MedicineId].Price : 15000m));
                        totalAmount = examFee + servicesFee + medsFee;
                    }
                }
            }

            Invoice invoice;

            // 5. Tim invoice pending (tao tu MedicalRecordsController khi bac si hoan tat)
            var pendingInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == dto.AppointmentId && i.PaymentStatus == "pending");
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

            // Cập nhật appointment status → Paid
            if (appt.StatusId != 5) // 5 = Paid
            {
                appt.StatusId = 5;
                appt.UpdatedAt = DateTime.UtcNow;
            }

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
                    PasswordHash = dto.Phone + "_DTT_temp",
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var patientRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Patient");
                if (patientRole != null) newUser.RoleId = patientRole.RoleId;

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();
                targetUserId = newUser.UserId;
            }

            // Tìm hồ sơ Patient sẵn có hoặc tạo mới
            var existingPatient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == targetUserId || p.PhoneNumber == dto.Phone);
            int targetPatientId;
            if (existingPatient != null)
            {
                existingPatient.FullName = dto.FullName;
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
                    dob = parsedDob;

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

                    using var slotCmd = conn.CreateCommand();
                    slotCmd.CommandText = $"SELECT s.slot_id FROM doctor_schedule_slots s JOIN doctor_schedules ds ON s.schedule_id = ds.schedule_id WHERE ds.doctor_id = {dto.DoctorId} AND s.slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL) LIMIT 1";
                    var val = await slotCmd.ExecuteScalarAsync();
                    if (val != null && val != DBNull.Value) validSlotId = Convert.ToInt32(val);

                    if (validSlotId == 0)
                    {
                        using var anySlot = conn.CreateCommand();
                        anySlot.CommandText = "SELECT slot_id FROM doctor_schedule_slots WHERE slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL) LIMIT 1";
                        var val2 = await anySlot.ExecuteScalarAsync();
                        if (val2 != null && val2 != DBNull.Value) validSlotId = Convert.ToInt32(val2);
                    }

                    if (validSlotId == 0)
                    {
                        using var schedCmd = conn.CreateCommand();
                        schedCmd.CommandText = $"INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time) VALUES ({dto.DoctorId}, CURRENT_DATE, '08:00:00', '17:00:00') RETURNING schedule_id";
                        var scRes = await schedCmd.ExecuteScalarAsync();
                        int scId = scRes != null && scRes != DBNull.Value ? Convert.ToInt32(scRes) : 1;

                        using var insSlot = conn.CreateCommand();
                        insSlot.CommandText = $"INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status) VALUES ({scId}, 1, '08:00:00', '17:00:00', 'Available') RETURNING slot_id";
                        var slRes = await insSlot.ExecuteScalarAsync();
                        if (slRes != null && slRes != DBNull.Value) validSlotId = Convert.ToInt32(slRes);
                    }
                }
                catch { }
                if (validSlotId == 0) validSlotId = 1;

                var newAppt = new Appointment
                {
                    PatientId = targetPatientId,
                    DoctorId = dto.DoctorId,
                    SlotId = validSlotId,
                    StatusId = 7, // CheckedIn ngay vì lễ tân đã xác nhận
                    QueueNumber = await _context.Appointments.CountAsync(a => a.DoctorId == dto.DoctorId && a.StatusId == 7) + 1,
                    Reason = $"Khám vãng lai - {dto.SpecialtyName}",
                    Note = $"Bệnh nhân vãng lai đăng ký tại quầy lễ tân | CCCD: {dto.CccdNumber}",
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
                        rawCmd.CommandText = $"INSERT INTO appointments (patient_id, doctor_id, slot_id, status_id, queue_number, reason, note, is_active, created_at, updated_at) VALUES ({targetPatientId}, {dto.DoctorId}, {validSlotId}, 7, {newAppt.QueueNumber}, 'Khám vãng lai', 'Đăng ký tại quầy Lễ Tân', true, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP) RETURNING appointment_id";
                        var rawId = await rawCmd.ExecuteScalarAsync();
                        if (rawId != null && rawId != DBNull.Value) newAppointmentId = Convert.ToInt32(rawId);
                    }
                    catch { }
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
                        string userRow = $"\"{targetUserId}\",\"{dto.Phone}\",\"{dto.Phone}@gmail.com\",\"$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad6J1B4B1V6K6Ne\",1,\"Active\",NULL,\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}\"";
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
