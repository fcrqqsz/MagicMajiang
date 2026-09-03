using MahjongGame.Core;

internal static class BattleMenuTests
{
    public static void Run(RegressionRunner runner)
    {
        var menu = new BattleMenuState();
        menu.Escape();
        runner.Check(menu.Page == BattleMenuPage.Home, "Escape from closed menu must open its homepage.");
        menu.ShowSettings();
        menu.Escape();
        runner.Check(menu.Page == BattleMenuPage.Home, "Escape from settings must return to homepage without leaving battle.");
        runner.Check(!menu.RequestExit(false) && menu.Page == BattleMenuPage.ConfirmExit,
            "Leaving an active match must require confirmation.");
        menu.Escape();
        runner.Check(menu.Page == BattleMenuPage.Home, "Escape must cancel exit confirmation without leaving battle.");
        menu.Escape();
        runner.Check(menu.Page == BattleMenuPage.Closed, "Escape from homepage must release the menu.");
        runner.Check(!menu.ConfirmExit(), "Hidden confirmation cannot initiate exit.");

        menu.Open();
        menu.RequestExit(false);
        runner.Check(menu.ConfirmExit(), "First confirmed exit must start navigation.");
        runner.Check(!menu.ConfirmExit() && !menu.RequestExit(true), "Repeated exit requests must not start duplicate navigation.");
        menu.Escape();
        menu.Close();
        menu.OnAuthoritativeBoundary();
        menu.Open();
        menu.ShowSettings();
        runner.Check(menu.Page == BattleMenuPage.Leaving,
            "Escape, boundaries and menu entries cannot revoke an in-progress exit.");

        var completed = new BattleMenuState();
        completed.Open();
        runner.Check(completed.RequestExit(true) && completed.Page == BattleMenuPage.Leaving,
            "Authoritative session completion permits immediate return without another leave confirmation.");
        var boundary = new BattleMenuState();
        boundary.Open();
        boundary.ShowSettings();
        boundary.OnAuthoritativeBoundary();
        runner.Check(boundary.Page == BattleMenuPage.Closed, "A new authoritative phase closes stale settings.");

        var gate = new BattleMenuInputGate();
        runner.Check(gate.CanInteract(true, 10) && !gate.CanInteract(false, 10), "Closed menu must preserve decision authority.");
        gate.SetBlocked(true, 10);
        runner.Check(!gate.CanInteract(true, 11), "Visible menu must block a still-valid discard decision.");
        gate.SetBlocked(false, 12);
        runner.Check(!gate.CanInteract(true, 12), "Menu closing click cannot pass through to a 3D tile during the same frame.");
        runner.Check(gate.CanInteract(true, 13), "After menu closes the still-valid decision must become usable.");
        runner.Check(!gate.CanInteract(false, 13), "Closing menu must not revive a decision that timed out underneath it.");
        gate.SetBlocked(false, 14);
        runner.Check(gate.CanInteract(true, 14), "Rendering an already closed menu must not reacquire its input block.");
    }
}
