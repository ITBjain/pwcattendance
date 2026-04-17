using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using System;

var builder = WebApplication.CreateBuilder(args);

// 1. BULLETPROOF PORT BINDING FOR RAILWAY
// This grabs Railway's exact port and forces .NET to use it.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

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
 