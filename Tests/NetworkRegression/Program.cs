var runner = new RegressionRunner();

if (args.Length == 1 && string.Equals(args[0], "talent-services", StringComparison.OrdinalIgnoreCase))
{
    TalentServiceFoundationTests.Run(runner);
    return runner.Complete();
}

if (args.Length == 1 && string.Equals(args[0], "ai-talents", StringComparison.OrdinalIgnoreCase))
{
    AiTalentPolicyTests.Run(runner);
    return runner.Complete();
}

if (args.Length == 1 && string.Equals(args[0], "talent-actions", StringComparison.OrdinalIgnoreCase))
{
    TalentActionTests.Run(runner);
    return runner.Complete();
}

if (args.Length == 1 && string.Equals(args[0], "sideboard", StringComparison.OrdinalIgnoreCase))
{
    SideboardTests.Run(runner);
    return runner.Complete();
}

if (args.Length == 1 && string.Equals(args[0], "talent-command-client", StringComparison.OrdinalIgnoreCase))
{
    TalentCommandClientTests.Run(runner);
    return runner.Complete();
}

if (args.Length == 1 && string.Equals(args[0], "talent-presentation", StringComparison.OrdinalIgnoreCase))
{
    TalentPresentationTests.Run(runner);
    return runner.Complete();
}

if (args.Length == 1 && string.Equals(args[0], "talent-attribution", StringComparison.OrdinalIgnoreCase))
{
    TalentResultAttributionTests.Run(runner);
    return runner.Complete();
}

if (args.Length == 1 && string.Equals(args[0], "compendium", StringComparison.OrdinalIgnoreCase))
{
    CompendiumPresentationTests.Run(runner);
    return runner.Complete();
}

if (args.Length == 1 && string.Equals(args[0], "private-tile-knowledge", StringComparison.OrdinalIgnoreCase))
{
    PrivateTileKnowledgeTrackerTests.Run(runner);
    OpponentKnownTileDisplayPolicyTests.Run(runner);
    return runner.Complete();
}

IdentityConnectionTests.Run(runner);
RoomSessionTests.Run(runner);
NetworkAuthorityBoundaryTests.Run(runner);
SnapshotReconnectTests.Run(runner);
ActionValidationTests.Run(runner);
TalentFoundationTests.Run(runner);
TalentServiceFoundationTests.Run(runner);
TalentResultAttributionTests.Run(runner);
TalentActionTests.Run(runner);
UniversalFillerTalentTests.Run(runner);
TalentCommandClientTests.Run(runner);
TalentPresentationTests.Run(runner);
SideboardTests.Run(runner);
AiTalentPolicyTests.Run(runner);
AuthoritativePublicTileTransitionTests.Run(runner);
CompendiumPresentationTests.Run(runner);
GameTableLayoutPolicyTests.Run(runner);
PrivateTileKnowledgeTrackerTests.Run(runner);
OpponentKnownTileDisplayPolicyTests.Run(runner);

return runner.Complete();
