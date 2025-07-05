using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AuthApi.Data;
using AuthApi.DTOs.Auth;
using AuthApi.Models;
using AuthApi.Repository.Interfaces;
using AuthApi.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AuthApi.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;
        private readonly IRepository<RefreshToken> _refreshTokenRepository;
        private readonly ApplicationDbContext _context;

        public AuthService(
            UserManager<User> userManager,
            IConfiguration configuration,
            IRepository<RefreshToken> refreshTokenRepository,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _configuration = configuration;
            _refreshTokenRepository = refreshTokenRepository;
            _context = context;
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest registerRequest)
        {
            var existingUser = await _userManager.FindByEmailAsync(registerRequest.Email);
            if (existingUser != null)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "User with this email already exists." }
                };
            }

            var newUser = new User
            {
                Email = registerRequest.Email,
                UserName = registerRequest.Email,
                FirstName = registerRequest.FirstName,
                LastName = registerRequest.LastName,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            var isCreated = await _userManager.CreateAsync(newUser, registerRequest.Password);
            if (!isCreated.Succeeded)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = isCreated.Errors.Select(x => x.Description)
                };
            }

            // Assign default role
            await _userManager.AddToRoleAsync(newUser, "User");

            var jwtToken = await GenerateJwtToken(newUser);
            var refreshToken = GenerateRefreshToken(newUser.Id, jwtToken.Id);

            await _refreshTokenRepository.AddAsync(refreshToken);

            return new AuthResponse
            {
                Success = true,
                Token = new JwtSecurityTokenHandler().WriteToken(jwtToken),
                RefreshToken = refreshToken.Token
            };
        }

        public async Task<AuthResponse> LoginAsync(AuthRequest authRequest)
        {
            var existingUser = await _userManager.FindByEmailAsync(authRequest.Email);
            if (existingUser == null)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "User does not exist." }
                };
            }

            if (!existingUser.IsActive)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "User account is inactive." }
                };
            }

            var isCorrect = await _userManager.CheckPasswordAsync(existingUser, authRequest.Password);
            if (!isCorrect)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "Invalid credentials." }
                };
            }

            var jwtToken = await GenerateJwtToken(existingUser);
            var refreshToken = GenerateRefreshToken(existingUser.Id, jwtToken.Id);

            // Revoke all previous refresh tokens
            await RevokeAllRefreshTokensForUser(existingUser.Id);

            await _refreshTokenRepository.AddAsync(refreshToken);

            return new AuthResponse
            {
                Success = true,
                Token = new JwtSecurityTokenHandler().WriteToken(jwtToken),
                RefreshToken = refreshToken.Token
            };
        }

        public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest refreshTokenRequest)
        {
            var principal = GetPrincipalFromExpiredToken(refreshTokenRequest.Token);
            if (principal == null)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "Invalid token." }
                };
            }

            var userId = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "Invalid token." }
                };
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || !user.IsActive)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "User not found or inactive." }
                };
            }

            var storedRefreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == refreshTokenRequest.RefreshToken && rt.UserId == userId);
            if (storedRefreshToken == null)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "Refresh token not found." }
                };
            }

            if (DateTime.UtcNow > storedRefreshToken.ExpiryDate)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "Refresh token expired." }
                };
            }

            if (storedRefreshToken.IsUsed || storedRefreshToken.IsRevoked)
            {
                return new AuthResponse
                {
                    Success = false,
                    Errors = new[] { "Refresh token has been used or revoked." }
                };
            }

            // Mark refresh token as used
            storedRefreshToken.IsUsed = true;
            await _refreshTokenRepository.UpdateAsync(storedRefreshToken);

            var jwtToken = await GenerateJwtToken(user);
            var newRefreshToken = GenerateRefreshToken(user.Id, jwtToken.Id);

            await _refreshTokenRepository.AddAsync(newRefreshToken);

            return new AuthResponse
            {
                Success = true,
                Token = new JwtSecurityTokenHandler().WriteToken(jwtToken),
                RefreshToken = newRefreshToken.Token
            };
        }

        public async Task<bool> RevokeTokenAsync(string userId)
        {
            return await RevokeAllRefreshTokensForUser(userId);
        }

        private async Task<JwtSecurityToken> GenerateJwtToken(User user)
        {
            var userRoles = await _userManager.GetRolesAsync(user);

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim("firstName", user.FirstName),
                new Claim("lastName", user.LastName)
            };

            foreach (var userRole in userRoles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, userRole));
            }

            var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT:Secret"]));

            var token = new JwtSecurityToken(
                issuer: _configuration["JWT:ValidIssuer"],
                audience: _configuration["JWT:ValidAudience"],
                expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(_configuration["JWT:TokenValidityInMinutes"])),
                claims: authClaims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );

            return token;
        }

        private RefreshToken GenerateRefreshToken(string userId, string jwtId)
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);

            return new RefreshToken
            {
                UserId = userId,
                Token = Convert.ToBase64String(randomNumber),
                JwtId = jwtId,
                AddedDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(Convert.ToDouble(_configuration["JWT:RefreshTokenValidityInDays"])),
                IsUsed = false,
                IsRevoked = false
            };
        }

        private ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT:Secret"])),
                ValidateLifetime = false
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);
            if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("Invalid token");

            return principal;
        }

        private async Task<bool> RevokeAllRefreshTokensForUser(string userId)
        {
            var refreshTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == userId && !rt.IsRevoked && !rt.IsUsed)
                .ToListAsync();

            foreach (var refreshToken in refreshTokens)
            {
                refreshToken.IsRevoked = true;
                _context.RefreshTokens.Update(refreshToken);
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
