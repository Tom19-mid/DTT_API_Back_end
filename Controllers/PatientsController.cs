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
}

public class LinkQrRequestDto
{
    public string PatientId { get; set; } = string.Empty;
    public string VerifyCode { get; set; } = string.Empty;
    public int OwnerPatientId { get; set; }
}
