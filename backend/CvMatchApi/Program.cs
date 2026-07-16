using System;
using System.IO;
using System.Text;
using Azure.Storage.Blobs;
using CvMatchApi.Data;
using CvMatchApi.Middleware;
using CvMatchApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

// 1. Load .env file at startup
LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure AppDbContext with Azure SQL Database connection string
var sqlConnectionString = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnectionString));

// Configure JWT Authentication
var jwtKey =
    Environment.GetEnvironmentVariable("JWT_KEY") ?? "SuperSecretSecureKeyForCvMatchAi2026!";
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "CvMatchIssuer";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "CvMatchAudience";

builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
    });

builder.Services.AddAuthorization();

// Configure Azure Blob Storage Client
var blobConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
if (!string.IsNullOrEmpty(blobConnectionString))
{
    builder.Services.AddSingleton(x => new BlobServiceClient(blobConnectionString));
}

// Register Custom Blob Storage Service
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();

// Register Skills Catalog Service
builder.Services.AddScoped<ISkillsCatalogService, SkillsCatalogService>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// 2. Initialize database / ensure tables exist
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.EnsureCreated();

    // Ensure JobPostings and UsageLogs tables are explicitly created since EF Core's EnsureCreated()
    // does not update schema if the database already exists.
    var sql =
        @"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'JobPostings')
        BEGIN
            CREATE TABLE JobPostings (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Title NVARCHAR(MAX) NOT NULL,
                Description NVARCHAR(MAX) NOT NULL,
                CreatedAt DATETIME2 NOT NULL
            );
        END

        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UsageLogs')
        BEGIN
            CREATE TABLE UsageLogs (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                UserId INT NOT NULL,
                Timestamp DATETIME2 NOT NULL,
                Description NVARCHAR(MAX) NOT NULL,
                CONSTRAINT FK_UsageLogs_Users_UserId FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );
        END";
    context.Database.ExecuteSqlRaw(sql);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => new { Message = "Hola Mundo" });

// Authentication must be called before Authorization
app.UseAuthentication();
app.UseAuthorization();

// Register Credit Control Middleware
app.UseMiddleware<CreditControlMiddleware>();

app.MapControllers();

app.Run();

/// <summary>
/// Helper method to find and load the .env file from the current directory or its parent directories.
/// </summary>
void LoadDotEnv()
{
    var currentDir = Directory.GetCurrentDirectory();
    while (!string.IsNullOrEmpty(currentDir))
    {
        var testPath = Path.Combine(currentDir, ".env");
        if (File.Exists(testPath))
        {
            foreach (var line in File.ReadLines(testPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                int idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;

                string key = line.Substring(0, idx).Trim();
                string val = line.Substring(idx + 1).Trim();

                // Strip surrounding quotes if present
                if (
                    (val.StartsWith('"') && val.EndsWith('"'))
                    || (val.StartsWith('\'') && val.EndsWith('\''))
                )
                {
                    val = val.Substring(1, val.Length - 2);
                }

                Environment.SetEnvironmentVariable(key, val);
            }
            break;
        }
        currentDir = Path.GetDirectoryName(currentDir);
    }
}
