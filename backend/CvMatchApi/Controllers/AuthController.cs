using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace CvMatchApi.Controllers;

/// <summary>
/// Controller for handles authentication helper operations.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Generates a test JWT token for manual and automated API verification.
    /// </summary>
    /// <returns>A response containing the generated JWT token string.</returns>
    /// <response code="200">Returns the token.</response>
    [HttpPost("token")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GenerateToken()
    {
        var jwtKey =
            Environment.GetEnvironmentVariable("JWT_KEY")
            ?? "SuperSecretSecureKeyForCvMatchAi2026!";
        var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "CvMatchIssuer";
        var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "CvMatchAudience";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Role, "User"),
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.Now.AddHours(2),
            signingCredentials: creds
        );

        return Ok(new { Token = new JwtSecurityTokenHandler().WriteToken(token) });
    }
}
