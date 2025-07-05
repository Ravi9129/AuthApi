using AuthApi.DTOs.Auth;
using AuthApi.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AuthApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest registerRequest)
        {
            var result = await _authService.RegisterAsync(registerRequest);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] AuthRequest authRequest)
        {
            var result = await _authService.LoginAsync(authRequest);
            if (!result.Success)
            {
                return Unauthorized(result);
            }

            return Ok(result);
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest refreshTokenRequest)
        {
            var result = await _authService.RefreshTokenAsync(refreshTokenRequest);
            if (!result.Success)
            {
                return Unauthorized(result);
            }

            return Ok(result);
        }

        [HttpPost("revoke-token/{userId}")]
        public async Task<IActionResult> RevokeToken(string userId)
        {
            var result = await _authService.RevokeTokenAsync(userId);
            if (!result)
            {
                return BadRequest("Failed to revoke token.");
            }

            return Ok("Token revoked successfully.");
        }
    }
}
