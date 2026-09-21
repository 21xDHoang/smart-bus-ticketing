var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Khung xác thực — Hiếu/Dăm cắm JWT Bearer vào đây ở task story 22
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// Cho phép frontend React (localhost:5173) gọi API khi chạy local
const string DevCors = "DevCors";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p => p
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(DevCors);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
