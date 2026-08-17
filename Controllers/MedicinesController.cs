using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

#region DTOs
public class CreateMedicineDto
{
    public int CategoryId { get; set; } = 1;

    [Required(ErrorMessage = "Tên thuốc không được để trống.")]
    public string MedicineName { get; set; } = string.Empty;

    public string Unit { get; set; } = "Viên";
    public decimal UnitPrice { get; set; } = 15000m;
    public int StockQuantity { get; set; } = 100;
    public string? Description { get; set; }
    public string? DefaultUsage { get; set; }
    public string Status { get; set; } = "Active";
    public string? ExpiryDate { get; set; }
}

public class UpdateMedicineDto
{
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "Tên thuốc không được để trống.")]
    public string MedicineName { get; set; } = string.Empty;

    public string Unit { get; set; } = "Viên";
    public decimal UnitPrice { get; set; } = 15000m;
    public int StockQuantity { get; set; } = 0;
    public string? Description { get; set; }
    public string? DefaultUsage { get; set; }
    public string Status { get; set; } = "Active";
    public string? ExpiryDate { get; set; }
}

public class UpdateMedicineStatusDto
{
    public string Status { get; set; } = "Active";
}

public class CreateMedicineCategoryDto
{
    [Required(ErrorMessage = "Tên danh mục không được để trống.")]
    public string CategoryName { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string Status { get; set; } = "Active";
}

public class UpdateMedicineCategoryDto
{
    [Required(ErrorMessage = "Tên danh mục không được để trống.")]
    public string CategoryName { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string Status { get; set; } = "Active";
}

public class UpdateMedicineCategoryStatusDto
{
    public string Status { get; set; } = "Active";
}
#endregion

[ApiController]
[Route("api/[controller]")]
public class MedicinesController : ControllerBase
{
    private readonly AppDbContext _context;

    public MedicinesController(AppDbContext context)
    {
        _context = context;
    }

    /* [OLD CODE COMMENTED OUT]
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
    */

    #region MEDICINES API (BẢNG MEDICINES)

    // Chỉ kiểm tra seed 1 lần/vòng đời app — trước đây CountAsync() (quét đếm toàn bảng, không có gì
    // để dừng sớm như EXISTS) chạy trên MỌI lần gọi endpoint bán thuốc nóng nhất hệ thống, dù chỉ có
    // ý nghĩa đúng 1 lần khi DB rỗng lúc khởi động.
    private static bool _medicinesSeedChecked = false;

