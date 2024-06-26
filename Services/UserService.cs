using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VelorettiAPI.Models;

namespace VelorettiAPI.Services;

public class UserService
{
    private readonly DatabaseContext _context;
    private readonly IConfiguration _configuration;
    public UserService(DatabaseContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }
    //get all users
    public async Task<List<User>> GetAllUsersAsync()
    {
        return await _context.Users.ToListAsync();
    }

    //get user by Id
    public async Task<User> GetById(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            throw new KeyNotFoundException($"The user with the ID {id} was not found.");
        }
        return user;
    }
    //get user by email
    public async Task<User> GetUserByEmail(string email)
    {
        return await _context.Users.SingleOrDefaultAsync(user => user.Email == email) ?? throw new KeyNotFoundException($"User with email {email} not found.");
    }
    //create user
    public async Task CreateUser(User user)
    {
        var existingUser = await _context.Users.SingleOrDefaultAsync(u => u.Email == user.Email);
        if (existingUser != null)
        {
            throw new InvalidOperationException("Email not valid.");
        }

        user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
    }
    //verify user
    public async Task<bool> VerifyUser(string email, string password)
    {
        var user = await _context.Users.SingleOrDefaultAsync(user => user.Email == email);
        if (user == null)
        {
            return false;
        }
        return BCrypt.Net.BCrypt.Verify(password, user.Password);
    }
    //generate jwt token
    public string GenerateJWT(User user)
    {
        var JwtKey = _configuration["Jwt:key"];
        if (string.IsNullOrEmpty(JwtKey))
        {
            throw new ArgumentNullException("Jwt:key", "JWT key configuration is missing or null.");
        }

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("IsAdmin", user.IsAdmin.ToString()),
            new Claim("UserId", user.UserId.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddMinutes(120),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public string GeneratePasswordResetToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:key"] ?? string.Empty);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new Claim[]
            {
                new Claim("UserId", user.UserId.ToString())
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);  
    }
    public string GenerateResetLink(string token)
    {
        return $"{_configuration["FrontendUrl"]}/reset-password?token={token}";
    }

    public ClaimsPrincipal ValidatePasswordResetToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:key"] ?? string.Empty);
        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);
            return principal;
        }
        catch
        {
            return null;
        }
    }
    public async Task ResetPassword(User user, string password)
    {
        user.Password = BCrypt.Net.BCrypt.HashPassword(password);
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

}