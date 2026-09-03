using System;
using System.Threading.Tasks;

namespace MahjongGame.Systems
{
    /// <summary>Serializes scene operations and revokes presentation authority from superseded routes.</summary>
    public sealed class ClientSceneNavigation
    {
        private readonly string[] _managedScenes;
        private readonly Func<string, bool> _isLoaded;
        private readonly Func<string, Task> _load;
        private readonly Action<string> _activate;
        private readonly Func<string, Task> _unload;
        private Task _tail = Task.CompletedTask;
        private string _target;

        public int Generation { get; private set; }

        public ClientSceneNavigation(string[] managedScenes, Func<string, bool> isLoaded,
            Func<string, Task> load, Action<string> activate, Func<string, Task> unload)
        {
            _managedScenes = managedScenes;
            _isLoaded = isLoaded;
            _load = load;
            _activate = activate;
            _unload = unload;
        }

        public void Invalidate()
        {
            Generation++;
            _target = null;
        }

        public Task NavigateAsync(string target)
        {
            if (_target == target && !_tail.IsFaulted && !_tail.IsCanceled) return _tail;
            int generation = ++Generation;
            _target = target;
            Task previous = _tail;
            var completion = new TaskCompletionSource<bool>();
            _tail = completion.Task;
            _ = RunAsync(previous, generation, target, completion);
            return completion.Task;
        }

        private async Task RunAsync(Task previous, int generation, string target, TaskCompletionSource<bool> completion)
        {
            try
            {
                // An already-started Unity operation cannot be cancelled. Drain it before
                // the newer route inspects loaded scenes or starts a replacement operation.
                try { await previous; }
                catch { /* A failed older route must not prevent returning to the lobby. */ }
                if (generation != Generation) return;
                if (!_isLoaded(target)) await _load(target);
                if (generation != Generation) return;
                _activate(target);
                foreach (string scene in _managedScenes)
                {
                    if (generation != Generation) return;
                    if (scene != target && _isLoaded(scene)) await _unload(scene);
                }
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
            finally
            {
                completion.TrySetResult(true);
            }
        }
    }
}
