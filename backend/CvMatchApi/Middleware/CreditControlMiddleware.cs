using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using CvMatchApi.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvMatchApi.Middleware;

/// <summary>
/// Middleware to intercept CV optimization requests and restrict them to 3 successful generations per user.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CreditControlMiddleware"/> class.
/// </remarks>
/// <param name="next">The next middleware delegate in the HTTP pipeline.</param>
public class CreditControlMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));

    /// <summary>
    /// Invokes the middleware logic to intercept requests on the optimization route and enforce the credit limit.
    /// </summary>
    /// <param name="context">The current HttpContext.</param>
    /// <returns>A task representing the middleware execution.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // Intercept POST requests to the optimize route
        if (
            context.Request.Path.StartsWithSegments("/api/cv/optimize")
            && context.Request.Method == HttpMethods.Post
        )
        {
            var email = context.User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(email))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context
                    .Response.WriteAsync(
                        JsonSerializer.Serialize(new { Message = "Unauthorized access." })
                    )
                    .ConfigureAwait(false);
                return;
            }

            // Resolve AppDbContext from the request services scope
            var dbContext = context.RequestServices.GetRequiredService<AppDbContext>();

            var user = await dbContext
                .Users.FirstOrDefaultAsync(u => u.Email == email)
                .ConfigureAwait(false);
            if (user == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context
                    .Response.WriteAsync(
                        JsonSerializer.Serialize(new { Message = "User profile not found." })
                    )
                    .ConfigureAwait(false);
                return;
            }

            // Count existing usage logs
            int usageCount = await dbContext
                .UsageLogs.CountAsync(ul => ul.UserId == user.Id)
                .ConfigureAwait(false);
            if (usageCount >= 3)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                var errorResponse = new
                {
                    Message = "Límite de generación gratuito alcanzado (Máximo 3 CVs)",
                };
                await context
                    .Response.WriteAsync(JsonSerializer.Serialize(errorResponse))
                    .ConfigureAwait(false);
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }
}
