using System.Threading.Tasks;
using UnityEngine;
using SuperMajiang.Network.Interfaces;

namespace SuperMajiang.Network.Mock
{
    public class MockAuthService : IAuthService
    {
        public async Task<bool> LoginAsync(string username, string password)
        {
            Debug.Log($"[MockAuthService] Attempting to login with {username}...");
            await Task.Delay(1000); // Simulate network delay
            Debug.Log($"[MockAuthService] Login successful.");
            return true;
        }
    }
}
