
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using SupportDesk.Presentation.Controllers;

namespace SupportDesk.UnitTests.Authentication;

public sealed class AuthControllerTests
{
    private const string Email = "alex@example.com";
    private const string Password = "Correct_Password1!";
    private const string SigningKey =
        "test-signing-key-that-is-at-least-32-bytes-long";

    private readonly Mock<IAgentRepository> _agents = new();
    private readonly PasswordHasher<Agent> _hasher = new();
    private readonly IConfiguration _configuration =
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:Issuer"] = "SupportDesk",
                ["Jwt:Audience"] = "SupportDesk.Web"
            })
            .Build();

    private AuthController CreateController() =>
        new(_agents.Object, _hasher, _configuration);

    private Agent CreateAgent(bool active = true)
    {
        var agent = new Agent(
            "Alex Turner",
            Email,
            maxOpenTickets: 10,
            DateTime.UtcNow);

        agent.SetPasswordHash(_hasher.HashPassword(agent, Password));

        if (!active)
        {
            agent.Deactivate();
        }

        return agent;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndUser()
    {
        var agent = CreateAgent();
        _agents.Setup(a => a.GetByEmailAsync(
                Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var result = await CreateController().Login(
            new LoginRequest(Email, Password), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var login = Assert.IsType<LoginResponse>(response.Value);

        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        Assert.Equal(agent.FullName, login.User.FullName);
        Assert.Equal(agent.Email, login.User.Email);
        Assert.True(login.ExpiresAtUtc > DateTime.UtcNow);
        Assert.True(login.ExpiresAtUtc <= DateTime.UtcNow.AddHours(1));
    }

    [Fact]
    public async Task Login_WithIncorrectPassword_ReturnsUnauthorized()
    {
        var agent = CreateAgent();
        _agents.Setup(a => a.GetByEmailAsync(
                Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var result = await CreateController().Login(
            new LoginRequest(Email, "Wrong_Password1!"),
            CancellationToken.None);

        var response = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        _agents.Setup(a => a.GetByEmailAsync(
                Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var result = await CreateController().Login(
            new LoginRequest(Email, Password), CancellationToken.None);

        var response = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithInactiveAgent_ReturnsUnauthorized()
    {
        var agent = CreateAgent(active: false);
        _agents.Setup(a => a.GetByEmailAsync(
                Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var result = await CreateController().Login(
            new LoginRequest(Email, Password), CancellationToken.None);

        var response = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, response.StatusCode);
    }

    [Theory]
    [InlineData("", "some-password")]
    [InlineData("alex@example.com", "")]
    [InlineData(" ", "some-password")]
    [InlineData("alex@example.com", " ")]
    public async Task Login_WithMissingCredentials_ReturnsBadRequest(
        string email,
        string password)
    {
        var result = await CreateController().Login(
            new LoginRequest(email, password), CancellationToken.None);

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, response.StatusCode);

        _agents.Verify(
            a => a.GetByEmailAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}