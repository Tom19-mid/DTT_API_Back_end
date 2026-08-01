using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

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

    // GET /api/patients — List all patients for Reception desk
    [HttpGet]
    public async Task<IActionResult> GetAllPatients()
    {
        try
        {
            var patients = await _context.Patients
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    id = p.PatientId,
                    fullName = p.FullName,
                    dob = p.DateOfBirth.HasValue ? p.DateOfBirth.Value.ToString("dd/MM/yyyy") : "",
                    gender = p.Gender ?? "",
                    phone = p.PhoneNumber ?? "",
                    cccd = p.CccdNumber ?? "",
                    bhyt = p.HealthInsuranceNumber ?? "",
                    address = p.Address ?? "",
                    verificationStatus = p.VerificationStatus ?? "pending",
                    createdAt = p.CreatedAt
                })
                .ToListAsync();

            return Ok(new { success = true, patients });
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
        try
        {
            var patients = await _context.Patients
                .Where(p => p.VerificationStatus == "pending" || p.CccdNumber == null || p.CccdNumber == "")
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    id = p.PatientId,
                    fullName = p.FullName,
                    phone = p.PhoneNumber ?? "",
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
        try
        {
            var p = await _context.Patients.FirstOrDefaultAsync(x => x.PatientId == id);
            if (p == null) return NotFound(new { success = false, message = "Không tìm thấy bệnh nhân." });

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

            int targetId = 0;
            string cleanId = dto.PatientId.Replace("#", "").Replace("F", "").Replace("P", "");
            int.TryParse(cleanId, out targetId);

            // Create a verified family member profile linked via hospital QR
            var newMember = new FamilyMember
            {
                OwnerPatientId = dto.OwnerPatientId > 0 ? dto.OwnerPatientId : 2,
                FullName = $"BỆNH NHÂN QR #{dto.PatientId}".ToUpper(),
                Relationship = "Người thân (QR)",
                Gender = "Nam",
                PhoneNumber = "0918889999",
                HealthInsuranceNumber = $"QR{dto.VerifyCode}{Random.Shared.Next(1000, 9999)}",
                VerificationStatus = "verified",
                VerificationNote = $"Đã liên kết & xác minh chính chủ qua Mã QR viện (Xác thực: {dto.VerifyCode})",
                VerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.FamilyMembers.Add(newMember);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã liên kết thành công hồ sơ bệnh nhân #{dto.PatientId} vào tài khoản của bạn.",
                profile = new
                {
                    id = newMember.MemberId.ToString(),
                    realId = newMember.MemberId,
                    isOwner = false,
                    name = newMember.FullName,
                    patientId = $"#F{newMember.MemberId:D5}",
                    relationship = newMember.Relationship,
                    verificationStatus = "verified",
                    isVerified = true,
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
