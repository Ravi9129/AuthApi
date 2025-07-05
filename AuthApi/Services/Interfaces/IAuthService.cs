using AuthApi.DTOs.Auth;

namespace AuthApi.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse> RegisterAsync(RegisterRequest registerRequest);
        Task<AuthResponse> LoginAsync(AuthRequest authRequest);
        Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest refreshTokenRequest);
        Task<bool> RevokeTokenAsync(string userId);
    }
}
