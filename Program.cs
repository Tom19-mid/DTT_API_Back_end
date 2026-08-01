using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using DTT_Backend_API.Data;

var builder = WebApplication.CreateBuilder(args);

// Add Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(); // Built-in OpenAPI in .NET 9

// PostgreSQL DbContext
var connString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connString));

// JWT Authentication
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? "DTT_Healthcare_Super_Secret_Key_2026_Graduation_Project";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

// CORS (Allow React Native Mobile App & Expo)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// ── Auto-seed bắt buộc: Đảm bảo status_id=7 'CheckedIn' luôn tồn tại trong DB ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        var hasCheckedIn = db.AppointmentStatuses.Any(s => s.StatusId == 7);
        if (!hasCheckedIn)
        {
            db.AppointmentStatuses.Add(new DTT_Backend_API.Models.AppointmentStatus { StatusId = 7, StatusName = "CheckedIn" });
            db.SaveChanges();
            Console.WriteLine("[Seed] ✅ Đã thêm status_id=7 'CheckedIn' vào appointment_statuses.");
        }

        // Tự động chuẩn hóa các hóa đơn test 235k cũ về mức công khám tiêu chuẩn 250.000đ trong DB
        db.Database.ExecuteSqlRaw(@"
            UPDATE invoice_items SET unit_price = 250000.00, amount = 250000.00 WHERE unit_price = 235000.00 OR amount = 235000.00;
            UPDATE invoices SET total_amount = 250000.00, paid_amount = 250000.00 WHERE total_amount = 235000.00 OR paid_amount = 235000.00;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] ⚠️ Không thể seed CheckedIn status: {ex.Message}");
    }
}

app.UseCors("AllowAll");

app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health Check root endpoint: http://localhost:5000/
app.MapGet("/", () => Results.Ok(new
{
    status = "Online",
    service = "DTT Healthcare Backend API",
    time = DateTime.UtcNow,
    endpoints = new[]
    {
        "GET /api/specialties",
        "GET /api/doctors",
        "POST /api/auth/login",
        "POST /api/auth/register",
        "GET /openapi/v1.json"
    }
}));

app.Run();
