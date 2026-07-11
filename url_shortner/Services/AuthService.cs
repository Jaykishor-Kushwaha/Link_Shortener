using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using UrlShortener.Api.Configuration;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Models;
using UrlShortener.Api.DTOs;

namespace UrlShortener.Api.Services;

public class AuthService
{
    private readonly MongoDbContext _db;
    private readonly AppSettings _settings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(MongoDbContext db, AppSettings settings, ILogger<AuthService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task<TokenResponse> SignupAsync(SignupRequest request)
    {
        var email = request.Email.ToLowerInvariant().Trim();

        // Check if email already exists
        var existing = await _db.Users.Find(u => u.Email == email).FirstOrDefaultAsync();
        if (existing != null)
            throw new AppException("Email is already registered.", 409);

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            await _db.Users.InsertOneAsync(user);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new AppException("Email is already registered.", 409);
        }

        _logger.LogInformation("User registered: {Email}", email);
        return await GenerateTokenPairAsync(user);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request)
    {
        var email = request.Email.ToLowerInvariant().Trim();

        var user = await _db.Users.Find(u => u.Email == email).FirstOrDefaultAsync();
        if (user == null)
            throw new AppException("Invalid email or password.", 401);

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new AppException("Invalid email or password.", 401);

        _logger.LogInformation("User logged in: {Email}", email);
        return await GenerateTokenPairAsync(user);
    }

    /// <summary>
    /// Refresh token rotation with replay detection.
    /// If a used token is presented, the entire family is invalidated (all tokens in the chain).
    /// This detects token theft: the legitimate user's next refresh will fail,
    /// forcing re-login and alerting them to compromise.
    /// </summary>
    public async Task<TokenResponse> RefreshAsync(RefreshRequest request)
    {
        var tokenHash = HashToken(request.RefreshToken);

        var storedToken = await _db.RefreshTokens
            .Find(r => r.TokenHash == tokenHash)
            .FirstOrDefaultAsync();

        if (storedToken == null)
            throw new AppException("Invalid refresh token.", 401);

        if (storedToken.ExpiresAt < DateTime.UtcNow)
            throw new AppException("Refresh token has expired.", 401);

        // Replay detection: if this token was already used, someone is replaying it.
        // Invalidate the entire family to protect the legitimate user.
        if (storedToken.Used)
        {
            _logger.LogWarning("Refresh token replay detected for family {Family}. Invalidating entire family.", storedToken.Family);
            await _db.RefreshTokens.DeleteManyAsync(r => r.Family == storedToken.Family);
            throw new AppException("Refresh token has been reused. Session invalidated for security.", 401);
        }

        // Mark the current token as used (atomically)
        await _db.RefreshTokens.UpdateOneAsync(
            r => r.Id == storedToken.Id,
            Builders<RefreshToken>.Update.Set(r => r.Used, true));

        var user = await _db.Users.Find(u => u.Id == storedToken.UserId).FirstOrDefaultAsync();
        if (user == null)
            throw new AppException("User not found.", 401);

        // Issue a new token pair in the same family
        return await GenerateTokenPairAsync(user, storedToken.Family);
    }

    private async Task<TokenResponse> GenerateTokenPairAsync(User user, string? family = null)
    {
        var accessToken = GenerateAccessToken(user);
        var (refreshToken, refreshTokenHash) = GenerateRefreshToken();

        var tokenFamily = family ?? Guid.NewGuid().ToString();

        var storedRefreshToken = new RefreshToken
        {
            TokenHash = refreshTokenHash,
            UserId = user.Id,
            Family = tokenFamily,
            Used = false,
            ExpiresAt = DateTime.UtcNow.Add(_settings.RefreshTokenLifetime),
            CreatedAt = DateTime.UtcNow
        };

        await _db.RefreshTokens.InsertOneAsync(storedRefreshToken);

        return new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken
        };
    }

    private string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "url-shortener",
            audience: "url-shortener",
            claims: claims,
            expires: DateTime.UtcNow.Add(_settings.AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static (string Token, string Hash) GenerateRefreshToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(tokenBytes);
        var hash = HashToken(token);
        return (token, hash);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
