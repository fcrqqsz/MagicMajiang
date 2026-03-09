using System.Threading.Tasks;

namespace MahjongGame.Core.Network.Interfaces
{
    public interface IMatchmakingService
    {
        Task<string> FindRoomAsync();
    }
}
