namespace AuthApi.DTOs.Auth
{
    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public bool Success { get; set; }
        public IEnumerable<string> Errors { get; set; } = new List<string>();
    }
}
