using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using DTT_Backend_API.Data;
using DTT_Backend_API.Hubs;
using DTT_Backend_API.Services;

var builder = WebApplication.CreateBuilder(args);

// Bật hỗ trợ lưu Utc DateTime vào cột timestamp without time zone của PostgreSQL
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Add Services
// Nén response (gzip/brotli) — nhiều endpoint list (medicines, doctors, appointments) trả JSON
// khá nặng (field bị lặp cả camelCase lẫn PascalCase), nén giúp giảm băng thông đáng kể cho
// Mobile dùng mạng di động mà không cần đổi code từng controller.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.AddControllers();
// Đăng ký PaypalClient dạng Singleton theo chuẩn hướng dẫn tích hợp PayPal
builder.Services.AddSingleton<DTT_Backend_API.Models.PaypalClient>(x =>
    new DTT_Backend_API.Models.PaypalClient(
        builder.Configuration["PayPalOptions:ClientId"] ?? "",
        builder.Configuration["PayPalOptions:ClientSecret"] ?? "",
        builder.Configuration["PayPalOptions:Mode"] ?? "Sandbox"
    )
);

builder.Services.AddSingleton<IVnPayService, VnPayService>();
builder.Services.AddSignalR();
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
// Ép kết nối qua IPv4 bằng raw socket — workaround CHỈ áp dụng trên Windows, vì lý do ban đầu
// là né lỗi mạng IPv6 chập chờn trên máy dev Windows cụ thể (SocketsHttpHandler's Happy Eyeballs
// "thử IPv6 trước" bị treo/rớt kết nối). Khi deploy lên container Linux (Render/Docker), cách làm
// socket thủ công này gây Gemini API bị treo/lỗi do không tương thích với hạ tầng mạng/NAT của
// nền tảng cloud — phát hiện qua test thực tế ngày 2026-09-07, Chat AI trả về fallback "đang bận"
// dù key/model vẫn đúng. Trên Linux, dùng SocketsHttpHandler mặc định của .NET là đủ và ổn định hơn.
var geminiHttpClientBuilder = builder.Services.AddHttpClient<GeminiService>();
if (OperatingSystem.IsWindows())
{
    geminiHttpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
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
}

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

    // SignalR clients gửi JWT qua query string "access_token" (chuẩn khuyến nghị của ASP.NET Core
    // cho WebSocket/SSE transport — request upgrade không phải lúc nào cũng mang được header
    // Authorization tùy client/transport), CHỈ áp dụng cho path /hubs/* để không nới lỏng xác thực
    // của các endpoint REST khác.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
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

// Khi chạy sau reverse proxy chấm dứt HTTPS (Render, hoặc bất kỳ PaaS nào tương tự),
// ASP.NET Core tự thấy Request.Scheme là "http" (proxy chuyển tiếp plain HTTP vào container)
// trừ khi đọc header X-Forwarded-Proto — nếu không, các URL tự sinh (VD: checkoutUrl/qrUrl
// của PayPal, InvoicesController.GetPaypalInfo) sẽ luôn ra "http://" dù domain thật là HTTPS.
// Xóa KnownNetworks/KnownProxies (mặc định chỉ tin loopback) vì IP của proxy Render
// không cố định trước — object initializer "{ }" KHÔNG xóa được các mục mặc định
// đã có sẵn trong constructor, phải gọi .Clear() tường minh.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseResponseCompression();
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
app.MapHub<NotificationHub>("/hubs/notifications");

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