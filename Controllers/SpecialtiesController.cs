using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;
using System.ComponentModel.DataAnnotations;

namespace DTT_Backend_API.Controllers;

public class CreateSpecialtyDto
{
    [Required(ErrorMessage = "Tên chuyên khoa không được để trống.")]
    public string SpecialtyName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Status { get; set; } = true;
}

public class UpdateSpecialtyDto
{
    [Required(ErrorMessage = "Tên chuyên khoa không được để trống.")]
    public string SpecialtyName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Status { get; set; } = true;
}

public class UpdateSpecialtyStatusDto
{
    public bool Status { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class SpecialtiesController : ControllerBase
{
    private readonly AppDbContext _context;

    public SpecialtiesController(AppDbContext context)
    {
        _context = context;
    }

    /* [OLD CODE COMMENTED OUT]
    [HttpGet]
    public async Task<IActionResult> GetSpecialties()
    {
        var list = await _context.Specialties.ToListAsync();

        // If database table is empty, auto-seed default specialties
        if (list.Count == 0)
        {
            var defaults = new List<Specialty>
            {
                new Specialty { SpecialtyName = "Nội tổng quát", Description = "Khám và điều trị các bệnh lý nội khoa chung", Status = true },
                new Specialty { SpecialtyName = "Nhi khoa", Description = "Chăm sóc sức khỏe và điều trị bệnh lý cho trẻ em", Status = true },
                new Specialty { SpecialtyName = "Sản phụ khoa", Description = "Khám thai, tư vấn và điều trị bệnh phụ khoa", Status = true },
                new Specialty { SpecialtyName = "Cơ xương khớp", Description = "Điều trị bệnh lý về xương, khớp, cột sống", Status = true },
                new Specialty { SpecialtyName = "Tim mạch", Description = "Tầm soát và điều trị bệnh lý tim mạch, huyết áp", Status = true },
                new Specialty { SpecialtyName = "Thần kinh", Description = "Tầm soát các bệnh lý hệ thần kinh và não bộ", Status = true },
                new Specialty { SpecialtyName = "Da liễu", Description = "Tư vấn và điều trị các bệnh về da", Status = true },
                new Specialty { SpecialtyName = "Chẩn đoán hình ảnh", Description = "Siêu âm, X-quang, chụp CT scanner", Status = true }
            };

            _context.Specialties.AddRange(defaults);
            await _context.SaveChangesAsync();
            list = await _context.Specialties.ToListAsync();
        }

        return Ok(list);
    }
    */

    // GET /api/specialties — Lấy danh sách tất cả chuyên khoa kèm số lượng bác sĩ
    [HttpGet]
    public async Task<IActionResult> GetSpecialties()
    {
        try
        {
            var list = await _context.Specialties.OrderBy(s => s.SpecialtyId).ToListAsync();

            // Auto-seed nếu database trống
            if (list.Count == 0)
            {
                // Trước đây chỉ seed 8 chuyên khoa — DB thật hiện đã có 11 (thêm Răng hàm mặt/Tai-Mũi-Họng/
                // Mắt qua Web Admin). Khớp đủ 11 để 1 DB mới/test bootstrap qua endpoint này không bị thiếu.
                var defaults = new List<Specialty>
                {
                    new Specialty { SpecialtyName = "Nội tổng quát", Description = "Khám và điều trị các bệnh lý nội khoa chung", Status = true },
                    new Specialty { SpecialtyName = "Nhi khoa", Description = "Chăm sóc sức khỏe và điều trị bệnh lý cho trẻ em", Status = true },
                    new Specialty { SpecialtyName = "Sản phụ khoa", Description = "Khám thai, tư vấn và điều trị bệnh phụ khoa", Status = true },
                    new Specialty { SpecialtyName = "Cơ xương khớp", Description = "Điều trị bệnh lý về xương, khớp, cột sống", Status = true },
                    new Specialty { SpecialtyName = "Tim mạch", Description = "Tầm soát và điều trị bệnh lý tim mạch, huyết áp", Status = true },
                    new Specialty { SpecialtyName = "Thần kinh", Description = "Tầm soát các bệnh lý hệ thần kinh và não bộ", Status = true },
                    new Specialty { SpecialtyName = "Da liễu", Description = "Tư vấn và điều trị các bệnh về da", Status = true },
                    new Specialty { SpecialtyName = "Chẩn đoán hình ảnh", Description = "Siêu âm, X-quang, chụp CT scanner", Status = true },
                    new Specialty { SpecialtyName = "Răng hàm mặt", Description = "Khám và điều trị các bệnh lý về răng, hàm, mặt", Status = true },
                    new Specialty { SpecialtyName = "Tai-Mũi-Họng", Description = "Khám và điều trị các bệnh lý về tai, mũi và họng", Status = true },
                    new Specialty { SpecialtyName = "Mắt", Description = "Khám, chẩn đoán và điều trị các bệnh lý về mắt", Status = true }
                };

                _context.Specialties.AddRange(defaults);
                await _context.SaveChangesAsync();
                list = await _context.Specialties.OrderBy(s => s.SpecialtyId).ToListAsync();
            }

            // Đếm số bác sĩ thuộc từng chuyên khoa
            var doctorCounts = await _context.Doctors
                .Where(d => d.SpecialtyId.HasValue)
                .GroupBy(d => d.SpecialtyId!.Value)
                .Select(g => new { SpecialtyId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SpecialtyId, x => x.Count);

            var result = list.Select((s, index) => new
            {
                specialtyId = s.SpecialtyId,
                id = s.SpecialtyId,
                stt = index + 1,
                specialtyName = s.SpecialtyName,
                name = s.SpecialtyName,
                description = s.Description ?? "",
                status = s.Status ? "Đang hoạt động" : "Ngưng hoạt động",
                rawStatus = s.Status,
                doctorCount = doctorCounts.ContainsKey(s.SpecialtyId) ? doctorCounts[s.SpecialtyId] : 0
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy danh sách Chuyên khoa.", error = ex.Message });
        }
    }

    // GET /api/specialties/{id} — Lấy thông tin 1 chuyên khoa
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSpecialtyById(int id)
    {
        try
        {
            var specialty = await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == id);
            if (specialty == null)
            {
                return NotFound(new { message = "Không tìm thấy Chuyên khoa." });
            }

            int doctorCount = await _context.Doctors.CountAsync(d => d.SpecialtyId == id);

            return Ok(new
            {
                specialtyId = specialty.SpecialtyId,
                id = specialty.SpecialtyId,
                specialtyName = specialty.SpecialtyName,
                name = specialty.SpecialtyName,
                description = specialty.Description ?? "",
                status = specialty.Status ? "Đang hoạt động" : "Ngưng hoạt động",
                rawStatus = specialty.Status,
                doctorCount = doctorCount
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy thông tin Chuyên khoa.", error = ex.Message });
        }
    }

    // POST /api/specialties — Thêm mới chuyên khoa
    [HttpPost]
    public async Task<IActionResult> CreateSpecialty([FromBody] CreateSpecialtyDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.SpecialtyName))
            {
                return BadRequest(new { message = "Tên chuyên khoa không được để trống." });
            }

            var trimmedName = dto.SpecialtyName.Trim();
            bool exists = await _context.Specialties.AnyAsync(s => s.SpecialtyName.ToLower() == trimmedName.ToLower());
            if (exists)
            {
                return BadRequest(new { message = $"Chuyên khoa '{trimmedName}' đã tồn tại." });
            }

            var specialty = new Specialty
            {
                SpecialtyName = trimmedName,
                Description = dto.Description?.Trim(),
                Status = dto.Status
            };

            _context.Specialties.Add(specialty);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Thêm mới Chuyên khoa thành công!",
                specialty = new
                {
                    specialtyId = specialty.SpecialtyId,
                    id = specialty.SpecialtyId,
                    specialtyName = specialty.SpecialtyName,
                    name = specialty.SpecialtyName,
                    description = specialty.Description ?? "",
                    status = specialty.Status ? "Đang hoạt động" : "Ngưng hoạt động",
                    rawStatus = specialty.Status,
                    doctorCount = 0
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi tạo Chuyên khoa mới.", error = ex.Message });
        }
    }

    // PUT /api/specialties/{id} — Cập nhật thông tin chuyên khoa
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSpecialty(int id, [FromBody] UpdateSpecialtyDto dto)
    {
        try
        {
            var specialty = await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == id);
            if (specialty == null)
            {
                return NotFound(new { message = "Không tìm thấy Chuyên khoa." });
            }

            if (!string.IsNullOrWhiteSpace(dto.SpecialtyName))
            {
                var trimmedName = dto.SpecialtyName.Trim();
                bool exists = await _context.Specialties.AnyAsync(s => s.SpecialtyId != id && s.SpecialtyName.ToLower() == trimmedName.ToLower());
                if (exists)
                {
                    return BadRequest(new { message = $"Tên chuyên khoa '{trimmedName}' đã được sử dụng." });
                }
                specialty.SpecialtyName = trimmedName;
            }

            specialty.Description = dto.Description?.Trim();
            specialty.Status = dto.Status;

            await _context.SaveChangesAsync();

            int doctorCount = await _context.Doctors.CountAsync(d => d.SpecialtyId == id);

            return Ok(new
            {
                success = true,
                message = "Cập nhật Chuyên khoa thành công!",
                specialty = new
                {
                    specialtyId = specialty.SpecialtyId,
                    id = specialty.SpecialtyId,
                    specialtyName = specialty.SpecialtyName,
                    name = specialty.SpecialtyName,
                    description = specialty.Description ?? "",
                    status = specialty.Status ? "Đang hoạt động" : "Ngưng hoạt động",
                    rawStatus = specialty.Status,
                    doctorCount = doctorCount
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi cập nhật Chuyên khoa.", error = ex.Message });
        }
    }

    // PUT /api/specialties/{id}/status — Khóa / Mở trạng thái chuyên khoa
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateSpecialtyStatus(int id, [FromBody] UpdateSpecialtyStatusDto dto)
    {
        try
        {
            var specialty = await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == id);
            if (specialty == null)
            {
                return NotFound(new { message = "Không tìm thấy Chuyên khoa." });
            }

            specialty.Status = dto.Status;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã {(specialty.Status ? "kích hoạt" : "khóa")} chuyên khoa thành công!",
                status = specialty.Status ? "Đang hoạt động" : "Ngưng hoạt động",
                rawStatus = specialty.Status
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi cập nhật trạng thái Chuyên khoa.", error = ex.Message });
        }
    }

    // DELETE /api/specialties/{id} — Xóa chuyên khoa
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSpecialty(int id)
    {
        try
        {
            var specialty = await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == id);
            if (specialty == null)
            {
                return NotFound(new { message = "Không tìm thấy Chuyên khoa." });
            }

            bool hasDoctors = await _context.Doctors.AnyAsync(d => d.SpecialtyId == id);
            if (hasDoctors)
            {
                // Nếu đã có bác sĩ thuộc chuyên khoa này, chuyển trạng thái sang ngưng hoạt động
                specialty.Status = false;
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Chuyên khoa đã có bác sĩ, đã chuyển trạng thái sang 'Ngưng hoạt động'." });
            }

            _context.Specialties.Remove(specialty);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã xóa Chuyên khoa thành công!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi xóa Chuyên khoa.", error = ex.Message });
        }
    }

    // GET /api/Specialties/with-doctors — Lấy danh sách chuyên khoa kèm tên bác sĩ (dùng cho form đăng ký vãng lai)
    [HttpGet("with-doctors")]
    public async Task<IActionResult> GetSpecialtiesWithDoctors()
    {
        try
        {
            var specialties = await _context.Specialties
                .Where(s => s.Status)
                .OrderBy(s => s.SpecialtyId)
                .ToListAsync();

            // Trước đây lọc bác sĩ bằng cách ĐOÁN theo tiền tố tên ("BS.", "Bác sĩ"...) — vừa loại
            // nhầm bác sĩ thật không đặt tên theo quy ước đó, vừa vô tình NHẬN VÀO các hồ sơ rác kiểu
            // "Bác sĩ C"/"Bác sĩ Tân" (tên có sẵn tiền tố "Bác sĩ"). Lọc đúng theo Status, khớp với quy
            // ước dùng xuyên suốt hệ thống (DoctorsController.GetDoctors): Active/OnLeave mới hiển thị,
            // Locked/Inactive bị ẩn — nhất quán với API bác sĩ theo chuyên khoa bên Mobile.
            var doctors = await _context.Doctors
                .Where(d => d.SpecialtyId.HasValue &&
                            (string.IsNullOrEmpty(d.Status) || d.Status == "Active" || d.Status == "OnLeave") &&
                            !d.IsTestData)
                .ToListAsync();

            var specDict = specialties.ToDictionary(s => s.SpecialtyId, s => s.SpecialtyName);

            // Loại bỏ bác sĩ thuộc chuyên khoa đã bị Admin vô hiệu hoá — trước đây không lọc, nên bác sĩ
            // đó vẫn hiện ra để chọn nhưng bị gắn nhầm nhãn "Nội tổng quát" thay vì bị ẩn đi.
            var doctorsWithActiveSpecialty = doctors
                .Where(d => d.SpecialtyId.HasValue && specDict.ContainsKey(d.SpecialtyId.Value))
                .ToList();

            var result = doctorsWithActiveSpecialty.Select(d =>
            {
                string specName = specDict[d.SpecialtyId!.Value];
                string degree = !string.IsNullOrEmpty(d.Degree) ? d.Degree : "BS.";
                string displayName = $"{specName} ({degree} {d.FullName})";
                return new
                {
                    specialtyId = d.SpecialtyId ?? 1,
                    specialtyName = specName,
                    displayName = displayName,
                    doctorId = d.DoctorId,
                    doctorName = d.FullName ?? "",
                    doctorDegree = degree
                };
            }).OrderBy(r => r.specialtyId).ThenBy(r => r.doctorName).ToList();

            return Ok(new { success = true, specialties = result });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}

