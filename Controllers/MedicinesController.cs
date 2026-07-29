using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MedicinesController : ControllerBase
{
    private readonly AppDbContext _context;

    public MedicinesController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/Medicines
    [HttpGet]
    public async Task<IActionResult> GetMedicines()
    {
        try
        {
            var medicines = await _context.Medicines
                .Where(m => m.Status == "Active")
                .Select(m => new {
                    MedicineId = m.MedicineId,
                    MedicineName = m.MedicineName,
                    Unit = m.Unit,
                    DefaultUsage = m.DefaultUsage ?? "Theo chỉ định bác sĩ"
                })
                .ToListAsync();

            return Ok(medicines);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
