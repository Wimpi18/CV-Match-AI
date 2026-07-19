using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using CvMatchApi.Models;
using CvMatchApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CvMatchApi.Controllers;

/// <summary>
/// Controller for processing and structuring user professional profiles using Azure OpenAI and Cosmos DB.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ProfileController"/> class.
/// </remarks>
/// <param name="profileStructuringService">The profile structuring service using OpenAI.</param>
/// <param name="cosmosDbService">The Cosmos DB storage service.</param>
[Authorize]
[ApiController]
[Route("api/profile")]
public class ProfileController(
    IProfileStructuringService profileStructuringService,
    ICosmosDbService cosmosDbService
) : ControllerBase
{
    private readonly IProfileStructuringService _profileStructuringService =
        profileStructuringService
        ?? throw new ArgumentNullException(nameof(profileStructuringService));
    private readonly ICosmosDbService _cosmosDbService =
        cosmosDbService ?? throw new ArgumentNullException(nameof(cosmosDbService));

    /// <summary>
    /// Sends raw CV text and standardized skills to Azure OpenAI for structuring, then persists the output to Cosmos DB.
    /// </summary>
    /// <param name="request">The profiling request contenant raw text and classified lists.</param>
    /// <returns>The structured JSON profile.</returns>
    /// <response code="200">The profile structuring and storage succeeded.</response>
    /// <response code="400">If the request payload is invalid.</response>
    /// <response code="401">If the request is unauthorized.</response>
    [HttpPost("process")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ProcessProfileAsync([FromBody] ProfileProcessRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { Message = "Request body cannot be null." });
        }

        // 1. Get user email from claims to map to document key
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
        if (string.IsNullOrEmpty(email))
        {
            return Unauthorized(new { Message = "User email claim not found in JWT token." });
        }

        try
        {
            // 2. Call OpenAI to structure the raw text and skills
            string structuredJson = await _profileStructuringService
                .StructureProfileAsync(
                    request.CvText,
                    request.CanonicalSkills,
                    request.CustomSkills
                )
                .ConfigureAwait(false);

            // 3. Deserialize JSON to structure parts for Cosmos DB document properties
            var parsedProfile = Newtonsoft.Json.Linq.JObject.Parse(structuredJson);

            // Extract parts or fallback if properties are missing
            var personalInfo = parsedProfile["personalInfo"] ?? new Newtonsoft.Json.Linq.JObject();
            var experience = parsedProfile["experience"] ?? new Newtonsoft.Json.Linq.JArray();
            var education = parsedProfile["education"] ?? new Newtonsoft.Json.Linq.JArray();
            var skills = parsedProfile["skills"] ?? new Newtonsoft.Json.Linq.JObject();

            // 4. Build document to upsert
            var document = new UserProfileDocument
            {
                Id = email,
                UserId = email,
                PersonalInfo = personalInfo!,
                Experience = experience!,
                Education = education!,
                Skills = skills!,
                RawText = request.CvText,
                UpdatedAt = DateTime.UtcNow,
            };

            // 5. Upsert in Cosmos DB
            await _cosmosDbService.UpsertProfileAsync(document).ConfigureAwait(false);

            return Ok(
                new { Message = "Profile structured and saved successfully.", Profile = document }
            );
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    Message = "An error occurred while processing the professional profile.",
                    Detail = ex.Message,
                }
            );
        }
    }

    /// <summary>
    /// Retrieves the current user's structured profile from Cosmos DB.
    /// </summary>
    /// <returns>The structured user profile, or 204 No Content if not found.</returns>
    /// <response code="200">The profile was retrieved successfully.</response>
    /// <response code="204">If no profile exists for the user.</response>
    /// <response code="401">If the request is unauthorized.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfileAsync()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
        if (string.IsNullOrEmpty(email))
        {
            return Unauthorized(new { Message = "User email claim not found in JWT token." });
        }

        try
        {
            var profile = await _cosmosDbService.GetProfileAsync(email).ConfigureAwait(false);
            if (profile == null)
            {
                return NoContent();
            }

            return Ok(profile);
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    Message = "An error occurred while fetching the profile.",
                    Detail = ex.Message,
                }
            );
        }
    }
}

/// <summary>
/// Data transfer object representing a profile structuring and saving request.
/// </summary>
public class ProfileProcessRequest
{
    /// <summary>
    /// Gets or sets the raw unstructured text extracted from the CV.
    /// </summary>
    public string CvText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of standardized canonical skills.
    /// </summary>
    public List<string> CanonicalSkills { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of custom/unrecognized skills.
    /// </summary>
    public List<string> CustomSkills { get; set; } = new();
}
