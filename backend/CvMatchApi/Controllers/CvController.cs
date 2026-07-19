using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CvMatchApi.Data;
using CvMatchApi.Models;
using CvMatchApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CvMatchApi.Controllers;

/// <summary>
/// Controller for managing CV optimization operations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CvController"/> class.
/// </remarks>
[Authorize]
[ApiController]
[Route("api/cv")]
public class CvController(
    AppDbContext context,
    ICosmosDbService cosmosDbService,
    ISkillsCatalogService skillsCatalogService,
    ICvOptimizationService cvOptimizationService
) : ControllerBase
{
    private readonly AppDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));
    private readonly ICosmosDbService _cosmosDbService =
        cosmosDbService ?? throw new ArgumentNullException(nameof(cosmosDbService));
    private readonly ISkillsCatalogService _skillsCatalogService =
        skillsCatalogService ?? throw new ArgumentNullException(nameof(skillsCatalogService));
    private readonly ICvOptimizationService _cvOptimizationService =
        cvOptimizationService ?? throw new ArgumentNullException(nameof(cvOptimizationService));

    /// <summary>
    /// Optimizes a CV based on target job description details. Enforces credit checks via middleware.
    /// </summary>
    /// <param name="request">The optimization request body.</param>
    /// <returns>A response confirming success and detailing the optimized CV details.</returns>
    /// <response code="200">CV successfully optimized and log entry generated.</response>
    /// <response code="400">If the job description is invalid or if the profile doesn't exist.</response>
    /// <response code="401">If the request is unauthorized.</response>
    /// <response code="429">If the user has exceeded their free generation limit.</response>
    [HttpPost("optimize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> OptimizeAsync([FromBody] OptimizeRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { Message = "Request body cannot be null." });
        }

        // 1. Validate Job Description length (between 100 and 10,000 characters)
        if (
            string.IsNullOrWhiteSpace(request.JobDescription)
            || request.JobDescription.Length < 100
            || request.JobDescription.Length > 10000
        )
        {
            return BadRequest(
                new
                {
                    Message = "La descripción de la vacante debe tener entre 100 y 10,000 caracteres.",
                }
            );
        }

        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
        {
            return Unauthorized(new { Message = "User email not found in token." });
        }

        var user = await _context
            .Users.FirstOrDefaultAsync(u => u.Email == email)
            .ConfigureAwait(false);
        if (user == null)
        {
            return Unauthorized(new { Message = "User profile not registered." });
        }

        // 2. Retrieve structured candidate profile from Cosmos DB
        var profile = await _cosmosDbService.GetProfileAsync(user.Email).ConfigureAwait(false);
        if (profile == null)
        {
            return BadRequest(
                new
                {
                    Message = "No se encontró ningún perfil estructurado para su usuario. Por favor, suba su CV primero.",
                }
            );
        }

        // 3. Scan job description for matching taxonomy skills in SQL Server
        var matchingSkills = await _skillsCatalogService
            .FindMatchingSkillsInTextAsync(request.JobDescription)
            .ConfigureAwait(false);

        // 4. Generate optimized Markdown resume and ATS score using Azure OpenAI
        var optimizationResult = await _cvOptimizationService
            .OptimizeCvAsync(profile, request.JobTitle, request.JobDescription, matchingSkills)
            .ConfigureAwait(false);

        try
        {
            // 5. Register usage log to increment credit count in Azure SQL Server
            var usageLog = new UsageLog
            {
                UserId = user.Id,
                Timestamp = DateTime.UtcNow,
                Description = $"CV Optimized for Job: {request.JobTitle}",
            };

            _context.UsageLogs.Add(usageLog);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            // 6. Save optimized resume in Cosmos DB associated with the SQL UsageLog Id
            var optimizedDoc = new OptimizedCvDocument
            {
                Id = $"{user.Email}_optimized_{usageLog.Id}",
                UserId = user.Email,
                LogId = usageLog.Id,
                OptimizedCvMarkdown = optimizationResult.OptimizedCvMarkdown,
                AtsReportMarkdown = optimizationResult.AtsReportMarkdown,
                AtsMatchScore = optimizationResult.AtsMatchScore,
                JobTitle = request.JobTitle,
                JobDescription = request.JobDescription,
                CreatedAt = DateTime.UtcNow,
            };
            await _cosmosDbService.UpsertOptimizedCvAsync(optimizedDoc).ConfigureAwait(false);

            // 7. Return optimized content and ATS match score
            return Ok(
                new
                {
                    Message = "CV optimizado con éxito.",
                    LogId = usageLog.Id,
                    AtsMatchScore = optimizationResult.AtsMatchScore,
                    OptimizedCvMarkdown = optimizationResult.OptimizedCvMarkdown,
                    AtsReportMarkdown = optimizationResult.AtsReportMarkdown,
                }
            );
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    Message = "Database error creating usage log or saving optimized CV.",
                    Detail = ex.Message,
                }
            );
        }
    }
}

/// <summary>
/// Data transfer object representing a CV optimization request.
/// </summary>
public class OptimizeRequest
{
    /// <summary>
    /// Gets or sets the target job position title.
    /// </summary>
    public string JobTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the detailed description/requirements of the target job posting.
    /// </summary>
    public string JobDescription { get; set; } = string.Empty;
}
