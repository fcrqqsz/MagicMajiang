using System.Threading.Tasks;

namespace SuperMajiang.Network.Interfaces
{
    public interface IAuthService
    {
        Task<bool> LoginAsync(string username, string password);
    }
}
