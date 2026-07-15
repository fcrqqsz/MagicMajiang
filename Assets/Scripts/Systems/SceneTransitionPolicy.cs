namespace MahjongGame.Systems
{
    /// <summary>Pure predicates used by additive scene transitions.</summary>
    public static class SceneTransitionPolicy
    {
        public static bool ShouldUnloadGameScene(bool gameSceneIsLoaded) => gameSceneIsLoaded;
    }
}
