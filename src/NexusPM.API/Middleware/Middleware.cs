using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Domain.Exceptions;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace NexusPM.API.Middleware;

/// <summary>
/// Catches all unhandled exceptions and maps them to RFC 7807 ProblemDetails responses.
/// This keeps controller actions clean — they never catch exceptions.
///
/// Mapping:
///   ValidationException       → 422 Unprocessable Entity
///   NotFoundException         → 404 Not Found
///   UnauthorizedException     → 403 Forbidden
///   DomainException           → 422 Unprocessable Entity
///   Everything else           → 500 Internal Server Error (generic message in production)
/// </summary>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IWebHostEnvironment env)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ctx, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext ctx, Exception ex)
    {
        var (statusCode, title, errors) = ex switch
        {
            ValidationException ve => (
                HttpStatusCode.UnprocessableEntity,
                "One or more validation errors occurred.",
                ve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => ToCamelCase(g.Key),
                        g => g.Select(e => e.ErrorMessage).ToArray())),

            NotFoundException nfe => (
                HttpStatusCode.NotFound,
                nfe.Message,
                (Dictionary<string, string[]>?)null),

            UnauthorizedException uae => (
                HttpStatusCode.Forbidden,
                uae.Message,
                (Dictionary<string, string[]>?)null),

            DuplicateException de => (
                HttpStatusCode.Conflict,
                de.Message,
                (Dictionary<string, string[]>?)null),

            BusinessRuleViolationException bre => (
                HttpStatusCode.UnprocessableEntity,
                bre.Message,
                (Dictionary<string, string[]>?)null),

            _ => (
                HttpStatusCode.InternalServerError,
                env.IsDevelopment() ? ex.Message : "An unexpected error occurred.",
                (Dictionary<string, string[]>?)null)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
            logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
        else
            logger.LogWarning(ex, "Handled exception: {Message}", ex.Message);

        ctx.Response.StatusCode  = (int)statusCode;
        ctx.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type     = $"https://nexuspm.io/errors/{statusCode.ToString().ToLowerInvariant().Replace(" ", "-")}",
            Title    = title,
            Status   = (int)statusCode,
            Instance = ctx.Request.Path,
        };

        problem.Extensions["traceId"] = ctx.TraceIdentifier;
        if (errors is not null)
            problem.Extensions["errors"] = errors;

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOpts));
    }

    private static string ToCamelCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}

/// <summary>
/// Resolves the current tenant (workspaceId) from the JWT and sets it on ICurrentTenant.
/// This runs after UseAuthentication so the principal is already populated.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ICurrentTenant tenant)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var workspaceIdClaim = ctx.User.FindFirstValue("workspace_id");
            if (Guid.TryParse(workspaceIdClaim, out var workspaceId))
                tenant.SetTenant(workspaceId);
        }
        await next(ctx);
    }
}

/// <summary>Adds security headers to every response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.Headers["X-Content-Type-Options"]    = "nosniff";
        ctx.Response.Headers["X-Frame-Options"]           = "DENY";
        ctx.Response.Headers["X-XSS-Protection"]          = "0";
        ctx.Response.Headers["Referrer-Policy"]           = "strict-origin-when-cross-origin";
        ctx.Response.Headers["Permissions-Policy"]        = "camera=(), microphone=(), geolocation=()";
        ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        await next(ctx);
    }
}

/// <summary>
/// Resolves the current authenticated user from the JWT claims principal.
/// Injected into commands and query handlers via ICurrentUser.
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid   UserId      => Guid.Parse(User?.FindFirstValue(ClaimTypes.NameIdentifier)
                                         ?? User?.FindFirstValue("sub")
                                         ?? Guid.Empty.ToString());
    public Guid   WorkspaceId => Guid.Parse(User?.FindFirstValue("workspace_id") ?? Guid.Empty.ToString());
    public string Email       => User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    public string Role        => User?.FindFirstValue("role") ?? string.Empty;
    public bool   IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role)          => Role.Equals(role, StringComparison.OrdinalIgnoreCase);
    public bool HasPermission(string permission) =>
        User?.Claims.Any(c => c.Type == "permission" && c.Value == permission) ?? false;
}

/// <summary>
/// Holds the current tenant ID for the duration of the request.
/// Set by TenantResolutionMiddleware; consumed by AppDbContext global query filters.
/// </summary>
public sealed class CurrentTenantService : ICurrentTenant
{
    public Guid WorkspaceId { get; private set; }
    public void SetTenant(Guid workspaceId) => WorkspaceId = workspaceId;
}
