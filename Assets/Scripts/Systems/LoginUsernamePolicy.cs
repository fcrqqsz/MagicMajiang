namespace MahjongGame.Systems
{
    /// <summary>Normalizes the temporary player identity supplied by the login screen.</summary>
    public static class LoginUsernamePolicy
    {
        public static string Normalize(string username)
        {
            return string.IsNullOrWhiteSpace(username) ? "Player" : username.Trim();
        }
    }
}
