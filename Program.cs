using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using DTT_Backend_API.Data;
using DTT_Backend_API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DTT Healthcare API",
        Version = "v1",
        Description = "Hệ thống API quản lý phòng khám & đặt lịch khám bệnh DTT Healthcare"
    });

    
    // Cấu hình nút "Authorize" để test API dùng Token JWT
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Dán trực tiếp chuỗi token JWT của bạn vào đây"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

/* Khi dùng Swagger thì không dùng AddOpenApi() */
//builder.Services.AddOpenApi(); // Built-in OpenAPI in .NET 9 (Đã comment để dùng Swashbuckle 7.0 tránh xung đột)

// PostgreSQL DbContext
var connString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connString));

// Gemini API — GeminiService.GetReplyAsync tự set timeout theo Gemini:TimeoutSeconds
// (xem Tai Lieu/ai_chatbot_roadmap.md mục 5.6), HttpClient chỉ cần dùng chung factory.
// Ép kết nối qua IPv4 — một số mạng có IPv6 chập chờn/bị chặn khiến SocketsHttpHandler's Happy
// Eyeballs "thử IPv6 trước" bị treo/rớt kết nối giữa chừng (SocketException khi đọc TLS response),
// dù IPv4 vẫn thông bình thường (đã kiểm chứng qua curl/PowerShell tới cùng endpoint).
builder.Services.AddHttpClient<GeminiService>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var entry = await System.Net.Dns.GetHostEntryAsync(context.DnsEndPoint.Host, System.Net.Sockets.AddressFamily.InterNetwork, cancellationToken);
            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(entry.AddressList[0], context.DnsEndPoint.Port, cancellationToken);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    });

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

// Mọi endpoint yêu cầu JWT hợp lệ theo mặc định — controller/action nào cần công khai
// (đăng nhập, đăng ký, OTP...) phải tự đánh dấu [AllowAnonymous] tường minh.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
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
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "DTT Healthcare API v1");
    c.RoutePrefix = "swagger"; // Đường dẫn truy cập sẽ là /swagger
});


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
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] ⚠️ Không thể seed CheckedIn status: {ex.Message}");
    }
}

app.UseCors("AllowAll");

// Phục vụ ảnh siêu âm KTV đính kèm (wwwroot/uploads/...) qua URL tĩnh /uploads/...
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads"));
app.UseStaticFiles();

/* Khi dùng Swagger thì không dùng app.MapOpenApi */
//app.MapOpenApi(); // Built-in OpenAPI in .NET 9 (Đã comment để dùng Swashbuckle 7.0 tránh xung đột)

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
})).AllowAnonymous();

app.Run();