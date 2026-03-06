using System.Threading.Tasks;

namespace SuperMajiang.Network.Interfaces
{
    public interface IMatchmakingService
    {
        Task<string> FindRoomAsync();
    }
}
