var runner = new RegressionRunner();

IdentityConnectionTests.Run(runner);
RoomSessionTests.Run(runner);
SnapshotReconnectTests.Run(runner);
ActionValidationTests.Run(runner);

return runner.Complete();
