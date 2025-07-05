using AuthApi.DTOs.User;

namespace AuthApi.Services.Interfaces
{
    public interface IUserService
    {
        Task<IEnumerable<UserDto>> GetAllUsersAsync();
        Task<UserDto?> GetUserByIdAsync(string id);
        Task<bool> UpdateUserAsync(string id, UserDto userDto);
        Task<bool> DeleteUserAsync(string id);
    }
}
