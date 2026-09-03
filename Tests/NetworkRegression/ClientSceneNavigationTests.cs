using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MahjongGame.Systems;

internal static class ClientSceneNavigationTests
{
    public static void Run(RegressionRunner runner)
    {
        TestExitDuringGameLoad(runner).GetAwaiter().GetResult();
        TestDuplicateDestination(runner).GetAwaiter().GetResult();
        TestExitDuringLobbyUnload(runner).GetAwaiter().GetResult();
        TestFailedRouteCanRetry(runner).GetAwaiter().GetResult();
    }

    private static async Task TestExitDuringGameLoad(RegressionRunner runner)
    {
        var scenes = new FakeScenes("Lobby");
        var gameLoad = scenes.BlockLoad("Game");
        var navigation = scenes.CreateNavigation();
        Task recovery = navigation.NavigateAsync("Game");
        navigation.Invalidate();
        Task exit = navigation.NavigateAsync("Lobby");
        runner.Check(!exit.IsCompleted && scenes.Loads.Count == 1,
            "Lobby exit serializes behind the existing Game load instead of starting duplicate loads.");
        gameLoad.SetResult(true);
        await Task.WhenAll(recovery, exit);
        runner.Check(scenes.Active == "Lobby" && !scenes.Loaded.Contains("Game")
            && !scenes.Activations.Contains("Game") && !scenes.Unloads.Contains("Lobby"),
            "A superseded recovery load cannot activate Game or unload Lobby; exit removes the stale loaded Game.");
    }

    private static async Task TestDuplicateDestination(RegressionRunner runner)
    {
        var scenes = new FakeScenes("Lobby");
        var gameLoad = scenes.BlockLoad("Game");
        var navigation = scenes.CreateNavigation();
        Task first = navigation.NavigateAsync("Game");
        Task second = navigation.NavigateAsync("Game");
        runner.Check(ReferenceEquals(first, second) && scenes.Loads.Count == 1,
            "Duplicate RoomReady/recovery routes share one scene load task.");
        gameLoad.SetResult(true);
        await first;
        runner.Check(scenes.Active == "Game" && scenes.Unloads.Count == 1,
            "The accepted navigation activates its target and unloads the previous scene once.");
    }

    private static async Task TestExitDuringLobbyUnload(RegressionRunner runner)
    {
        var scenes = new FakeScenes("Lobby");
        var unloading = scenes.BlockUnload("Lobby");
        var navigation = scenes.CreateNavigation();
        Task recovery = navigation.NavigateAsync("Game");
        navigation.Invalidate();
        Task exit = navigation.NavigateAsync("Lobby");
        unloading.SetResult(true);
        await Task.WhenAll(recovery, exit);
        runner.Check(scenes.Active == "Lobby" && scenes.Loaded.SetEquals(new[] { "Lobby" }),
            "If Lobby unloading has already begun, the serialized exit waits and reloads Lobby before removing Game.");
    }

    private sealed class FakeScenes
    {
        public readonly HashSet<string> Loaded = new();
        public readonly List<string> Loads = new();
        public readonly List<string> Unloads = new();
        public readonly List<string> Activations = new();
        private readonly Dictionary<string, TaskCompletionSource<bool>> _loading = new();
        private readonly Dictionary<string, TaskCompletionSource<bool>> _unloading = new();
        public string Active;
        public FakeScenes(string initial) { Loaded.Add(initial); Active = initial; }
        public TaskCompletionSource<bool> BlockLoad(string name) => _loading[name] = new();
        public TaskCompletionSource<bool> BlockUnload(string name) => _unloading[name] = new();
        public ClientSceneNavigation CreateNavigation() => new ClientSceneNavigation(
            new[] { "Login", "Lobby", "Game" }, Loaded.Contains, Load,
            scene => { Active = scene; Activations.Add(scene); }, Unload);
        private async Task Load(string name)
        {
            Loads.Add(name);
            if (_loading.TryGetValue(name, out var pending)) await pending.Task;
            Loaded.Add(name);
        }
        private async Task Unload(string name)
        {
            Unloads.Add(name);
            if (_unloading.TryGetValue(name, out var pending)) await pending.Task;
            Loaded.Remove(name);
        }
    }

    private static async Task TestFailedRouteCanRetry(RegressionRunner runner)
    {
        var loaded = new HashSet<string> { "Game" };
        bool fail = true;
        string active = "Game";
        var navigation = new ClientSceneNavigation(new[] { "Lobby", "Game" }, loaded.Contains,
            scene =>
            {
                if (fail) throw new InvalidOperationException("Scene load failed.");
                loaded.Add(scene);
                return Task.CompletedTask;
            }, scene => active = scene,
            scene => { loaded.Remove(scene); return Task.CompletedTask; });
        Task failed = navigation.NavigateAsync("Lobby");
        try { await failed; }
        catch (InvalidOperationException) { }
        fail = false;
        Task retry = navigation.NavigateAsync("Lobby");
        await retry;
        runner.Check(failed.IsFaulted && !ReferenceEquals(failed, retry) && retry.IsCompletedSuccessfully
            && active == "Lobby" && loaded.SetEquals(new[] { "Lobby" }),
            "A failed lobby load faults its task and permits an explicit retry of the same destination.");
    }
}
