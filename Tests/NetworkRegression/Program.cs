var runner = new RegressionRunner();

IdentityConnectionTests.Run(runner);
RoomSessionTests.Run(runner);
NetworkAuthorityBoundaryTests.Run(runner);
SnapshotReconnectTests.Run(runner);
ActionValidationTests.Run(runner);
TalentFoundationTests.Run(runner);
AuthoritativePublicTileTransitionTests.Run(runner);

return runner.Complete();
