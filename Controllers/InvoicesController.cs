using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;
using System.Linq;
using System.Security.Claims;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    // [Old code - Dịch vụ VNPAY]:
    // private readonly DTT_Backend_API.Services.IVnPayService _vnPayService;
    private readonly DTT_Backend_API.Models.PaypalClient _paypalClient;

    public InvoicesController(AppDbContext context, IConfiguration config, DTT_Backend_API.Models.PaypalClient paypalClient)
    {
        _context = context;
        _config = config;
        // _vnPayService = vnPayService;
        _paypalClient = paypalClient;
    }

    // Trả về user_id thật của nhân viên đang đăng nhập (từ JWT), thay vì đoán đại một tài khoản
    // lễ tân bất kỳ trong DB hay dùng GUID cố định khi không tìm thấy.
    private Guid? GetCurrentStaffUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : (Guid?)null;
    }

    // GET /api/Invoices/estimate/{appointmentId}
    // Trả về phí khám + phí thuốc THẬT tính từ đơn thuốc điện tử (prescription_details x medicines.unit_price)
    // Dùng để hiển thị đúng số tiền trên màn Thanh Toán TRƯỚC khi Lễ Tân bấm xác nhận thu tiền.

    [HttpGet("sync-invoice-items/{invoiceId}")]
    [AllowAnonymous]
    public async Task<IActionResult> SyncInvoiceItems(int invoiceId)
    {
        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);
        if (invoice == null) return NotFound(new { success = false, message = "Invoice not found" });
        if (!await AccessControl.CanAccessAppointmentAsync(User, _context, invoice.AppointmentId)) return this.ForbidJson();
        var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == invoice.AppointmentId);
        if (appt == null) return NotFound(new { success = false, message = "Appt not found" });

        decimal examFee = (appt.Note?.Contains("Gói khám:") == true) ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
        decimal servicesFee = await ComputeClsFeeAsync(appt.AppointmentId);
        decimal medsFee = await ComputeMedsFeeAsync(appt.AppointmentId);

        var existing = await _context.InvoiceItems.Where(i => i.InvoiceId == invoiceId).ToListAsync();
        if (existing.Count == 0)
        {
            var items = new List<InvoiceItem>();
            string examItemName = (appt.Note?.Contains("Gói khám:") == true) ? "Trọn gói khám sức khỏe" : "Công khám lâm sàng chuyên khoa";
            items.Add(new InvoiceItem { InvoiceId = invoiceId, ItemName = examItemName, ItemType = "exam", Quantity = 1, UnitPrice = examFee, Amount = examFee, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            if (servicesFee > 0)
                items.Add(new InvoiceItem { InvoiceId = invoiceId, ItemName = "Phí dịch vụ Cận lâm sàng (CLS)", ItemType = "service", Quantity = 1, UnitPrice = servicesFee, Amount = servicesFee, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            if (medsFee > 0)
                items.Add(new InvoiceItem { InvoiceId = invoiceId, ItemName = "Phí thuốc theo Đơn thuốc điện tử", ItemType = "medicine", Quantity = 1, UnitPrice = medsFee, Amount = medsFee, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            
            _context.InvoiceItems.AddRange(items);
            await _context.SaveChangesAsync();
        }
        return Ok(new { success = true, items = await _context.InvoiceItems.Where(i => i.InvoiceId == invoiceId).ToListAsync() });
    }

    [HttpGet("estimate/{appointmentId}")]
    [AllowAnonymous]
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

        // Một hồ sơ khám có thể có NHIỀU dòng Prescription (bác sĩ gọi lại API kê đơn nhiều lần cho
        // cùng 1 ca khám, mỗi lần tạo 1 Prescription mới chứ không cập nhật đè đơn cũ — xem
        // MedicalRecordsController.CreateMedicalRecord/DispensePrescription). Trước đây chỉ lấy đơn
        // ĐẦU TIÊN tìm được nên hóa đơn có thể thiếu hẳn tiền thuốc của các đơn kê sau — giờ cộng dồn
        // đủ mọi đơn thuộc cùng hồ sơ khám này.
        var prescriptionIds = await _context.Prescriptions
            .Where(p => p.MedicalRecordId == medRecord.MedicalRecordId)
            .Select(p => p.PrescriptionId)
            .ToListAsync();
        if (prescriptionIds.Count == 0) return 0m;

        var details = await _context.PrescriptionDetails.Where(d => prescriptionIds.Contains(d.PrescriptionId)).ToListAsync();
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
        using var transaction = await _context.Database.BeginTransactionAsync();
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
                // Đặt PaidAmount = 0 để tránh vi phạm chk_paid_le_total khi xóa và thêm lại các dòng items
                invoice.PaidAmount = 0;
                await _context.SaveChangesAsync();

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
                // Tao Invoice moi voi PaidAmount = 0 ban dau
                // QUAN TRỌNG: Phải để PaidAmount = 0 khi tạo ban đầu vì DB có Trigger 'trg_recalc_invoice_total'
                // tự động UPDATE 'total_amount = SUM(amount)' sau MỖI DÒNG insert vào invoice_items.
                // Nếu gán PaidAmount = totalAmount ngay từ đầu, khi chèn dòng đầu tiên (chưa đủ tổng),
                // Postgres sẽ báo lỗi Check Constraint 'chk_paid_le_total' (paid_amount <= total_amount)
                // dẫn tới rollback và mất toàn bộ các dòng invoice_items.
                invoice = new Invoice
                {
                    AppointmentId = dto.AppointmentId,
                    PatientId = patient?.PatientId ?? appt.PatientId,
                    // Gắn đúng hồ sơ người thân nếu lịch hẹn này đặt cho người thân (xem cùng lý do ở
                    // MedicalRecordsController.CreateMedicalRecord) — trước đây luôn bỏ trống.
                    MemberId = appt.MemberId,
                    TotalAmount = 0,
                    PaidAmount = 0,
                    PaymentStatus = "unpaid",
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

            // Cập nhật invoice pending → paid sau khi các items đã được lưu và trigger đã tính xong total_amount
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
                    Content = $"Hóa đơn khám chữa bệnh của bạn ({patient.FullName}) đã được xác nhận thanh toán thành công tại Quầy Thu Ngân DTT Healthcare.\n\nTỔNG TIỀN: {totalFormatted}\nHình thức: {(dto.PaymentMethod == "vnpay" ? "Cổng thanh toán VNPAY" : (dto.PaymentMethod == "bank_transfer" || dto.PaymentMethod == "transfer" ? "Chuyển khoản Ngân hàng (VietQR)" : "Tiền mặt tại quầy"))}\n\nVui lòng vào mục Hồ Sơ Y Tế → Hóa Đơn để xem chi tiết.",
                    Type = "result",
                    RelatedId = invoice.InvoiceId,
                    RelatedType = "invoice",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            await transaction.CommitAsync();

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
            await transaction.RollbackAsync();
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

            // Không cho phép 2 hồ sơ khác nhau dùng CHUNG 1 số CCCD — loại trừ đúng hồ sơ ĐANG thao tác
            // (dto.ExistingPatientId/dto.MemberId, nếu có) để không tự chặn nhầm khi xác nhận lại CCCD
            // đã đúng sẵn của chính hồ sơ đó.
            bool cccdTakenByOtherPatient = await _context.Patients.AnyAsync(p =>
                p.CccdNumber == dto.CccdNumber &&
                !(dto.ExistingPatientId.HasValue && p.PatientId == dto.ExistingPatientId.Value));
            bool cccdTakenByOtherMember = await _context.FamilyMembers.AnyAsync(m =>
                m.CccdNumber == dto.CccdNumber &&
                !(dto.MemberId.HasValue && m.MemberId == dto.MemberId.Value));
            if (cccdTakenByOtherPatient || cccdTakenByOtherMember)
                return BadRequest(new { success = false, message = "Số CCCD này đã được sử dụng cho một hồ sơ khác trong hệ thống." });

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
            int targetPatientId;
            int? memberIdForAppt = null;
            // Thông báo "tài khoản App Mobile vừa được tạo kèm mật khẩu tạm" (bên dưới, sau khối này)
            // chỉ có ý nghĩa với người THẬT SỰ vừa được tạo/khớp tài khoản mới qua SĐT — 2 nhánh MỚI
            // dưới đây (chọn sẵn 1 bệnh nhân/người thân ĐÃ CÓ hồ sơ từ tab "Đặt Khám Ngay") không tạo
            // gì mới cả nên phải tắt thông báo này, tránh báo sai "vừa tạo tài khoản" cho người đã có
            // tài khoản từ trước (đặc biệt phiền với nhánh người thân: SĐT gửi lên là SĐT của CHỦ TÀI
            // KHOẢN do người thân thường không có SĐT riêng).
            bool sendNewAccountNotification = true;

            if (dto.MemberId.HasValue && dto.MemberId.Value > 0 && dto.OwnerPatientId.HasValue && dto.OwnerPatientId.Value > 0)
            {
                sendNewAccountNotification = false;
                // Đặt khám ngay cho HỒ SƠ NGƯỜI THÂN đã có sẵn (tab "Đặt Khám Ngay" tìm ra qua SĐT của
                // chủ tài khoản) — chỉ được cập nhật đúng hồ sơ family_members đó, TUYỆT ĐỐI không đụng
                // tới hồ sơ patients của chủ tài khoản.
                var member = await _context.FamilyMembers.FirstOrDefaultAsync(m => m.MemberId == dto.MemberId.Value && m.OwnerPatientId == dto.OwnerPatientId.Value);
                if (member == null)
                    return BadRequest(new { success = false, message = "Không tìm thấy hồ sơ người thân hoặc hồ sơ không thuộc đúng chủ tài khoản." });
                var ownerForMember = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == dto.OwnerPatientId.Value);
                if (ownerForMember == null)
                    return BadRequest(new { success = false, message = "Không tìm thấy hồ sơ chủ tài khoản của người thân này." });

                member.CccdNumber = dto.CccdNumber;
                if (!string.IsNullOrEmpty(dto.BhytNumber)) member.HealthInsuranceNumber = dto.BhytNumber;
                if (member.VerificationStatus != "verified")
                {
                    member.VerificationStatus = "verified";
                    member.VerifiedBy = GetCurrentStaffUserId();
                    member.VerifiedAt = DateTime.UtcNow;
                    member.VerificationNote = $"Đối chiếu CCCD khi Đặt Khám Ngay tại quầy Lễ Tân. Ngày: {DateTime.Now:dd/MM/yyyy HH:mm}";
                }
                member.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                targetPatientId = ownerForMember.PatientId;
                memberIdForAppt = member.MemberId;
            }
            else if (dto.ExistingPatientId.HasValue && dto.ExistingPatientId.Value > 0)
            {
                sendNewAccountNotification = false;
                // Đặt khám ngay cho 1 BỆNH NHÂN đã có sẵn hồ sơ (tab "Đặt Khám Ngay") — chỉ cập nhật CCCD
                // vừa đối chiếu, KHÔNG ghi đè họ tên/thông tin định danh đã đúng sẵn của họ bằng dto.FullName
                // (khác nhánh "vãng lai mới hoàn toàn" bên dưới, nơi FullName THẬT SỰ là dữ liệu mới).
                var existingPatient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == dto.ExistingPatientId.Value);
                if (existingPatient == null)
                    return BadRequest(new { success = false, message = "Không tìm thấy hồ sơ bệnh nhân." });

                existingPatient.CccdNumber = dto.CccdNumber;
                if (!string.IsNullOrEmpty(dto.BhytNumber)) existingPatient.HealthInsuranceNumber = dto.BhytNumber;
                if (existingPatient.VerificationStatus != "verified")
                {
                    existingPatient.VerificationStatus = "verified";
                    existingPatient.VerifiedBy = GetCurrentStaffUserId();
                    existingPatient.VerifiedAt = DateTime.UtcNow;
                }
                existingPatient.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                targetPatientId = existingPatient.PatientId;
            }
            else if (await _context.Patients.FirstOrDefaultAsync(p => p.UserId == targetUserId || p.PhoneNumber == dto.Phone) is Patient existingPatientByPhone)
            {
                // Đăng ký vãng lai MỚI HOÀN TOÀN (tab "Đăng Ký Vãng Lai") nhưng trùng SĐT với 1 hồ sơ đã
                // có sẵn trong DB (vd bệnh nhân cũ quay lại mà Lễ tân không tra cứu trước) — giữ nguyên
                // hành vi gốc: cập nhật lại hồ sơ đó bằng thông tin vừa nhập.
                // DB có trigger trg_create_profile_on_user_insert tự tạo sẵn 1 dòng patients rỗng
                // (phone_number NULL) ngay khi tạo users mới — nhánh update này trước đây quên gán lại
                // PhoneNumber, nên bệnh nhân vãng lai luôn có users.phone_number đúng nhưng
                // patients.phone_number NULL vĩnh viễn (lộ ra ở màn "Lịch Sử Hồ Sơ Bệnh Án" — cột SĐT
                // trống dù đăng ký có nhập số điện thoại).
                existingPatientByPhone.FullName = dto.FullName;
                existingPatientByPhone.PhoneNumber = dto.Phone;
                existingPatientByPhone.CccdNumber = dto.CccdNumber;
                if (!string.IsNullOrEmpty(dto.BhytNumber)) existingPatientByPhone.HealthInsuranceNumber = dto.BhytNumber;
                existingPatientByPhone.VerificationStatus = "verified";
                existingPatientByPhone.VerifiedAt = DateTime.UtcNow;
                existingPatientByPhone.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                targetPatientId = existingPatientByPhone.PatientId;
            }
            else
            {
                var staffId = GetCurrentStaffUserId();
                if (staffId == null)
                    return BadRequest(new { success = false, message = "Không xác định được nhân viên thực hiện đăng ký." });
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
                    VerifiedBy = staffId,
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
                var todayVnForSlot = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
                try
                {
                    var conn = _context.Database.GetDbConnection();
                    if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

                    // CHỈ chọn slot đang RẢNH (không bị appointment active nào chiếm) của ĐÚNG NGÀY HÔM NAY
                    // — trước đây dùng "ORDER BY slot_id DESC LIMIT 1" lấy bừa slot mới nhất của bác sĩ bất
                    // kể còn trống hay không VÀ bất kể thuộc ngày nào, nên khi bác sĩ đã có 1 lịch hẹn active
                    // dùng đúng slot đó, lần đăng ký vãng lai tiếp theo cho cùng bác sĩ sẽ đụng UNIQUE
                    // constraint idx_appointments_slot_active; đồng thời slot_id có thể thuộc 1 schedule của
                    // ngày KHÁC trong khi appointment.appointment_date luôn được set = hôm nay (todayVn bên
                    // dưới), gây lệch dữ liệu slot/ngày.
                    using var slotCmd = conn.CreateCommand();
                    slotCmd.CommandText = @"
                        SELECT s.slot_id
                        FROM doctor_schedule_slots s
                        JOIN doctor_schedules ds ON s.schedule_id = ds.schedule_id
                        WHERE ds.doctor_id = @docId
                          AND ds.work_date = @workDate
                          AND s.slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL AND is_active = true)
                        ORDER BY s.slot_id DESC
                        LIMIT 1";
                    var pDoc = slotCmd.CreateParameter(); pDoc.ParameterName = "@docId"; pDoc.Value = dto.DoctorId; slotCmd.Parameters.Add(pDoc);
                    var pWd = slotCmd.CreateParameter(); pWd.ParameterName = "@workDate"; pWd.Value = todayVnForSlot.ToDateTime(TimeOnly.MinValue); slotCmd.Parameters.Add(pWd);
                    var val = await slotCmd.ExecuteScalarAsync();
                    if (val != null && val != DBNull.Value) validSlotId = Convert.ToInt32(val);

                    if (validSlotId == 0)
                    {
                        int scId = 0;
                        using var schedCheck = conn.CreateCommand();
                        schedCheck.CommandText = "SELECT schedule_id FROM doctor_schedules WHERE doctor_id = @docId AND work_date = @workDate LIMIT 1";
                        var pDoc2 = schedCheck.CreateParameter(); pDoc2.ParameterName = "@docId"; pDoc2.Value = dto.DoctorId; schedCheck.Parameters.Add(pDoc2);
                        var pWd2 = schedCheck.CreateParameter(); pWd2.ParameterName = "@workDate"; pWd2.Value = todayVnForSlot.ToDateTime(TimeOnly.MinValue); schedCheck.Parameters.Add(pWd2);
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

                var todayVn = todayVnForSlot;
                var newAppt = new Appointment
                {
                    PatientId = targetPatientId,
                    MemberId = memberIdForAppt,
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
                            INSERT INTO appointments (patient_id, member_id, doctor_id, slot_id, status_id, queue_number, reason, note, is_active, appointment_date, created_at, updated_at)
                            VALUES (@pId, @mId, @dId, @sId, 7, @qNum, @reason, @note, true, CURRENT_DATE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                            RETURNING appointment_id";

                        var p1 = rawCmd.CreateParameter(); p1.ParameterName = "@pId"; p1.Value = targetPatientId; rawCmd.Parameters.Add(p1);
                        var pMember = rawCmd.CreateParameter(); pMember.ParameterName = "@mId"; pMember.Value = (object?)memberIdForAppt ?? DBNull.Value; rawCmd.Parameters.Add(pMember);
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

            // Gửi thông báo nội bộ (giải lập SMS) — chỉ khi thật sự vừa tạo/khớp tài khoản mới qua SĐT
            // (xem giải thích ở khai báo sendNewAccountNotification phía trên).
            if (sendNewAccountNotification)
            {
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
            }

            await _context.SaveChangesAsync();

            // Tự động ghi đồng bộ thông tin vào file static users.csv & patients.csv trên đĩa — CHỈ khi
            // thật sự vừa tạo/khớp tài khoản mới qua SĐT. Nhánh "chọn sẵn bệnh nhân/người thân đã có hồ
            // sơ" (2 nhánh mới ở trên) KHÔNG được ghi dòng nào vào đây: targetPatientId ở 2 nhánh đó có
            // thể là patient_id của CHỦ TÀI KHOẢN trong khi dto.FullName lại là tên NGƯỜI THÂN — ghi CSV
            // trong trường hợp này sẽ tạo ra 1 dòng patients.csv sai tên cho đúng patientId đó (lặp lại
            // đúng kiểu lỗi đã sửa ở DB thật phía trên, chỉ khác là ở file tĩnh).
            if (sendNewAccountNotification)
            {
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
            }

            return Ok(new
            {
                success = true,
                message = sendNewAccountNotification
                    ? "Đã tạo hồ sơ bệnh nhân vãng lai thành công!"
                    : "Đã đặt khám thành công!",
                patientId = targetPatientId,
                memberId = memberIdForAppt,
                userId = targetUserId,
                appointmentId = newAppointmentId,
                tempPassword = sendNewAccountNotification ? randomPwd : null,
                note = sendNewAccountNotification ? $"Đã giả lập gửi SMS thông báo tài khoản đến SĐT {dto.Phone}" : null
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi tạo hồ sơ vãng lai: " + ex.Message });
        }
    }


    // ══════════════════════════════════════════════════════════════════════════
    // PHÂN HỆ THANH TOÁN ĐA PHƯƠNG THỨC: VNPAY & VIETQR (NGÂN HÀNG)
    // ══════════════════════════════════════════════════════════════════════════

    // GET /api/Invoices/vietqr/{appointmentId}
    // Lấy thông tin & URL mã VietQR (Napas 247) tự động kèm số tiền và cú pháp chuyển khoản
    [HttpGet("vietqr/{appointmentId}")]
    public async Task<IActionResult> GetVietQrInfo(int appointmentId)
    {
        try
        {
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
            if (appt == null) return NotFound(new { success = false, message = "Không tìm thấy ca khám." });

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId);

            bool isPackage = appt.Note?.Contains("Gói khám:") == true;
            decimal examFee = isPackage ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
            decimal servicesFee = await ComputeClsFeeAsync(appointmentId);
            decimal medsFee = await ComputeMedsFeeAsync(appointmentId);
            decimal totalAmount = examFee + servicesFee + medsFee;

            string bankId = _config["VietQR:BankId"] ?? "MB";
            string bankName = _config["VietQR:BankName"] ?? "Ngân hàng Quân Đội (MB Bank)";
            string accountNo = _config["VietQR:AccountNo"] ?? "0904444444";
            string accountName = _config["VietQR:AccountName"] ?? "PHONG KHAM DA KHOA DTT HEALTHCARE";
            string template = _config["VietQR:Template"] ?? "compact2";

            string content = $"DTT CA{appointmentId}";
            string qrUrl = $"https://img.vietqr.io/image/{bankId}-{accountNo}-{template}.jpg?amount={(long)totalAmount}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(accountName)}";

            return Ok(new
            {
                success = true,
                appointmentId,
                patientName = patient?.FullName ?? "Bệnh nhân",
                examFee,
                servicesFee,
                medsFee,
                totalAmount,
                bankId,
                bankName,
                accountNo,
                accountName,
                transferContent = content,
                qrUrl
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi tạo thông tin VietQR: " + ex.Message });
        }
    }


    // ══════════════════════════════════════════════════════════════════════
    // ── PHÂN HỆ THANH TOÁN QUỐC TẾ PAYPAL (REST API v2 & JAVASCRIPT SDK) ──
    // ══════════════════════════════════════════════════════════════════════

    // 1. GET /api/Invoices/paypal-info/{appointmentId}
    // Lấy thông tin thanh toán PayPal, số tiền quy đổi USD và mã QR thanh toán bằng điện thoại
    [HttpGet("paypal-info/{appointmentId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPaypalInfo(int appointmentId)
    {
        try
        {
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
            if (appt == null) return NotFound(new { success = false, message = "Không tìm thấy ca khám." });

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId);

            bool isPackage = appt.Note?.Contains("Gói khám:") == true;
            decimal examFee = isPackage ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
            decimal servicesFee = await ComputeClsFeeAsync(appointmentId);
            decimal medsFee = await ComputeMedsFeeAsync(appointmentId);
            decimal totalVnd = examFee + servicesFee + medsFee;

            decimal rate = _config.GetValue<decimal>("PayPalOptions:ExchangeRateUsd", 25000m);
            if (rate <= 0) rate = 25000m;
            decimal totalUsd = Math.Round(totalVnd / rate, 2);
            if (totalUsd <= 0) totalUsd = 1.00m;

            // Link trang Checkout PayPal trên mobile (dùng IP mạng LAN để điện thoại quét mã QR truy cập được)
            string host = Request.Host.Value;
            string scheme = Request.Scheme;

            if (host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || host.StartsWith("127.0.0.1"))
            {
                string? localIp = GetLocalIpAddress();
                if (!string.IsNullOrEmpty(localIp))
                {
                    int port = Request.Host.Port ?? 5000;
                    host = $"{localIp}:{port}";
                }
            }
            string checkoutUrl = $"{scheme}://{host}/api/Invoices/paypal-checkout/{appointmentId}";

            // Sinh mã QR để bệnh nhân quét bằng điện thoại
            string qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=300x300&data={Uri.EscapeDataString(checkoutUrl)}";

            return Ok(new
            {
                success = true,
                appointmentId,
                patientName = patient?.FullName ?? "Bệnh nhân",
                examFee,
                servicesFee,
                medsFee,
                totalVnd,
                totalUsd,
                checkoutUrl,
                qrUrl
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi lấy thông tin PayPal: " + ex.Message });
        }
    }

    // 2. POST /api/Invoices/create-paypal-order
    // Tạo đơn hàng thanh toán trên cổng PayPal REST API v2
    [HttpPost("create-paypal-order")]
    [HttpPost("/payment/create-paypal-order")]
    [AllowAnonymous]
    public async Task<IActionResult> CreatePaypalOrder([FromBody] PaypalCreateOrderDto req)
    {
        try
        {
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == req.AppointmentId);
            if (appt == null) return NotFound(new { success = false, message = "Không tìm thấy ca khám." });

            bool isPackage = appt.Note?.Contains("Gói khám:") == true;
            decimal examFee = isPackage ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
            decimal servicesFee = await ComputeClsFeeAsync(req.AppointmentId);
            decimal medsFee = await ComputeMedsFeeAsync(req.AppointmentId);
            decimal totalVnd = examFee + servicesFee + medsFee;

            decimal rate = _config.GetValue<decimal>("PayPalOptions:ExchangeRateUsd", 25000m);
            if (rate <= 0) rate = 25000m;
            decimal totalUsd = Math.Round(totalVnd / rate, 2);
            if (totalUsd <= 0) totalUsd = 1.00m;

            string value = totalUsd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            string currency = "USD";
            string refId = $"DTT_APPT_{req.AppointmentId}_{DateTime.UtcNow.Ticks}";

            var response = await _paypalClient.CreateOrder(value, currency, refId);
            if (response == null || string.IsNullOrEmpty(response.id))
            {
                return BadRequest(new { success = false, message = "Không thể tạo đơn hàng PayPal." });
            }

            return Ok(new
            {
                success = true,
                id = response.id,
                status = response.status,
                totalUsd,
                totalVnd
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.GetBaseException().Message });
        }
    }

    // 3. POST /api/Invoices/capture-paypal-order
    // Bắt giữ giao dịch (Capture) sau khi bệnh nhân xác nhận trên PayPal JS SDK và cập nhật CSDL
    [HttpPost("capture-paypal-order")]
    [HttpPost("/payment/capture-paypal-order")]
    [AllowAnonymous]
    public async Task<IActionResult> CapturePaypalOrder([FromQuery] string orderId, [FromQuery] int appointmentId)
    {
        try
        {
            if (string.IsNullOrEmpty(orderId)) return BadRequest(new { success = false, message = "Thiếu orderId PayPal." });

            var response = await _paypalClient.CaptureOrder(orderId);
            if (response == null || response.status != "COMPLETED")
            {
                return BadRequest(new { success = false, message = "Giao dịch PayPal chưa hoàn tất hoặc bị hủy.", status = response?.status });
            }

            // Chống gian lận: order PayPal này phải được TẠO RA (create-paypal-order) đúng cho appointmentId
            // đang capture — nếu không, ai đó có thể trả 1$ cho ca khám của chính mình rồi gọi capture
            // với appointmentId của người khác để đánh dấu hóa đơn (có thể rất lớn) của người khác là "đã thanh toán".
            var capturedRefId = response.purchase_units?.FirstOrDefault()?.reference_id;
            if (string.IsNullOrEmpty(capturedRefId) || !capturedRefId.StartsWith($"DTT_APPT_{appointmentId}_", StringComparison.Ordinal))
            {
                return BadRequest(new { success = false, message = "Đơn hàng PayPal không khớp với ca khám này." });
            }

            // Ghi nhận hóa đơn trong CSDL
            var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
            var patient = appt != null ? await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId) : null;

            decimal examFee = (appt?.Note?.Contains("Gói khám:") == true) ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
            decimal servicesFee = await ComputeClsFeeAsync(appointmentId);
            decimal medsFee = await ComputeMedsFeeAsync(appointmentId);
            decimal totalAmount = examFee + servicesFee + medsFee;

            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == appointmentId);
            if (invoice != null)
            {
                var oldItems = await _context.InvoiceItems.Where(item => item.InvoiceId == invoice.InvoiceId).ToListAsync();
                if (oldItems.Count > 0)
                {
                    _context.InvoiceItems.RemoveRange(oldItems);
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                invoice = new Invoice
                {
                    AppointmentId = appointmentId,
                    PatientId = patient?.PatientId ?? (appt?.PatientId ?? 0),
                    MemberId = appt?.MemberId,
                    TotalAmount = 0,
                    PaidAmount = 0,
                    PaymentStatus = "unpaid",
                    PaymentMethod = "paypal",
                    InvoiceDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Invoices.Add(invoice);
                await _context.SaveChangesAsync();
            }

            // Tạo các InvoiceItems chi tiết cho hóa đơn PayPal
            var items = new List<InvoiceItem>();
            string examItemName = (appt?.Note?.Contains("Gói khám:") == true) ? "Trọn gói khám sức khỏe" : "Công khám lâm sàng chuyên khoa";
            items.Add(new InvoiceItem
            {
                InvoiceId = invoice.InvoiceId,
                ItemName = examItemName,
                ItemType = "exam",
                Quantity = 1,
                UnitPrice = examFee,
                Amount = examFee,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            if (servicesFee > 0)
            {
                items.Add(new InvoiceItem
                {
                    InvoiceId = invoice.InvoiceId,
                    ItemName = "Phí dịch vụ Cận lâm sàng (CLS)",
                    ItemType = "service",
                    Quantity = 1,
                    UnitPrice = servicesFee,
                    Amount = servicesFee,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            if (medsFee > 0)
            {
                items.Add(new InvoiceItem
                {
                    InvoiceId = invoice.InvoiceId,
                    ItemName = "Phí thuốc theo Đơn thuốc điện tử",
                    ItemType = "medicine",
                    Quantity = 1,
                    UnitPrice = medsFee,
                    Amount = medsFee,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            _context.InvoiceItems.AddRange(items);
            await _context.SaveChangesAsync();

            invoice.TotalAmount = totalAmount;
            invoice.PaidAmount = totalAmount;
            invoice.PaymentStatus = "paid";
            invoice.PaymentMethod = "paypal";
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Gửi thông báo tới App Mobile của bệnh nhân
            if (patient != null && patient.UserId != Guid.Empty)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = patient.UserId,
                    Title = "Thanh Toán PayPal Thành Công",
                    Content = $"Hóa đơn viện phí của bạn ({patient.FullName}) đã được thanh toán thành công qua Cổng PayPal.\n\nMÃ GIAO DỊCH: {orderId}\nSỐ TIỀN: {totalAmount:N0} VNĐ\n\nCảm ơn bạn đã sử dụng dịch vụ tại DTT Healthcare!",
                    Type = "result",
                    RelatedId = invoice.InvoiceId,
                    RelatedType = "invoice",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                success = true,
                invoiceId = invoice.InvoiceId,
                orderId,
                status = response.status,
                message = "Thanh toán PayPal thành công!"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.GetBaseException().Message });
        }
    }

    // 4. GET /api/Invoices/paypal-checkout/{appointmentId}
    // Trang Web Mobile Checkout nhúng trực tiếp thư viện PayPal JavaScript SDK chính hãng
    [HttpGet("paypal-checkout/{appointmentId}")]
    [AllowAnonymous]
    public async Task<IActionResult> PaypalCheckoutPage(int appointmentId)
    {
        var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
        var patient = appt != null ? await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId) : null;

        var existingInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == appointmentId);
        bool isAlreadyPaid = existingInvoice != null && existingInvoice.PaymentStatus == "paid";

        decimal examFee = (appt?.Note?.Contains("Gói khám:") == true) ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
        decimal servicesFee = await ComputeClsFeeAsync(appointmentId);
        decimal medsFee = await ComputeMedsFeeAsync(appointmentId);
        decimal totalVnd = examFee + servicesFee + medsFee;

        decimal rate = _config.GetValue<decimal>("PayPalOptions:ExchangeRateUsd", 25000m);
        if (rate <= 0) rate = 25000m;
        decimal totalUsd = Math.Round(totalVnd / rate, 2);
        if (totalUsd <= 0) totalUsd = 1.00m;

        string clientId = _config["PayPalOptions:ClientId"] ?? "BAABlKpDuJPnlCxgjeRE69mwXMYujrDCq4ZZ95SxP9KeEwBrp-I-dcRrczBowbuwo-XTxQ0341Wyud_eks";
        if (string.IsNullOrWhiteSpace(clientId)) clientId = "BAABlKpDuJPnlCxgjeRE69mwXMYujrDCq4ZZ95SxP9KeEwBrp-I-dcRrczBowbuwo-XTxQ0341Wyud_eks";

        string pName = patient?.FullName ?? "Bệnh nhân";
        string usdFormatted = totalUsd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"vi\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("    <title>Thanh Toán Viện Phí PayPal - DTT Healthcare</title>");
        if (!isAlreadyPaid)
        {
            sb.AppendLine("    <!-- Nhúng thư viện PayPal JavaScript SDK chính hãng -->");
            sb.AppendLine("    <script src=\"https://www.paypal.com/sdk/js?client-id=" + clientId + "&currency=USD\"></script>");
        }
        sb.AppendLine("    <style>");
        sb.AppendLine("        * { box-sizing: border-box; margin: 0; padding: 0; }");
        sb.AppendLine("        body {");
        sb.AppendLine("            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;");
        sb.AppendLine("            background: #f1f5f9;");
        sb.AppendLine("            color: #1e293b;");
        sb.AppendLine("            min-height: 100vh;");
        sb.AppendLine("            display: flex;");
        sb.AppendLine("            align-items: center;");
        sb.AppendLine("            justify-content: center;");
        sb.AppendLine("            padding: 16px;");
        sb.AppendLine("        }");
        sb.AppendLine("        .checkout-card {");
        sb.AppendLine("            background: #ffffff;");
        sb.AppendLine("            border-radius: 20px;");
        sb.AppendLine("            box-shadow: 0 20px 35px -10px rgba(0,0,0,0.1), 0 1px 3px rgba(0,0,0,0.05);");
        sb.AppendLine("            max-width: 460px;");
        sb.AppendLine("            width: 100%;");
        sb.AppendLine("            overflow: hidden;");
        sb.AppendLine("        }");
        sb.AppendLine("        .header {");
        sb.AppendLine("            background: linear-gradient(135deg, #003087 0%, #0070ba 100%);");
        sb.AppendLine("            color: white;");
        sb.AppendLine("            padding: 24px;");
        sb.AppendLine("            text-align: center;");
        sb.AppendLine("        }");
        sb.AppendLine("        .header h1 { font-size: 20px; font-weight: 700; margin-bottom: 4px; }");
        sb.AppendLine("        .header p { font-size: 13px; opacity: 0.9; }");
        sb.AppendLine("        .body { padding: 24px; }");
        sb.AppendLine("        .bill-info {");
        sb.AppendLine("            background: #f8fafc;");
        sb.AppendLine("            border: 1px solid #e2e8f0;");
        sb.AppendLine("            border-radius: 12px;");
        sb.AppendLine("            padding: 16px;");
        sb.AppendLine("            margin-bottom: 20px;");
        sb.AppendLine("        }");
        sb.AppendLine("        .bill-row {");
        sb.AppendLine("            display: flex;");
        sb.AppendLine("            justify-content: space-between;");
        sb.AppendLine("            margin-bottom: 8px;");
        sb.AppendLine("            font-size: 14px;");
        sb.AppendLine("        }");
        sb.AppendLine("        .bill-row.total {");
        sb.AppendLine("            border-top: 1px dashed #cbd5e1;");
        sb.AppendLine("            padding-top: 10px;");
        sb.AppendLine("            margin-top: 10px;");
        sb.AppendLine("            font-weight: bold;");
        sb.AppendLine("            font-size: 16px;");
        sb.AppendLine("        }");
        sb.AppendLine("        .total-vnd { color: #059669; font-size: 20px; font-weight: 800; }");
        sb.AppendLine("        .total-usd { color: #0070ba; font-size: 14px; font-weight: 600; }");
        sb.AppendLine("        #paypal-button-container { margin-top: 16px; min-height: 120px; }");
        sb.AppendLine("        .success-box {");
        sb.AppendLine("            display: " + (isAlreadyPaid ? "block" : "none") + ";");
        sb.AppendLine("            text-align: center;");
        sb.AppendLine("            padding: 24px;");
        sb.AppendLine("        }");
        sb.AppendLine("        .success-icon { font-size: 54px; margin-bottom: 12px; }");
        sb.AppendLine("        .success-title { color: #16a34a; font-size: 22px; font-weight: bold; margin-bottom: 8px; }");
        sb.AppendLine("        .success-msg { color: #64748b; font-size: 14px; line-height: 1.5; }");
        sb.AppendLine("        .loading {");
        sb.AppendLine("            display: none;");
        sb.AppendLine("            text-align: center;");
        sb.AppendLine("            padding: 16px;");
        sb.AppendLine("            color: #0070ba;");
        sb.AppendLine("            font-weight: 600;");
        sb.AppendLine("            background: #eff6ff;");
        sb.AppendLine("            border-radius: 8px;");
        sb.AppendLine("            margin-top: 12px;");
        sb.AppendLine("        }");
        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("    <div class=\"checkout-card\">");
        sb.AppendLine("        <div class=\"header\">");
        sb.AppendLine("            <h1>🅿️ CỔNG THANH TOÁN PAYPAL</h1>");
        sb.AppendLine("            <p>Phòng khám Đa khoa DTT Healthcare</p>");
        sb.AppendLine("        </div>");
        if (isAlreadyPaid)
        {
            sb.AppendLine("        <div class=\"success-box\">");
            sb.AppendLine("            <div class=\"success-icon\">✅</div>");
            sb.AppendLine("            <div class=\"success-title\">HÓA ĐƠN ĐÃ ĐƯỢC THANH TOÁN!</div>");
            sb.AppendLine("            <p class=\"success-msg\">");
            sb.AppendLine("                Ca khám <strong>#" + appointmentId + "</strong> của bệnh nhân <strong>" + pName + "</strong> đã hoàn tất thanh toán.<br><br>");
            sb.AppendLine("                Mã hóa đơn: <strong>#HD-" + (existingInvoice?.InvoiceId ?? 0) + "</strong><br>");
            sb.AppendLine("                Tổng viện phí: <strong style=\"color:#059669; font-size:16px;\">" + totalVnd.ToString("N0") + " VNĐ</strong><br><br>");
            sb.AppendLine("                Bạn có thể đóng trang này.");
            sb.AppendLine("            </p>");
            sb.AppendLine("        </div>");
        }
        else
        {
            sb.AppendLine("        <div class=\"body\" id=\"checkout-content\">");
            sb.AppendLine("            <div class=\"bill-info\">");
            sb.AppendLine("                <div class=\"bill-row\">");
            sb.AppendLine("                    <span style=\"color:#64748b;\">Bệnh nhân:</span>");
            sb.AppendLine("                    <strong>" + pName + "</strong>");
            sb.AppendLine("                </div>");
            sb.AppendLine("                <div class=\"bill-row\">");
            sb.AppendLine("                    <span style=\"color:#64748b;\">Mã ca khám:</span>");
            sb.AppendLine("                    <strong>#" + appointmentId + "</strong>");
            sb.AppendLine("                </div>");
            sb.AppendLine("                <div class=\"bill-row\">");
            sb.AppendLine("                    <span style=\"color:#64748b;\">Công khám:</span>");
            sb.AppendLine("                    <span>" + examFee.ToString("N0") + " đ</span>");
            sb.AppendLine("                </div>");
            sb.AppendLine("                <div class=\"bill-row\">");
            sb.AppendLine("                    <span style=\"color:#64748b;\">Dịch vụ CLS:</span>");
            sb.AppendLine("                    <span>" + servicesFee.ToString("N0") + " đ</span>");
            sb.AppendLine("                </div>");
            sb.AppendLine("                <div class=\"bill-row\">");
            sb.AppendLine("                    <span style=\"color:#64748b;\">Tiền thuốc:</span>");
            sb.AppendLine("                    <span>" + medsFee.ToString("N0") + " đ</span>");
            sb.AppendLine("                </div>");
            sb.AppendLine("                <div class=\"bill-row total\">");
            sb.AppendLine("                    <span>Tổng viện phí:</span>");
            sb.AppendLine("                    <div style=\"text-align:right;\">");
            sb.AppendLine("                        <div class=\"total-vnd\">" + totalVnd.ToString("N0") + " VNĐ</div>");
            sb.AppendLine("                        <div class=\"total-usd\">≈ $" + usdFormatted + " USD</div>");
            sb.AppendLine("                    </div>");
            sb.AppendLine("                </div>");
            sb.AppendLine("            </div>");
            sb.AppendLine("            <p style=\"font-size:13px; color:#64748b; text-align:center; margin-bottom:12px;\">");
            sb.AppendLine("                Chọn phương thức thanh toán bằng tài khoản PayPal hoặc Thẻ Quốc tế (Visa / Mastercard):");
            sb.AppendLine("            </p>");
            sb.AppendLine("            <div id=\"paypal-button-container\"></div>");
            sb.AppendLine("            <div id=\"loading-box\" class=\"loading\">⏳ Đang xử lý giao dịch PayPal...</div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"success-box\" id=\"success-box\">");
            sb.AppendLine("            <div class=\"success-icon\">✅</div>");
            sb.AppendLine("            <div class=\"success-title\">THANH TOÁN THÀNH CÔNG!</div>");
            sb.AppendLine("            <p class=\"success-msg\">");
            sb.AppendLine("                Giao dịch của bạn đã được ghi nhận trên hệ thống phòng khám DTT Healthcare.<br><br>");
            sb.AppendLine("                Bạn có thể đóng trang này.");
            sb.AppendLine("            </p>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <script>");
            sb.AppendLine("            if (window.paypal && paypal.Buttons) {");
            sb.AppendLine("                paypal.Buttons({");
            sb.AppendLine("                    style: {");
            sb.AppendLine("                        layout: 'vertical',");
            sb.AppendLine("                        color: 'gold',");
            sb.AppendLine("                        shape: 'rect',");
            sb.AppendLine("                        label: 'paypal'");
            sb.AppendLine("                    },");
            sb.AppendLine("                    async createOrder() {");
            sb.AppendLine("                        const response = await fetch('/payment/create-paypal-order', {");
            sb.AppendLine("                            method: 'POST',");
            sb.AppendLine("                            headers: { 'Content-Type': 'application/json' },");
            sb.AppendLine("                            body: JSON.stringify({ appointmentId: " + appointmentId + " })");
            sb.AppendLine("                        });");
            sb.AppendLine("                        const order = await response.json();");
            sb.AppendLine("                        return order.id;");
            sb.AppendLine("                    },");
            sb.AppendLine("                    async onApprove(data) {");
            sb.AppendLine("                        document.getElementById('loading-box').style.display = 'block';");
            sb.AppendLine("                        const response = await fetch('/payment/capture-paypal-order?orderId=' + encodeURIComponent(data.orderID) + '&appointmentId=' + " + appointmentId + ", {");
            sb.AppendLine("                            method: 'POST'");
            sb.AppendLine("                        });");
            sb.AppendLine("                        const details = await response.json();");
            sb.AppendLine("                        document.getElementById('loading-box').style.display = 'none';");
            sb.AppendLine("                        if (details && details.success) {");
            sb.AppendLine("                            document.getElementById('checkout-content').style.display = 'none';");
            sb.AppendLine("                            document.getElementById('success-box').style.display = 'block';");
            sb.AppendLine("                        } else {");
            sb.AppendLine("                            alert('Lỗi xác nhận thanh toán PayPal: ' + (details.message || ''));");
            sb.AppendLine("                        }");
            sb.AppendLine("                    },");
            sb.AppendLine("                    onCancel(data) {");
            sb.AppendLine("                        alert('Bạn đã hủy giao dịch PayPal.');");
            sb.AppendLine("                    },");
            sb.AppendLine("                    onError(err) {");
            sb.AppendLine("                        console.error('PayPal Error:', err);");
            sb.AppendLine("                    }");
            sb.AppendLine("                }).render('#paypal-button-container');");
            sb.AppendLine("            }");
            sb.AppendLine("        </script>");
        }
        sb.AppendLine("    </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        string html = sb.ToString();
        return Content(html, "text/html; charset=utf-8");
    }

    // [Old code VNPAY]:     // POST /api/Invoices/vnpay-create-payment-url
    // [Old code VNPAY]:     // Tạo link và mã QR thanh toán VNPAY Sandbox qua IVnPayService
    // [Old code VNPAY]:     [HttpPost("vnpay-create-payment-url")]
    // [Old code VNPAY]:     public async Task<IActionResult> CreateVnPayPaymentUrl([FromBody] VnPayPaymentRequestDto req)
    // [Old code VNPAY]:     {
    // [Old code VNPAY]:         try
    // [Old code VNPAY]:         {
    // [Old code VNPAY]:             var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == req.AppointmentId);
    // [Old code VNPAY]:             if (appt == null) return NotFound(new { success = false, message = "Không tìm thấy ca khám." });
    // [Old code VNPAY]: 
    // [Old code VNPAY]:             var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId);
    // [Old code VNPAY]: 
    // [Old code VNPAY]:             bool isPackage = appt.Note?.Contains("Gói khám:") == true;
    // [Old code VNPAY]:             decimal examFee = isPackage ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
    // [Old code VNPAY]:             decimal servicesFee = await ComputeClsFeeAsync(req.AppointmentId);
    // [Old code VNPAY]:             decimal medsFee = await ComputeMedsFeeAsync(req.AppointmentId);
    // [Old code VNPAY]:             decimal totalAmount = examFee + servicesFee + medsFee;
    // [Old code VNPAY]: 
    // [Old code VNPAY]:             // [New code - Sử dụng VnPaymentRequestModel chuẩn và IVnPayService]:
    // [Old code VNPAY]:             var vnPayModel = new VnPaymentRequestModel
    // [Old code VNPAY]:             {
    // [Old code VNPAY]:                 OrderId = req.AppointmentId,
    // [Old code VNPAY]:                 FullName = patient?.FullName ?? "Bệnh nhân",
    // [Old code VNPAY]:                 Description = $"Thanh toan vien phi ca #{req.AppointmentId} BN {patient?.FullName}",
    // [Old code VNPAY]:                 Amount = (double)totalAmount,
    // [Old code VNPAY]:                 CreatedDate = DateTime.Now
    // [Old code VNPAY]:             };
    // [Old code VNPAY]: 
    // [Old code VNPAY]:             string paymentUrl = _vnPayService.CreatePaymentUrl(HttpContext, vnPayModel);
    // [Old code VNPAY]: 
    // [Old code VNPAY]:             return Ok(new
    // [Old code VNPAY]:             {
    // [Old code VNPAY]:                 success = true,
    // [Old code VNPAY]:                 paymentUrl,
    // [Old code VNPAY]:                 totalAmount,
    // [Old code VNPAY]:                 appointmentId = req.AppointmentId,
    // [Old code VNPAY]:                 patientName = patient?.FullName ?? "Bệnh nhân"
    // [Old code VNPAY]:             });
    // [Old code VNPAY]:         }
    // [Old code VNPAY]:         catch (Exception ex)
    // [Old code VNPAY]:         {
    // [Old code VNPAY]:             return StatusCode(500, new { success = false, message = "Lỗi tạo URL VNPAY: " + ex.Message });
    // [Old code VNPAY]:         }
    // [Old code VNPAY]:     }
    // [Old code VNPAY]: 
    // [Old code VNPAY]:     // GET /api/Invoices/vnpay-callback
    // [Old code VNPAY]:     // Nhận kết quả phản hồi từ Cổng VNPAY qua IVnPayService.PaymentExecute và cập nhật trạng thái thanh toán
    // [Old code VNPAY]:     [HttpGet("vnpay-callback")]
    // [Old code VNPAY]:     public async Task<IActionResult> VnPayCallback()
    // [Old code VNPAY]:     {
    // [Old code VNPAY]:         try
    // [Old code VNPAY]:         {
    // [Old code VNPAY]:             // [New code - Xử lý callback qua IVnPayService.PaymentExecute]:
    // [Old code VNPAY]:             var response = _vnPayService.PaymentExecute(Request.Query);
    // [Old code VNPAY]: 
    // [Old code VNPAY]:             if (!response.Success)
    // [Old code VNPAY]:             {
    // [Old code VNPAY]:                 return Content("<h2 style='color:red;text-align:center;'>Chữ ký VNPAY không hợp lệ!</h2>", "text/html; charset=utf-8");
    // [Old code VNPAY]:             }
    // [Old code VNPAY]: 
    // [Old code VNPAY]:             int.TryParse(response.OrderId, out int appointmentId);
    // [Old code VNPAY]: 
    // [Old code VNPAY]:             if (response.VnPayResponseCode == "00" && appointmentId > 0)
    // [Old code VNPAY]:             {
    // [Old code VNPAY]:                 // Giao dịch VNPAY thành công
    // [Old code VNPAY]:                 var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
    // [Old code VNPAY]:                 var patient = appt != null ? await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == appt.PatientId) : null;
    // [Old code VNPAY]: 
    // [Old code VNPAY]:                 decimal examFee = (appt?.Note?.Contains("Gói khám:") == true) ? (ExtractPriceFromNote(appt.Note) ?? 250000m) : 250000m;
    // [Old code VNPAY]:                 decimal servicesFee = await ComputeClsFeeAsync(appointmentId);
    // [Old code VNPAY]:                 decimal medsFee = await ComputeMedsFeeAsync(appointmentId);
    // [Old code VNPAY]:                 decimal totalAmount = examFee + servicesFee + medsFee;
    // [Old code VNPAY]: 
    // [Old code VNPAY]:                 var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == appointmentId);
    // [Old code VNPAY]:                 if (invoice == null)
    // [Old code VNPAY]:                 {
    // [Old code VNPAY]:                     invoice = new Invoice
    // [Old code VNPAY]:                     {
    // [Old code VNPAY]:                         AppointmentId = appointmentId,
    // [Old code VNPAY]:                         PatientId = patient?.PatientId ?? (appt?.PatientId ?? 0),
    // [Old code VNPAY]:                         TotalAmount = totalAmount,
    // [Old code VNPAY]:                         PaidAmount = totalAmount,
    // [Old code VNPAY]:                         PaymentStatus = "paid",
    // [Old code VNPAY]:                         PaymentMethod = "vnpay",
    // [Old code VNPAY]:                         InvoiceDate = DateTime.UtcNow,
    // [Old code VNPAY]:                         CreatedAt = DateTime.UtcNow,
    // [Old code VNPAY]:                         UpdatedAt = DateTime.UtcNow
    // [Old code VNPAY]:                     };
    // [Old code VNPAY]:                     _context.Invoices.Add(invoice);
    // [Old code VNPAY]:                 }
    // [Old code VNPAY]:                 else
    // [Old code VNPAY]:                 {
    // [Old code VNPAY]:                     invoice.PaidAmount = totalAmount;
    // [Old code VNPAY]:                     invoice.TotalAmount = totalAmount;
    // [Old code VNPAY]:                     invoice.PaymentStatus = "paid";
    // [Old code VNPAY]:                     invoice.PaymentMethod = "vnpay";
    // [Old code VNPAY]:                     invoice.UpdatedAt = DateTime.UtcNow;
    // [Old code VNPAY]:                 }
    // [Old code VNPAY]:                 await _context.SaveChangesAsync();
    // [Old code VNPAY]: 
    // [Old code VNPAY]:                 // Gửi thông báo mobile
    // [Old code VNPAY]:                 if (patient != null && patient.UserId != Guid.Empty)
    // [Old code VNPAY]:                 {
    // [Old code VNPAY]:                     _context.Notifications.Add(new Notification
    // [Old code VNPAY]:                     {
    // [Old code VNPAY]:                         UserId = patient.UserId,
    // [Old code VNPAY]:                         Title = "Thanh Toán VNPAY Thành Công",
    // [Old code VNPAY]:                         Content = $"Hóa đơn viện phí của bạn ({patient.FullName}) đã được thanh toán thành công qua Cổng VNPAY.\n\nMÃ GIAO DỊCH: {response.TransactionId}\nSỐ TIỀN: {totalAmount:N0} VNĐ\n\nCảm ơn bạn đã sử dụng dịch vụ tại DTT Healthcare!",
    // [Old code VNPAY]:                         Type = "result",
    // [Old code VNPAY]:                         RelatedId = invoice.InvoiceId,
    // [Old code VNPAY]:                         RelatedType = "invoice",
    // [Old code VNPAY]:                         IsRead = false,
    // [Old code VNPAY]:                         CreatedAt = DateTime.UtcNow
    // [Old code VNPAY]:                     });
    // [Old code VNPAY]:                     await _context.SaveChangesAsync();
    // [Old code VNPAY]:                 }
    // [Old code VNPAY]: 
    // [Old code VNPAY]:                 string successHtml = $@"
    // [Old code VNPAY]: <!DOCTYPE html>
    // [Old code VNPAY]: <html>
    // [Old code VNPAY]: <head>
    // [Old code VNPAY]:     <meta charset='utf-8'/>
    // [Old code VNPAY]:     <title>Thanh Toán VNPAY Thành Công - DTT Healthcare</title>
    // [Old code VNPAY]:     <style>
    // [Old code VNPAY]:         body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #f0fdf4; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }}
    // [Old code VNPAY]:         .card {{ background: white; padding: 40px; border-radius: 16px; box-shadow: 0 10px 25px rgba(0,0,0,0.08); text-align: center; max-width: 480px; width: 90%; }}
    // [Old code VNPAY]:         .icon {{ font-size: 64px; color: #16a34a; margin-bottom: 16px; }}
    // [Old code VNPAY]:         h1 {{ color: #166534; font-size: 24px; margin-bottom: 8px; }}
    // [Old code VNPAY]:         p {{ color: #4b5563; font-size: 15px; line-height: 1.5; }}
    // [Old code VNPAY]:         .amount {{ font-size: 28px; font-weight: bold; color: #0284c7; margin: 16px 0; }}
    // [Old code VNPAY]:         .details {{ background: #f8fafc; border-radius: 8px; padding: 16px; text-align: left; font-size: 14px; color: #334155; margin-bottom: 24px; }}
    // [Old code VNPAY]:         .btn {{ display: inline-block; background: #16a34a; color: white; padding: 12px 24px; border-radius: 8px; text-decoration: none; font-weight: bold; }}
    // [Old code VNPAY]:     </style>
    // [Old code VNPAY]: </head>
    // [Old code VNPAY]: <body>
    // [Old code VNPAY]:     <div class='card'>
    // [Old code VNPAY]:         <div class='icon'>✅</div>
    // [Old code VNPAY]:         <h1>THANH TOÁN THÀNH CÔNG</h1>
    // [Old code VNPAY]:         <p>Giao dịch qua Cổng VNPAY đã được ghi nhận vào hệ thống bệnh viện.</p>
    // [Old code VNPAY]:         <div class='amount'>{totalAmount:N0} VNĐ</div>
    // [Old code VNPAY]:         <div class='details'>
    // [Old code VNPAY]:             <div><strong>Ca khám:</strong> #{appointmentId}</div>
    // [Old code VNPAY]:             <div><strong>Bệnh nhân:</strong> {patient?.FullName ?? "Bệnh nhân"}</div>
    // [Old code VNPAY]:             <div><strong>Mã giao dịch VNPAY:</strong> {response.TransactionId}</div>
    // [Old code VNPAY]:             <div><strong>Phương thức:</strong> Cổng VNPAY (VNPAY-QR / Thẻ ATM)</div>
    // [Old code VNPAY]:         </div>
    // [Old code VNPAY]:         <p style='color:#6b7280; font-size:13px;'>Bạn có thể đóng cửa sổ này và quay lại phần mềm.</p>
    // [Old code VNPAY]:     </div>
    // [Old code VNPAY]: </body>
    // [Old code VNPAY]: </html>";
    // [Old code VNPAY]:                 return Content(successHtml, "text/html; charset=utf-8");
    // [Old code VNPAY]:             }
    // [Old code VNPAY]:             else
    // [Old code VNPAY]:             {
    // [Old code VNPAY]:                 string failHtml = $@"
    // [Old code VNPAY]: <!DOCTYPE html>
    // [Old code VNPAY]: <html>
    // [Old code VNPAY]: <head>
    // [Old code VNPAY]:     <meta charset='utf-8'/>
    // [Old code VNPAY]:     <title>Thanh Toán Không Thành Công</title>
    // [Old code VNPAY]:     <style>
    // [Old code VNPAY]:         body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #fef2f2; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }}
    // [Old code VNPAY]:         .card {{ background: white; padding: 40px; border-radius: 16px; box-shadow: 0 10px 25px rgba(0,0,0,0.08); text-align: center; max-width: 480px; width: 90%; }}
    // [Old code VNPAY]:         .icon {{ font-size: 64px; color: #dc2626; margin-bottom: 16px; }}
    // [Old code VNPAY]:         h1 {{ color: #991b1b; font-size: 24px; margin-bottom: 8px; }}
    // [Old code VNPAY]:         p {{ color: #4b5563; font-size: 15px; line-height: 1.5; }}
    // [Old code VNPAY]:     </style>
    // [Old code VNPAY]: </head>
    // [Old code VNPAY]: <body>
    // [Old code VNPAY]:     <div class='card'>
    // [Old code VNPAY]:         <div class='icon'>❌</div>
    // [Old code VNPAY]:         <h1>GIAO DỊCH KHÔNG THÀNH CÔNG</h1>
    // [Old code VNPAY]:         <p>Giao dịch VNPAY đã bị hủy hoặc không thành công (Mã lỗi: {response.VnPayResponseCode}).</p>
    // [Old code VNPAY]:     </div>
    // [Old code VNPAY]: </body>
    // [Old code VNPAY]: </html>";
    // [Old code VNPAY]:                 return Content(failHtml, "text/html; charset=utf-8");
    // [Old code VNPAY]:             }
    // [Old code VNPAY]:         }
    // [Old code VNPAY]:         catch (Exception ex)
    // [Old code VNPAY]:         {
    // [Old code VNPAY]:             return StatusCode(500, new { success = false, message = "Lỗi xử lý callback VNPAY: " + ex.Message });
    // [Old code VNPAY]:         }
    // [Old code VNPAY]:     }
    // [Old code VNPAY]: 
    // [Old code VNPAY]:     // GET /api/Invoices/status/{appointmentId}
    // Kiểm tra trạng thái thanh toán hiện tại của ca khám
    [HttpGet("status/{appointmentId}")]
    public async Task<IActionResult> GetPaymentStatus(int appointmentId)
    {
        try
        {
            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.AppointmentId == appointmentId);
            bool isPaid = invoice != null && invoice.PaymentStatus == "paid";

            return Ok(new
            {
                success = true,
                appointmentId,
                isPaid,
                paymentStatus = invoice?.PaymentStatus ?? "unpaid",
                paymentMethod = invoice?.PaymentMethod ?? "cash",
                paidAmount = invoice?.PaidAmount ?? 0m,
                totalAmount = invoice?.TotalAmount ?? 0m,
                invoiceId = invoice?.InvoiceId ?? 0,
                updatedAt = invoice?.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private static string? GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ip.ToString().StartsWith("192.168."))
                {
                    return ip.ToString();
                }
            }
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ip.ToString().StartsWith("10."))
                {
                    return ip.ToString();
                }
            }
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(ip) &&
                    !ip.ToString().StartsWith("169.254") &&
                    !ip.ToString().StartsWith("172.24"))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return null;
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

    /// <summary>Đặt khám ngay cho 1 BỆNH NHÂN đã có sẵn hồ sơ (chọn từ tab "Đặt Khám Ngay", không phải
    /// đăng ký vãng lai mới hoàn toàn) — patients.patient_id thật. Khi có giá trị này, KHÔNG được ghi
    /// đè FullName/thông tin định danh đã đúng sẵn của họ, chỉ cập nhật CCCD vừa đối chiếu.</summary>
    public int? ExistingPatientId { get; set; }

    /// <summary>Đặt khám ngay cho 1 HỒ SƠ NGƯỜI THÂN đã có sẵn (family_members.member_id) — phải đi kèm
    /// OwnerPatientId. Khi có giá trị này, appointment được gán cho patient_id của CHỦ TÀI KHOẢN kèm
    /// member_id này, và chỉ hồ sơ family_members mới được cập nhật CCCD — KHÔNG được đụng tới hồ sơ
    /// patients của chủ tài khoản (đây chính là lỗi cần sửa: trước đây RegisterWalkIn luôn tìm
    /// patients theo SĐT, mà người thân thường dùng chung SĐT với chủ tài khoản do không có SĐT riêng,
    /// nên vô tình ghi đè họ tên/CCCD/BHYT thật của chủ tài khoản bằng thông tin của người thân).</summary>
    public int? MemberId { get; set; }
    public int? OwnerPatientId { get; set; }
}

public class VnPayPaymentRequestDto
{
    public int AppointmentId { get; set; }
}

public class PaypalCreateOrderDto
{
    public int AppointmentId { get; set; }
}
