var runner = new RegressionRunner();

if (args.Length == 1 && string.Equals(args[0], "talent-actions", StringComparison.OrdinalIgnoreCase))
{
    TalentActionTests.Run(runner);
    return runner.Complete();
}

IdentityConnectionTests.Run(runner);
RoomSessionTests.Run(runner);
NetworkAuthorityBoundaryTests.Run(runner);
SnapshotReconnectTests.Run(runner);
ActionValidationTests.Run(runner);
TalentFoundationTests.Run(runner);
TalentActionTests.Run(runner);
AuthoritativePublicTileTransitionTests.Run(runner);

return runner.Complete();
