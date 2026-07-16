using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CvMatchApi.Data;
using CvMatchApi.Models;
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
/// <param name="context">The database context for user logs registry.</param>
[Authorize]
[ApiController]
[Route("api/cv")]
public class CvController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Optimizes a CV based on target job description details. Enforces credit checks via middleware.
    /// </summary>
    /// <param name="request">The optimization request body.</param>
    /// <returns>A response confirming success and detailing the created usage log ID.</returns>
    /// <response code="200">CV successfully optimized and log entry generated.</response>
    /// <response code="401">If the request is unauthorized.</response>
    /// <response code="429">If the user has exceeded their free generation limit.</response>
    [HttpPost("optimize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> OptimizeAsync([FromBody] OptimizeRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { Message = "Request body cannot be null." });
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

        try
        {
            // Register usage log representing the successful generation/optimization
            var usageLog = new UsageLog
            {
                UserId = user.Id,
                Timestamp = DateTime.UtcNow,
                Description = $"CV Optimized for Job: {request.JobTitle}",
            };

            _context.UsageLogs.Add(usageLog);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            return Ok(new { Message = "CV optimizado con éxito.", LogId = usageLog.Id });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { Message = "Database error creating usage log.", Detail = ex.Message }
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
