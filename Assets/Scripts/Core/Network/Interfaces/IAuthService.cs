using System.Threading.Tasks;

namespace MahjongGame.Core.Network.Interfaces
{
    public interface IAuthService
    {
        Task<bool> LoginAsync(string username, string password);
    }
}
