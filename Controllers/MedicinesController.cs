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
    public async Task<IActionResult> GetMedicines([FromQuery] string? specialty)
    {
        try
        {
            var count = await _context.Medicines.CountAsync();
            if (count < 10)
            {
                await Seed30MedicinesAsync();
            }

            var medicines = await _context.Medicines
                .Where(m => m.Status == "Active")
                .Select(m => new {
                    MedicineId = m.MedicineId,
                    CategoryId = m.CategoryId,
                    MedicineName = m.MedicineName,
                    Unit = m.Unit,
                    UnitPrice = m.UnitPrice,
                    StockQuantity = m.StockQuantity,
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

    private async Task Seed30MedicinesAsync()
    {
        try
        {
            if (!await _context.MedicineCategories.AnyAsync())
            {
                _context.MedicineCategories.AddRange(
                    new MedicineCategory { CategoryId = 1, CategoryName = "Thuốc Kháng sinh & Kháng viêm", Description = "Tai mũi họng, hô hấp" },
                    new MedicineCategory { CategoryId = 2, CategoryName = "Thuốc Giảm đau & Hạ sốt", Description = "Giảm đau tổng quát" },
                    new MedicineCategory { CategoryId = 3, CategoryName = "Thuốc Tim mạch & Huyết áp", Description = "Tim mạch, mỡ máu" },
                    new MedicineCategory { CategoryId = 4, CategoryName = "Thuốc Tiêu hóa & Dạ dày", Description = "Trào ngược, dạ dày" },
                    new MedicineCategory { CategoryId = 5, CategoryName = "Thuốc Cơ xương khớp", Description = "Thoái hóa khớp, Gút" },
                    new MedicineCategory { CategoryId = 6, CategoryName = "Thuốc Nhi khoa & Bổ sung", Description = "Vitamin, khoáng chất" },
                    new MedicineCategory { CategoryId = 7, CategoryName = "Thuốc Da liễu & Dị ứng", Description = "Kem bôi, mề đai" }
                );
                await _context.SaveChangesAsync();
            }

            var medList = new List<Medicine>
            {
                new Medicine { MedicineId = 1, CategoryId = 1, MedicineName = "Amoxicillin 500mg", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần, 2 lần/ngày sau ăn sáng, tối", Status = "Active" },
                new Medicine { MedicineId = 2, CategoryId = 1, MedicineName = "Augmentin 1g (Amoxicillin/Clavulanate)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần, 2 lần/ngày sau ăn", Status = "Active" },
                new Medicine { MedicineId = 3, CategoryId = 1, MedicineName = "Cefuroxime 500mg (Zinnat)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần, 2 lần/ngày sau ăn", Status = "Active" },
                new Medicine { MedicineId = 4, CategoryId = 1, MedicineName = "Azithromycin 500mg", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày trước ăn 1 giờ", Status = "Active" },
                new Medicine { MedicineId = 5, CategoryId = 1, MedicineName = "Ciprofloxacin 500mg", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần, 2 lần/ngày", Status = "Active" },

                new Medicine { MedicineId = 6, CategoryId = 2, MedicineName = "Paracetamol 500mg (Panadol Extra)", Unit = "Viên", DefaultUsage = "Uống 1-2 viên/lần khi sốt >38.5°C (cách 4-6h)", Status = "Active" },
                new Medicine { MedicineId = 7, CategoryId = 2, MedicineName = "Ibuprofen 400mg", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần, 2 lần/ngày sau ăn no", Status = "Active" },
                new Medicine { MedicineId = 8, CategoryId = 2, MedicineName = "Efferalgan Codeine 500mg", Unit = "Sủi", DefaultUsage = "Hòa 1 viên sủi vào 200ml nước uống khi đau", Status = "Active" },
                new Medicine { MedicineId = 9, CategoryId = 2, MedicineName = "Celecoxib 200mg (Celebrex)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày sau ăn", Status = "Active" },

                new Medicine { MedicineId = 10, CategoryId = 3, MedicineName = "Amlodipine 5mg (Norvasc)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày buổi sáng", Status = "Active" },
                new Medicine { MedicineId = 11, CategoryId = 3, MedicineName = "Losartan 50mg (Cozaar)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày buổi sáng", Status = "Active" },
                new Medicine { MedicineId = 12, CategoryId = 3, MedicineName = "Atorvastatin 20mg (Lipitor)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày vào buổi tối trước ngủ", Status = "Active" },
                new Medicine { MedicineId = 13, CategoryId = 3, MedicineName = "Concor 5mg (Bisoprolol)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày buổi sáng", Status = "Active" },
                new Medicine { MedicineId = 14, CategoryId = 3, MedicineName = "Aspirin 81mg (Stent/Chống đông)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày sau ăn trưa", Status = "Active" },

                new Medicine { MedicineId = 15, CategoryId = 4, MedicineName = "Nexium mups 40mg (Esomeprazole)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày trước ăn sáng 30 phút", Status = "Active" },
                new Medicine { MedicineId = 16, CategoryId = 4, MedicineName = "Phosphalugel (Gói sữa dạ dày)", Unit = "Gói", DefaultUsage = "Uống 1 gói/lần khi đau bụng hoặc sau ăn 2 giờ", Status = "Active" },
                new Medicine { MedicineId = 17, CategoryId = 4, MedicineName = "Debridat 100mg (Trimebutine)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần, 3 lần/ngày trước ăn", Status = "Active" },
                new Medicine { MedicineId = 18, CategoryId = 4, MedicineName = "Smecta 3g", Unit = "Gói", DefaultUsage = "Hòa 1 gói vào 50ml nước uống 2-3 lần/ngày", Status = "Active" },

                new Medicine { MedicineId = 19, CategoryId = 5, MedicineName = "Meloxicam 15mg (Mobic)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày sau ăn no", Status = "Active" },
                new Medicine { MedicineId = 20, CategoryId = 5, MedicineName = "Glucosamine Sulfate 1500mg", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày sau ăn", Status = "Active" },
                new Medicine { MedicineId = 21, CategoryId = 5, MedicineName = "Colchicine 1mg", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần theo chỉ định bác sĩ", Status = "Active" },
                new Medicine { MedicineId = 22, CategoryId = 5, MedicineName = "Myonal 50mg (Eperisone)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần, 3 lần/ngày sau ăn", Status = "Active" },

                new Medicine { MedicineId = 23, CategoryId = 6, MedicineName = "Siro Prospan 100ml", Unit = "Chai", DefaultUsage = "Uống 5ml/lần, 3 lần/ngày", Status = "Active" },
                new Medicine { MedicineId = 24, CategoryId = 6, MedicineName = "Hapacol 250mg", Unit = "Gói", DefaultUsage = "Hòa 1 gói vào nước uống khi trẻ sốt >38.5°C", Status = "Active" },
                new Medicine { MedicineId = 25, CategoryId = 6, MedicineName = "Vitamin C 1000mg", Unit = "Hộp", DefaultUsage = "Hòa 1 viên sủi vào 200ml nước uống mỗi sáng", Status = "Active" },
                new Medicine { MedicineId = 26, CategoryId = 6, MedicineName = "ZinC 70mg (Kẽm vi chất)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày sau ăn", Status = "Active" },

                new Medicine { MedicineId = 27, CategoryId = 7, MedicineName = "Telfast 180mg (Fexofenadine)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày buổi tối", Status = "Active" },
                new Medicine { MedicineId = 28, CategoryId = 7, MedicineName = "Claritin 10mg (Loratadine)", Unit = "Viên", DefaultUsage = "Uống 1 viên/lần/ngày", Status = "Active" },
                new Medicine { MedicineId = 29, CategoryId = 7, MedicineName = "Fucicort Cream 15g", Unit = "Tuýp", DefaultUsage = "Thoa 1 lớp mỏng lên vùng da bệnh 2 lần/ngày", Status = "Active" },
                new Medicine { MedicineId = 30, CategoryId = 7, MedicineName = "Diprospan Injectable", Unit = "Ống", DefaultUsage = "Tiêm bắp theo chỉ định chuyên khoa Da liễu", Status = "Active" }
            };

            foreach (var m in medList)
            {
                if (!await _context.Medicines.AnyAsync(x => x.MedicineId == m.MedicineId))
                {
                    _context.Medicines.Add(m);
                }
            }
            await _context.SaveChangesAsync();
        }
        catch { }
    }
}
