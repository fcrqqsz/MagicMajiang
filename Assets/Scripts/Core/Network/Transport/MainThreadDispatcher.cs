using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace MahjongGame.Core.Network.Transport
{
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        public static MainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("MainThreadDispatcher");
                    _instance = go.AddComponent<MainThreadDispatcher>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();

        public void Enqueue(Action action)
        {
            if (action == null) return;
            _executionQueue.Enqueue(action);
        }

        private void Update()
        {
            while (_executionQueue.TryDequeue(out var action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MainThreadDispatcher] Exception in queued action: {ex}");
                }
            }
        }
    }
}
