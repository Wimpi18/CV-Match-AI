using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CvMatchApi.Data;
using CvMatchApi.Models;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CvMatchApi.Controllers;

/// <summary>
/// Controller handling user authentication using Google OAuth and session JWT issuance.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AuthController"/> class.
/// </remarks>
/// <param name="context">The database context for user records.</param>
[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly HttpClient _httpClient = new();

    /// <summary>
    /// Redirects the user's browser to the Google OAuth consent screen.
    /// </summary>
    /// <returns>A redirect response to accounts.google.com.</returns>
    /// <response code="302">Redirects to Google consent screen.</response>
    [HttpGet("login")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Login()
    {
        var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? "dummy-client-id.apps.googleusercontent.com";
        var redirectUri = Environment.GetEnvironmentVariable("GOOGLE_CALLBACK_URL") ?? "http://localhost:5008/api/auth/callback";

        var authorizationUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                               $"client_id={Uri.EscapeDataString(clientId)}&" +
                               $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                               $"response_type=code&" +
                               $"scope=openid%20email%20profile";

        return Redirect(authorizationUrl);
    }

    /// <summary>
    /// Receives the Google callback with authorization code, registers or updates the user, and returns a 24-hour JWT token.
    /// </summary>
    /// <param name="code">The authorization code returned from Google.</param>
    /// <returns>A response containing the signed JWT token and user profile.</returns>
    /// <response code="200">Session started successfully.</response>
    /// <response code="400">If the code parameter is missing or exchange fails.</response>
    [HttpGet("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CallbackAsync([FromQuery] string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest(new { Message = "Authorization code is missing." });
        }

        string email;
        string name;

        var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? "dummy-client-id.apps.googleusercontent.com";
        var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? "dummy-secret";
        var redirectUri = Environment.GetEnvironmentVariable("GOOGLE_CALLBACK_URL") ?? "http://localhost:5008/api/auth/callback";

        // 1. Bypass check for integration tests or dummy environments
        if (code == "test-google-code" || clientId.StartsWith("dummy"))
        {
            email = "test-google-oauth@example.com";
            name = "Google Test User";
        }
        else
        {
            try
            {
                // Exchange authorization code for token
                var requestParams = new Dictionary<string, string>
                {
                    { "code", code },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "redirect_uri", redirectUri },
                    { "grant_type", "authorization_code" }
                };

                using var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(requestParams)).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string errContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return BadRequest(new { Message = "Failed to exchange authorization code with Google.", Detail = errContent });
                }

                var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>().ConfigureAwait(false);
                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.IdToken))
                {
                    return BadRequest(new { Message = "No Identity Token returned from Google." });
                }

                // Validate and decode ID Token
                var payload = await GoogleJsonWebSignature.ValidateAsync(tokenResponse.IdToken).ConfigureAwait(false);
                email = payload.Email;
                name = payload.Name;
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = "Error validating Google ID token.", Detail = ex.Message });
            }
        }

        try
        {
            // 2. Register or update user record in Azure SQL database
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email).ConfigureAwait(false);
            if (user == null)
            {
                user = new User
                {
                    Email = email,
                    Name = name,
                    RegisteredAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
            }
            else
            {
                user.Name = name;
                _context.Entry(user).State = EntityState.Modified;
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);

            // 3. Generate signed JWT that expires in 24 hours
            var tokenString = GenerateJwtToken(user.Email, user.Name);

            return Ok(new
            {
                Token = tokenString,
                Email = user.Email,
                Name = user.Name
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Database error saving user identity.", Detail = ex.Message });
        }
    }

    /// <summary>
    /// Generates a test JWT token directly for manual testing (Legacy method).
    /// </summary>
    /// <returns>A response containing the generated JWT token string.</returns>
    [HttpPost("token")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GenerateToken()
    {
        var tokenString = GenerateJwtToken("testuser@example.com", "testuser");
        return Ok(new { Token = tokenString });
    }

    private string GenerateJwtToken(string email, string name)
    {
        var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? "SuperSecretSecureKeyForCvMatchAi2026!";
        var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "CvMatchIssuer";
        var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "CvMatchAudience";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, "User")
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24), // Expire exactly in 24 hours
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private class GoogleTokenResponse
    {
        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }
}
