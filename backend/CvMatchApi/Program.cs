using System;
using System.IO;
using System.Text;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using CvMatchApi.Data;
using CvMatchApi.Middleware;
using CvMatchApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

// 1. Load .env file at startup
LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure CORS to allow Angular frontend
var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(frontendUrl.TrimEnd('/'))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Configure AppDbContext with Azure SQL Database connection string
var sqlConnectionString = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnectionString));

// Configure JWT Authentication
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException("A secure JWT_KEY environment variable of at least 256 bits (32 characters) must be configured.");
}
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

// Register Application Insights Telemetry
builder.Services.AddApplicationInsightsTelemetry();

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

// Configure Cosmos DB Client
var cosmosConnectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
if (!string.IsNullOrEmpty(cosmosConnectionString))
{
    builder.Services.AddSingleton(new CosmosClient(cosmosConnectionString));
}

// Register Cosmos DB Service
builder.Services.AddScoped<ICosmosDbService, CosmosDbService>();

// Configure Azure OpenAI Client
var openAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var openAiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
if (!string.IsNullOrEmpty(openAiEndpoint) && !string.IsNullOrEmpty(openAiKey))
{
    builder.Services.AddSingleton(
        new AzureOpenAIClient(
            new Uri(openAiEndpoint),
            new System.ClientModel.ApiKeyCredential(openAiKey)
        )
    );
}

// Register Profile Structuring Service
builder.Services.AddScoped<IProfileStructuringService, ProfileStructuringService>();

// Configure Azure AI Document Intelligence Client using Managed Identity
var dintelEndpoint = Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT");
if (!string.IsNullOrEmpty(dintelEndpoint))
{
    builder.Services.AddSingleton(
        new DocumentAnalysisClient(new Uri(dintelEndpoint), new DefaultAzureCredential())
    );
}

// Register Document Intelligence Service
builder.Services.AddScoped<IDocumentIntelligenceService, DocumentIntelligenceService>();

// Register CV Optimization Service
builder.Services.AddScoped<ICvOptimizationService, CvOptimizationService>();

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

// Custom Security Headers Middleware
app.Use(
    async (context, next) =>
    {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append(
            "Content-Security-Policy",
            "default-src 'none'; frame-ancestors 'none';"
        );
        context.Response.Headers.Append("Referrer-Policy", "no-referrer");
        await next();
    }
);

app.UseCors();

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