    // GET: api/Medicines
    [HttpGet]
    public async Task<IActionResult> GetMedicines()
    {
        try
        {
            if (!_medicinesSeedChecked)
            {
                var count = await _context.Medicines.CountAsync();
                if (count < 10)
                {
                    await Seed30MedicinesAsync();
                }
                _medicinesSeedChecked = true;
            }

            /*
            // Code cũ chưa dùng AsNoTracking():
            var categoriesMap = await _context.MedicineCategories.ToDictionaryAsync(c => c.CategoryId, c => c.CategoryName);
            var medicinesList = await _context.Medicines.OrderByDescending(m => m.MedicineId).ToListAsync();
            */
            var categoriesMap = await _context.MedicineCategories.AsNoTracking()
                .ToDictionaryAsync(c => c.CategoryId, c => c.CategoryName);

            // Chỉ Admin (web admin) mới thấy thuốc Inactive để quản lý/kích hoạt lại; các role khác
            // (vd: bác sĩ kê đơn trên WinForms) chỉ thấy thuốc đang bán — giữ đúng hành vi lọc gốc,
            // tránh kê nhầm thuốc đã ngưng bán (quy hồi sau khi API này được mở rộng cho web admin).
            var medsQuery = _context.Medicines.AsNoTracking().AsQueryable();
            bool isAdmin = User.FindFirst("role_id")?.Value == "1";
            if (!isAdmin) medsQuery = medsQuery.Where(m => m.Status == "Active");

            var medicinesList = await medsQuery
                .OrderByDescending(m => m.MedicineId)
                .ToListAsync();

            int sttIndex = 1;
            var result = medicinesList.Select(m => new
            {
                stt = sttIndex++,
                medicineId = m.MedicineId,
                id = m.MedicineId,
                categoryId = m.CategoryId,
                categoryName = categoriesMap.TryGetValue(m.CategoryId, out var catName) ? catName : "Chưa phân loại",
                category = categoriesMap.TryGetValue(m.CategoryId, out var catName2) ? catName2 : "Chưa phân loại",
                medicineName = m.MedicineName,
                name = m.MedicineName,
                unit = m.Unit,
                unitPrice = m.UnitPrice,
                price = m.UnitPrice,
                stockQuantity = m.StockQuantity,
                stock = m.StockQuantity,
                description = m.Description ?? "",
                defaultUsage = m.DefaultUsage ?? "Theo chỉ định bác sĩ",
                usage = m.DefaultUsage ?? "Theo chỉ định bác sĩ",
                status = NormalizeStatus(m.Status),
                rawStatus = m.Status,
                expiryDate = m.ExpiryDate.HasValue ? m.ExpiryDate.Value.ToString("yyyy-MM-dd") : null,
                createdAt = m.CreatedAt,
                updatedAt = m.UpdatedAt
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy danh sách thuốc: " + ex.Message });
        }
    }

    // GET: api/Medicines/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetMedicineById(int id)
    {
        try
        {
            var m = await _context.Medicines.FindAsync(id);
            if (m == null)
            {
                return NotFound(new { message = $"Không tìm thấy thuốc có mã #{id}" });
            }

            var categoryName = await _context.MedicineCategories
                .Where(c => c.CategoryId == m.CategoryId)
                .Select(c => c.CategoryName)
                .FirstOrDefaultAsync() ?? "Chưa phân loại";

            return Ok(new
            {
                medicineId = m.MedicineId,
                id = m.MedicineId,
                categoryId = m.CategoryId,
                categoryName = categoryName,
                category = categoryName,
                medicineName = m.MedicineName,
                name = m.MedicineName,
                unit = m.Unit,
                unitPrice = m.UnitPrice,
                price = m.UnitPrice,
                stockQuantity = m.StockQuantity,
                stock = m.StockQuantity,
                description = m.Description ?? "",
                defaultUsage = m.DefaultUsage ?? "Theo chỉ định bác sĩ",
                usage = m.DefaultUsage ?? "Theo chỉ định bác sĩ",
                status = NormalizeStatus(m.Status),
                rawStatus = m.Status,
                expiryDate = m.ExpiryDate.HasValue ? m.ExpiryDate.Value.ToString("yyyy-MM-dd") : null
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy thông tin thuốc: " + ex.Message });
        }
    }

    private async Task SyncSequencesAsync()
    {
        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SELECT setval(pg_get_serial_sequence('medicines', 'medicine_id'), (SELECT COALESCE(MAX(medicine_id), 1) FROM medicines));"
            );
        }
        catch { }

        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SELECT setval(pg_get_serial_sequence('medicine_categories', 'category_id'), (SELECT COALESCE(MAX(category_id), 1) FROM medicine_categories));"
            );
        }
        catch { }
    }

    private static DateOnly? ParseExpiryDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "MM/dd/yyyy" };
        if (DateOnly.TryParseExact(trimmed, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var exp))
        {
            return exp;
        }
        if (DateOnly.TryParse(trimmed, out var fallbackExp))
        {
            return fallbackExp;
        }
        return null;
    }

