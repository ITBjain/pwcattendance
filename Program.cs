using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using System;
using Scalar.AspNetCore; // 👈 Brings in the new UI

var builder = WebApplication.CreateBuilder(args);

// 1. Add Controllers
builder.Services.AddControllers();

// 2. Configure MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("🚨 Database connection string 'DefaultConnection' is missing!");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 3. Add Modern .NET 10 OpenAPI (Replaces AddSwaggerGen)
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Generates the API specification
    app.MapOpenApi();
    
    // Maps the interactive UI (Replaces UseSwaggerUI)
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();