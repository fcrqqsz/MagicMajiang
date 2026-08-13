using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal static class UnityAssetMetadataTests
{
    private static readonly string[] Phase3UiAssets =
    {
        "Assets/UI/ActionPanel.uxml",
        "Assets/UI/ActionPanelStyles.uss",
        "Assets/UI/DeckEditorStyles.uss",
        "Assets/UI/DeckEditorView.uxml",
        "Assets/UI/GameHUD/GameHUD.uxml",
        "Assets/UI/GameHUD/GameHUDStyles.uss",
        "Assets/UI/MainLobby.uxml",
        "Assets/UI/MainLobbyStyles.uss",
        "Assets/UI/ResultPanel.uxml",
        "Assets/UI/ResultPanelStyles.uss",
        "Assets/UI/SideboardPanel.uxml",
        "Assets/UI/SideboardPanelStyles.uss",
        "Assets/UI/TalentChipTemplate.uxml",
        "Assets/UI/TalentChipTemplate.uss"
    };

    private static readonly Regex GuidLine = new Regex(
        @"(?m)^guid: (?<guid>[^\r\n]+)\r?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex UnityHexGuid = new Regex(
        @"^[0-9a-f]{32}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex TuanjieBase64Guid = new Regex(
        @"^[A-Za-z0-9+/]{55}=$",
        RegexOptions.CultureInvariant);

    public static void Run(RegressionRunner runner)
    {
        string root = FindRepositoryRoot();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string asset in Phase3UiAssets)
        {
            string metaPath = Path.Combine(root, asset.Replace('/', Path.DirectorySeparatorChar) + ".meta");
            runner.Check(File.Exists(metaPath), $"Phase 3 UI asset must retain its meta file: {asset}.meta");
            if (!File.Exists(metaPath)) continue;

            string meta = File.ReadAllText(metaPath);
            Match match = GuidLine.Match(meta);
            runner.Check(match.Success, $"Phase 3 UI meta must declare a guid: {asset}.meta");
            if (!match.Success) continue;

            string guid = match.Groups["guid"].Value;
            runner.Check(guid.Length != 55,
                $"Phase 3 UI meta guid must not use the known-invalid 55-character truncated Base64 form: {asset}.meta");
            runner.Check(UnityHexGuid.IsMatch(guid) || TuanjieBase64Guid.IsMatch(guid),
                $"Phase 3 UI meta guid must be 32 lowercase hex or 56-character Tuanjie Base64: {asset}.meta");

            if (seen.TryGetValue(guid, out string priorAsset))
            {
                runner.Check(false,
                    $"Phase 3 UI meta guids must be unique: {asset}.meta duplicates {priorAsset}.meta");
            }
            else
            {
                seen.Add(guid, asset);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ProjectSettings", "ProjectVersion.txt")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Repository root not found.");
        return directory.FullName;
    }
}
