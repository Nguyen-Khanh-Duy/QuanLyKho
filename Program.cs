using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuanlykhoAPI.Models;
using QuanlykhoAPI.Services; // chứa AIService & GeminiOptions
using Quanlykhohang.Services;

var builder = WebApplication.CreateBuilder(args);

// Cho phép API lắng nghe từ ngoài LAN
builder.WebHost.UseUrls("http://0.0.0.0:5044", "https://0.0.0.0:7092");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ✅ Đăng ký DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ✅ Cấu hình GeminiOptions (AI key và model)
builder.Services.Configure<GeminiOptions>(
    builder.Configuration.GetSection("Gemini"));
builder.Services.AddScoped<AIService>();

// ✅ Thêm Distributed Cache (bắt buộc cho Session)
builder.Services.AddDistributedMemoryCache();

// ✅ Thêm Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ✅ Cho phép truy cập HttpContext trong service
builder.Services.AddHttpContextAccessor();

// ✅ Cho phép CORS để mobile app gọi API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMobileApp", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowMobileApp");

// ⚠️ Tắt HTTPS redirection khi test LAN
// app.UseHttpsRedirection();

app.UseRouting();

// ✅ Bắt buộc: Session phải nằm TRƯỚC Authorization
app.UseSession();

app.UseAuthorization();

app.MapControllers();

app.Run();
