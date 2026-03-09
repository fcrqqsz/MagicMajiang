using System.Threading.Tasks;
using UnityEngine;
using MahjongGame.Core.Network.Interfaces;

namespace MahjongGame.Core.Network.Mock
{
    public class MockMatchmakingService : IMatchmakingService
    {
        public async Task<string> FindRoomAsync()
        {
            Debug.Log("[MockMatchmakingService] Searching for a room...");
            await Task.Delay(2000); // Simulate matchmaking delay
            string roomId = "Room_" + Random.Range(1000, 9999);
            Debug.Log($"[MockMatchmakingService] Found room: {roomId}");
            return roomId;
        }
    }
}
