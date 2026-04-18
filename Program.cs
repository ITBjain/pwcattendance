using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using System;

var builder = WebApplication.CreateBuilder(args);

// 1. DYNAMIC PORT BINDING (Works Locally + on Railway)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    // If it finds a port (like on Railway), use it.
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
// If 'port' is null (like on your Mac), it will automatically fall back 
// to using Port 5086 from your launchSettings.json!

// 2. Add Controllers
builder.Services.AddControllers();

// 3. Configure MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("🚨 Database connection string 'DefaultConnection' is missing!");
}

// 4. FIX: Use Hardcoded Version instead of AutoDetect
// This stops the app from freezing/crashing if Hostinger is slow to respond on boot.
var serverVersion = new MySqlServerVersion(new Version(8, 0, 30)); 

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));
// Register the R2 Storage Service
builder.Services.AddSingleton<PwcApi.Services.R2StorageService>();
// 5. Add Swagger Gen 
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection(); <-- KEEP THIS DELETED/COMMENTED OUT
app.UseAuthorization();
app.MapControllers();

app.Run();

//hello world this is a test line i a, typing right now to see if it works  actually and  think it will do











