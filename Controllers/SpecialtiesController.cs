using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpecialtiesController : ControllerBase
{
    private readonly AppDbContext _context;

    public SpecialtiesController(AppDbContext context)
    {
        _context = context;
    }

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
}
