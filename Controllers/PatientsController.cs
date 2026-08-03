using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;
using System.Linq;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly AppDbContext _context;

    public PatientsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/patients — List all patients + hồ sơ người thân (family_members) chờ xác thực CCCD tại quầy
    // cho Lễ Tân. Trước đây chỉ trả về patients — hồ sơ người thân tự thêm qua App (đang "pending")
    // không có cách nào hiện lên màn "Xác Thực Hồ Sơ" để Lễ Tân duyệt khi họ đến quầy đối chiếu CCCD.
    [HttpGet]
    public async Task<IActionResult> GetAllPatients()
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            // patients.phone_number có thể NULL với các hồ sơ cũ/tạo qua vài luồng khác nhau,
            // trong khi users.phone_number (SĐT đăng nhập) LUÔN có giá trị thật — fallback sang
            // đó để Lễ Tân luôn tìm được bệnh nhân theo SĐT, tránh báo "Không tìm thấy hồ sơ" sai.
            var patients = await (
                from p in _context.Patients
                join u in _context.Users on p.UserId equals u.UserId into pu
                from u in pu.DefaultIfEmpty()
                select new ReceptionProfileDto
                {
                    Id = p.PatientId,
                    RecordType = "patient",
                    FullName = p.FullName,
                    Relationship = "Bản thân",
                    Dob = p.DateOfBirth.HasValue ? p.DateOfBirth.Value.ToString("dd/MM/yyyy") : "",
                    Gender = p.Gender ?? "",
                    Phone = !string.IsNullOrEmpty(p.PhoneNumber) ? p.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    Cccd = p.CccdNumber ?? "",
                    Bhyt = p.HealthInsuranceNumber ?? "",
                    Address = p.Address ?? "",
                    VerificationStatus = p.VerificationStatus ?? "pending",
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();

            var familyMembers = await (
                from m in _context.FamilyMembers
                join owner in _context.Patients on m.OwnerPatientId equals owner.PatientId into om
                from owner in om.DefaultIfEmpty()
                join u in _context.Users on (owner != null ? owner.UserId : Guid.Empty) equals u.UserId into mu
                from u in mu.DefaultIfEmpty()
                select new ReceptionProfileDto
                {
                    Id = m.MemberId,
                    RecordType = "family_member",
                    FullName = m.FullName,
                    Relationship = m.Relationship ?? "Người thân",
                    Dob = m.DateOfBirth.HasValue ? m.DateOfBirth.Value.ToString("dd/MM/yyyy") : "",
                    Gender = m.Gender ?? "",
                    // Người thân thường không có SĐT riêng — hiện SĐT của chủ tài khoản để Lễ Tân liên hệ/tra cứu
                    Phone = !string.IsNullOrEmpty(m.PhoneNumber) ? m.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    Cccd = m.CccdNumber ?? "",
                    Bhyt = m.HealthInsuranceNumber ?? "",
                    Address = m.Address ?? "",
                    VerificationStatus = m.VerificationStatus ?? "pending",
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync();

            var combined = patients.Concat(familyMembers).OrderByDescending(x => x.CreatedAt).ToList();

            return Ok(new { success = true, patients = combined });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // GET /api/patients/pending — Patients waiting CCCD verification
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingPatients()
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var patients = await (
                from p in _context.Patients
                join u in _context.Users on p.UserId equals u.UserId into pu
                from u in pu.DefaultIfEmpty()
                where p.VerificationStatus == "pending" || p.CccdNumber == null || p.CccdNumber == ""
                orderby p.CreatedAt descending
                select new
                {
                    id = p.PatientId,
                    fullName = p.FullName,
                    phone = !string.IsNullOrEmpty(p.PhoneNumber) ? p.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    cccd = p.CccdNumber ?? "",
                    bhyt = p.HealthInsuranceNumber ?? "",
                    verificationStatus = p.VerificationStatus ?? "pending"
                })
                .ToListAsync();

            return Ok(new { success = true, patients });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // PATCH /api/patients/{id}/verify — Receptionist approves CCCD verification
    [HttpPatch("{id}/verify")]
    public async Task<IActionResult> VerifyPatient(int id, [FromBody] VerifyPatientDto dto)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var p = await _context.Patients.FirstOrDefaultAsync(x => x.PatientId == id);
            if (p == null) return NotFound(new { success = false, message = "Không tìm thấy bệnh nhân." });

            // dto.CccdNumber là string non-nullable trong C# nhưng client vẫn có thể gửi JSON "null" —
            // System.Text.Json vẫn gán được null vào đó, gây NullReferenceException ở dto.CccdNumber.Length
            // bên dưới nếu không chặn sớm.
            if (string.IsNullOrWhiteSpace(dto.CccdNumber))
                return BadRequest(new { success = false, message = "Vui lòng nhập số CCCD." });

            Guid? currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                var receptionistUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 4 || u.Email == "letan.minhchau@gmail.com");
                currentUserId = receptionistUser?.UserId ?? Guid.Parse("ddb25ca6-80c8-434d-a05a-d4231c25e95b");
            }

            // 1. Cập nhật CCCD & trạng thái xác thực vào DB
            p.CccdNumber = dto.CccdNumber;
            p.VerificationStatus = "verified";
            p.VerifiedBy = currentUserId;
            p.VerifiedAt = DateTime.UtcNow;
            p.VerificationNote = $"Đã đối chiếu thẻ CCCD thực tế tại Quầy Lễ Tân. CCCD: {dto.CccdNumber}. Duyệt lúc: {DateTime.UtcNow:dd/MM/yyyy HH:mm}";
            p.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // 2. Tự động gửi thông báo đẩy vào DB để App Mobile nhận được
            try
            {
                var cccdMasked = dto.CccdNumber.Length >= 4
                    ? "****" + dto.CccdNumber.Substring(dto.CccdNumber.Length - 4)
                    : dto.CccdNumber;

                _context.Notifications.Add(new Notification
                {
                    UserId = p.UserId,
                    Title = "✅ Tài khoản đã được Xác Thực CCCD",
                    Content = $"Hồ sơ của bạn ({p.FullName}) đã được Lễ Tân Bệnh viện DTT Healthcare đối chiếu thẻ CCCD thực tế (CCCD: {cccdMasked}) và chính thức được XÁC THỰC. Bạn có thể đặt lịch khám và sử dụng đầy đủ các tính năng của ứng dụng!",
                    Type = "system",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            catch
            {
                // Notification failure should not block the main verify response
            }

            return Ok(new
            {
                success = true,
                message = "Đã xác thực CCCD và gửi thông báo thành công!",
                patientId = id,
                cccd = dto.CccdNumber,
                verificationStatus = "verified",
                notified = true
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // GET /api/patients/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPatient(int id)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, id)) return this.ForbidJson();
        try
        {
            var p = await _context.Patients.FirstOrDefaultAsync(x => x.PatientId == id);
            if (p == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy thông tin bệnh nhân." });
            }

            return Ok(new
            {
                success = true,
                patient = new
                {
                    id = p.PatientId,
                    fullName = p.FullName,
                    dob = p.DateOfBirth?.ToString("dd/MM/yyyy"),
                    gender = p.Gender,
                    phone = p.PhoneNumber,
                    cccd = p.CccdNumber,
                    bhyt = p.HealthInsuranceNumber,
                    address = p.Address,
                    verificationStatus = p.VerificationStatus
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // POST /api/patients/link-by-qr
    [HttpPost("link-by-qr")]
    public async Task<IActionResult> LinkByQr([FromBody] LinkQrRequestDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.VerifyCode))
            {
                return BadRequest(new { success = false, message = "Mã QR thiếu thông tin liên kết hợp lệ." });
            }

            // Không fallback về patient_id=2 nếu client thiếu OwnerPatientId — trước đây làm vậy sẽ
            // âm thầm gắn hồ sơ người thân QR vào TÀI KHOẢN CỦA NGƯỜI KHÁC (bất kỳ ai cũng có thể để
            // trống trường này và thêm được "người thân" vào hồ sơ bệnh nhân #2).
            if (dto.OwnerPatientId <= 0)
                return BadRequest(new { success = false, message = "Thiếu OwnerPatientId hợp lệ." });
            if (!await AccessControl.CanAccessPatientAsync(User, _context, dto.OwnerPatientId)) return this.ForbidJson();

            int targetId = 0;
            string cleanId = dto.PatientId.Replace("#", "").Replace("F", "").Replace("P", "");
            int.TryParse(cleanId, out targetId);

            // Tạo hồ sơ người thân liên kết qua QR nhưng ở trạng thái "pending" — KHÔNG tự động
            // "verified". VerifyCode ở đây không được đối chiếu với bất kỳ dữ liệu thật nào (chưa có
            // hạ tầng QR viện thật), nên trước đây bất kỳ bệnh nhân nào cũng tự tạo được hồ sơ người
            // thân "đã xác thực" giả với tên/SĐT/BHYT tùy ý. Giờ bắt buộc đi qua đúng hàng đợi Lễ Tân
            // duyệt CCCD thật như mọi hồ sơ người thân khác (FamilyMembersController.VerifyFamilyMember).
            var newMember = new FamilyMember
            {
                OwnerPatientId = dto.OwnerPatientId,
                FullName = $"BỆNH NHÂN QR #{dto.PatientId}".ToUpper(),
                Relationship = "Người thân (QR)",
                Gender = "Nam",
                PhoneNumber = "0918889999",
                HealthInsuranceNumber = $"QR{dto.VerifyCode}{Random.Shared.Next(1000, 9999)}",
                VerificationStatus = "pending",
                VerificationNote = $"Đã liên kết qua Mã QR viện (Mã: {dto.VerifyCode}) — chờ Lễ Tân đối chiếu CCCD thực tế để xác thực.",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.FamilyMembers.Add(newMember);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã liên kết hồ sơ bệnh nhân #{dto.PatientId} — vui lòng mang CCCD ra quầy Lễ Tân để hoàn tất xác thực.",
                profile = new
                {
                    id = newMember.MemberId.ToString(),
                    realId = newMember.MemberId,
                    isOwner = false,
                    name = newMember.FullName,
                    patientId = $"#F{newMember.MemberId:D5}",
                    relationship = newMember.Relationship,
                    verificationStatus = "pending",
                    isVerified = false,
                    verificationNote = newMember.VerificationNote,
                    dob = "01/01/1985",
                    gender = newMember.Gender,
                    phone = newMember.PhoneNumber,
                    bhyt = newMember.HealthInsuranceNumber
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi xử lý QR: " + ex.Message });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value
                 ?? User.FindFirst("userId")?.Value;
        if (!string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out Guid userId))
        {
            return userId;
        }
        return null;
    }
}

public class LinkQrRequestDto
{
    public string PatientId { get; set; } = string.Empty;
    public string VerifyCode { get; set; } = string.Empty;
    public int OwnerPatientId { get; set; }
}

public class VerifyPatientDto
{
    public string CccdNumber { get; set; } = string.Empty;
}

// Dùng chung cho cả hồ sơ bệnh nhân chính (patients) và hồ sơ người thân (family_members)
// trên màn "Xác Thực Hồ Sơ" của Lễ Tân — RecordType phân biệt để gọi đúng endpoint duyệt.
public class ReceptionProfileDto
{
    public int Id { get; set; }
    public string RecordType { get; set; } = "patient"; // "patient" | "family_member"
    public string? FullName { get; set; }
    public string Relationship { get; set; } = "Bản thân";
    public string? Dob { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Cccd { get; set; }
    public string? Bhyt { get; set; }
    public string? Address { get; set; }
    public string VerificationStatus { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
}