    // POST: api/Medicines
    [HttpPost]
    public async Task<IActionResult> CreateMedicine([FromBody] CreateMedicineDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await SyncSequencesAsync();

            var normalizedStatus = NormalizeStatusToDb(dto.Status);
            DateOnly? parsedExpiry = ParseExpiryDate(dto.ExpiryDate);

            var medicine = new Medicine
            {
                CategoryId = dto.CategoryId > 0 ? dto.CategoryId : 1,
                MedicineName = dto.MedicineName.Trim(),
                Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "Viên" : dto.Unit.Trim(),
                UnitPrice = dto.UnitPrice >= 0 ? dto.UnitPrice : 0m,
                StockQuantity = dto.StockQuantity >= 0 ? dto.StockQuantity : 0,
                Description = dto.Description?.Trim(),
                DefaultUsage = dto.DefaultUsage?.Trim(),
                Status = normalizedStatus,
                ExpiryDate = parsedExpiry,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Medicines.Add(medicine);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tạo mới thuốc thành công!", medicineId = medicine.MedicineId, medicine });
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { message = "Lỗi khi tạo thuốc mới: " + detail });
        }
    }

    // PUT: api/Medicines/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateMedicine(int id, [FromBody] UpdateMedicineDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var medicine = await _context.Medicines.FindAsync(id);
            if (medicine == null)
            {
                return NotFound(new { message = $"Không tìm thấy thuốc có mã #{id}" });
            }

            if (dto.CategoryId > 0)
            {
                medicine.CategoryId = dto.CategoryId;
            }
            medicine.MedicineName = dto.MedicineName.Trim();
            medicine.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? medicine.Unit : dto.Unit.Trim();
            medicine.UnitPrice = dto.UnitPrice;
            medicine.StockQuantity = dto.StockQuantity;
            medicine.Description = dto.Description?.Trim();
            medicine.DefaultUsage = dto.DefaultUsage?.Trim();
            medicine.Status = NormalizeStatusToDb(dto.Status);

            if (!string.IsNullOrWhiteSpace(dto.ExpiryDate))
            {
                medicine.ExpiryDate = ParseExpiryDate(dto.ExpiryDate);
            }
            medicine.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Cập nhật thuốc thành công!", medicine });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi cập nhật thuốc: " + ex.Message });
        }
    }

    // PUT: api/Medicines/5/status
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateMedicineStatus(int id, [FromBody] UpdateMedicineStatusDto dto)
    {
        try
        {
            var medicine = await _context.Medicines.FindAsync(id);
            if (medicine == null)
            {
                return NotFound(new { message = $"Không tìm thấy thuốc có mã #{id}" });
            }

            medicine.Status = NormalizeStatusToDb(dto.Status);
            medicine.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Cập nhật trạng thái thuốc thành công!", status = medicine.Status });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi đổi trạng thái thuốc: " + ex.Message });
        }
    }

    // DELETE: api/Medicines/5 (Chuyển sang Inactive theo yêu cầu người dùng, không xóa cứng)
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMedicine(int id)
    {
        try
        {
            var medicine = await _context.Medicines.FindAsync(id);
            if (medicine == null)
            {
                return NotFound(new { message = $"Không tìm thấy thuốc có mã #{id}" });
            }

            // Chuyển về Inactive thay vì xóa khỏi DB
            medicine.Status = "Inactive";
            medicine.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã chuyển thuốc sang trạng thái Ngưng hoạt động (Inactive) thành công!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi cập nhật trạng thái ngưng hoạt động cho thuốc: " + ex.Message });
        }
    }

    #endregion

    #region MEDICINE CATEGORIES API (BẢNG MEDICINE_CATEGORIES)

    // GET: api/Medicines/categories
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        try
        {
            if (!await _context.MedicineCategories.AnyAsync())
            {
                await Seed30MedicinesAsync();
            }

            var categories = await _context.MedicineCategories
                .OrderBy(c => c.CategoryId)
                .ToListAsync();

            // Đếm số lượng thuốc thuộc từng danh mục
            var medicineCounts = await _context.Medicines
                .GroupBy(m => m.CategoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

            int sttIndex = 1;
            var result = categories.Select(c => new
            {
                stt = sttIndex++,
                categoryId = c.CategoryId,
                id = c.CategoryId,
                categoryName = c.CategoryName,
                name = c.CategoryName,
                description = c.Description ?? "",
                status = NormalizeStatus(c.Status),
                rawStatus = c.Status,
                medicineCount = medicineCounts.TryGetValue(c.CategoryId, out var count) ? count : 0,
                createdAt = c.CreatedAt,
                updatedAt = c.UpdatedAt
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy danh sách danh mục thuốc: " + ex.Message });
        }
    }

    // GET: api/Medicines/categories/5
    [HttpGet("categories/{id:int}")]
    public async Task<IActionResult> GetCategoryById(int id)
    {
        try
        {
            var category = await _context.MedicineCategories.FindAsync(id);
            if (category == null)
            {
                return NotFound(new { message = $"Không tìm thấy danh mục thuốc có mã #{id}" });
            }

            var count = await _context.Medicines.CountAsync(m => m.CategoryId == id);

            return Ok(new
            {
                categoryId = category.CategoryId,
                id = category.CategoryId,
                categoryName = category.CategoryName,
                name = category.CategoryName,
                description = category.Description ?? "",
                status = NormalizeStatus(category.Status),
                rawStatus = category.Status,
                medicineCount = count
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy chi tiết danh mục thuốc: " + ex.Message });
        }
    }

    // POST: api/Medicines/categories
    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateMedicineCategoryDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await SyncSequencesAsync();

            var exists = await _context.MedicineCategories
                .AnyAsync(c => c.CategoryName.ToLower() == dto.CategoryName.Trim().ToLower());
            if (exists)
            {
                return BadRequest(new { message = $"Tên danh mục '{dto.CategoryName.Trim()}' đã tồn tại!" });
            }

            var category = new MedicineCategory
            {
                CategoryName = dto.CategoryName.Trim(),
                Description = dto.Description?.Trim(),
                Status = NormalizeStatusToDb(dto.Status),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.MedicineCategories.Add(category);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tạo mới danh mục thuốc thành công!", categoryId = category.CategoryId, category });
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { message = "Lỗi khi tạo danh mục thuốc: " + detail });
        }
    }

    // PUT: api/Medicines/categories/5
    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] UpdateMedicineCategoryDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var category = await _context.MedicineCategories.FindAsync(id);
            if (category == null)
            {
                return NotFound(new { message = $"Không tìm thấy danh mục thuốc có mã #{id}" });
            }

            var duplicate = await _context.MedicineCategories
                .AnyAsync(c => c.CategoryId != id && c.CategoryName.ToLower() == dto.CategoryName.Trim().ToLower());
            if (duplicate)
            {
                return BadRequest(new { message = $"Tên danh mục '{dto.CategoryName.Trim()}' đã trùng với danh mục khác!" });
            }

            category.CategoryName = dto.CategoryName.Trim();
            category.Description = dto.Description?.Trim();
            category.Status = NormalizeStatusToDb(dto.Status);
            category.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Cập nhật danh mục thuốc thành công!", category });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi cập nhật danh mục thuốc: " + ex.Message });
        }
    }

    // PUT: api/Medicines/categories/5/status
    [HttpPut("categories/{id:int}/status")]
    public async Task<IActionResult> UpdateCategoryStatus(int id, [FromBody] UpdateMedicineCategoryStatusDto dto)
    {
        try
        {
            var category = await _context.MedicineCategories.FindAsync(id);
            if (category == null)
            {
                return NotFound(new { message = $"Không tìm thấy danh mục thuốc có mã #{id}" });
            }

            category.Status = NormalizeStatusToDb(dto.Status);
            category.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Cập nhật trạng thái danh mục thuốc thành công!", status = category.Status });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi đổi trạng thái danh mục thuốc: " + ex.Message });
        }
    }

    // DELETE: api/Medicines/categories/5 (Chuyển sang Inactive theo yêu cầu người dùng, không xóa cứng)
    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        try
        {
            var category = await _context.MedicineCategories.FindAsync(id);
            if (category == null)
            {
                return NotFound(new { message = $"Không tìm thấy danh mục thuốc có mã #{id}" });
            }

            category.Status = "Inactive";
            category.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã chuyển danh mục thuốc sang trạng thái Ngưng hoạt động (Inactive) thành công!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi ngưng hoạt động danh mục thuốc: " + ex.Message });
        }
    }

    #endregion

    #region HELPER METHODS
    private static string NormalizeStatus(string status)
    {
        if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Đang hoạt động", StringComparison.OrdinalIgnoreCase))
        {
            return "Đang hoạt động";
        }
        return "Ngưng hoạt động";
    }

    private static string NormalizeStatusToDb(string status)
    {
        if (string.Equals(status, "Đang hoạt động", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "Active";
        }
        return "Inactive";
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
    #endregion
}

