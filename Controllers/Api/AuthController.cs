using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.Api;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly IJwtService _jwtService;

    public AuthController(UserManager<ApplicationUser> userManager, ApplicationDbContext context, IJwtService jwtService)
    {
        _userManager = userManager;
        _context = context;
        _jwtService = jwtService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing != null)
        {
            return Conflict(new { message = "An account with that email already exists." });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        await _userManager.AddToRoleAsync(user, "Customer");

        return await IssueTokensAsync(user);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Incorrect email or password." });
        }

        return await IssueTokensAsync(user);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        var stored = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == request.RefreshToken);

        if (stored == null || !stored.IsActive || stored.User == null)
        {
            return Unauthorized(new { message = "That refresh token is invalid or has expired." });
        }

        // Rotate: revoke the old token and issue a brand new pair.
        stored.RevokedAt = DateTime.UtcNow;

        var response = await IssueTokensInternalAsync(stored.User);
        stored.ReplacedByToken = response.RefreshToken;
        await _context.SaveChangesAsync();

        return Ok(response);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RevokeRequest request)
    {
        var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == request.RefreshToken);
        if (stored == null || !stored.IsActive)
        {
            return NotFound(new { message = "That refresh token isn't active." });
        }

        stored.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Token revoked." });
    }

    private async Task<IActionResult> IssueTokensAsync(ApplicationUser user)
    {
        var response = await IssueTokensInternalAsync(user);
        await _context.SaveChangesAsync();
        return Ok(response);
    }

    private async Task<AuthResponse> IssueTokensInternalAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, expiresAt) = _jwtService.GenerateAccessToken(user, roles);
        var refreshToken = _jwtService.GenerateRefreshToken(user.Id, HttpContext.Connection.RemoteIpAddress?.ToString());

        _context.RefreshTokens.Add(refreshToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            AccessTokenExpiresAt = expiresAt,
            RefreshToken = refreshToken.Token,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToList()
        };
    }
}
