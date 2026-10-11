
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using System.ComponentModel.DataAnnotations;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAgentRepository agents,
    IPasswordHasher<Agent> passwordHasher,
    IConfiguration configuration) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }
        var email = request.Email.Trim();
        var agent = await agents.GetByEmailAsync(email, ct);

        if (agent is null ||
            !agent.IsActive ||
            passwordHasher.VerifyHashedPassword(
                agent, agent.PasswordHash, request.Password)
                == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var signingKey = configuration["Jwt:SigningKey"]!;
        var issuer = configuration["Jwt:Issuer"] ?? "SupportDesk";
        var audience = configuration["Jwt:Audience"] ?? "SupportDesk.Web";
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, agent.Id.ToString()),
            new Claim(ClaimTypes.Name, agent.FullName),
            new Claim(ClaimTypes.Email, agent.Email)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return Ok(new LoginResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            new AuthenticatedAgent(agent.Id, agent.FullName, agent.Email)));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.FindFirstValue(ClaimTypes.Name);
        var email = User.FindFirstValue(ClaimTypes.Email);

        if (id is null || name is null || email is null)
        {
            return Unauthorized();
        }

        return Ok(new AuthenticatedAgent(id, name, email));
    }
}

public sealed record LoginRequest(
    [param: Required]
    [param: EmailAddress]
    string Email,

    [param: Required]
    string Password);

public sealed record LoginResponse(
    string Token,
    DateTime ExpiresAtUtc,
    AuthenticatedAgent User);

public sealed record AuthenticatedAgent(
    string Id,
    string FullName,
    string Email)
{
    public AuthenticatedAgent(int id, string fullName, string email)
        : this(id.ToString(), fullName, email) { }
}
