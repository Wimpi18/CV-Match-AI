using System;
using System.IO;
using System.Text;
using Azure.Storage.Blobs;
using CvMatchApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

// 1. Load .env file at startup
LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure JWT Authentication
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? "SuperSecretSecureKeyForCvMatchAi2026!";
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "CvMatchIssuer";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "CvMatchAudience";

builder.Services.AddAuthentication(options =>
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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
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

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => new { Message = "Hola Mundo" });

// Authentication must be called before Authorization
app.UseAuthentication();
app.UseAuthorization();

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
                if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
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
