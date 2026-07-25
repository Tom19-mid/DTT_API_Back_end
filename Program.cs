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

app.UseCors("AllowAll");

// Map OpenAPI JSON endpoint: http://localhost:5000/openapi/v1.json
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
