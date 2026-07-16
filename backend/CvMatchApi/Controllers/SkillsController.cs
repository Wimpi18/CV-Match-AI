using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CvMatchApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CvMatchApi.Controllers;

/// <summary>
/// Controller for handling skills matching operations against the SQL Server taxonomy catalog.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SkillsController"/> class.
/// </remarks>
/// <param name="skillsCatalogService">The skills catalog service.</param>
[Authorize]
[ApiController]
[Route("api/skills")]
public class SkillsController(ISkillsCatalogService skillsCatalogService) : ControllerBase
{
    private readonly ISkillsCatalogService _skillsCatalogService =
        skillsCatalogService ?? throw new ArgumentNullException(nameof(skillsCatalogService));

    /// <summary>
    /// Matches a list of raw user skills against the legacy SQL Server taxonomy database.
    /// </summary>
    /// <param name="request">The matching request body containing raw skills.</param>
    /// <returns>A categorized list of canonical and custom skills.</returns>
    /// <response code="200">The skills catalog crossing succeeded.</response>
    /// <response code="400">If the request payload is invalid.</response>
    /// <response code="401">If the request is unauthorized.</response>
    [HttpPost("match")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MatchSkillsAsync([FromBody] SkillMatchRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { Message = "Request body cannot be null." });
        }

        try
        {
            var result = await _skillsCatalogService
                .MatchSkillsAsync(request.RawSkills)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { Message = "An error occurred while matching skills.", Detail = ex.Message }
            );
        }
    }
}

/// <summary>
/// Data transfer object representing a skills match request.
/// </summary>
public class SkillMatchRequest
{
    /// <summary>
    /// Gets or sets the list of raw skills extracted from a CV to be matched.
    /// </summary>
    public List<string> RawSkills { get; set; } = new();
}
