# 联机小局切换架构与修复计划

## 背景

当前联机 Phase 1.6 已经能完成单小局的服务端权威流程，但小局结束后客户端无法稳定推进到下一小局。根因在于：

- 服务端 `GameSession` 在 `OnRoundFinished` 中推进，但客户端没有收到等价的会话推进信息。
- 客户端结算面板依赖本地 `GameSession` 判断按钮行为；联机消息只触发 `ShowWin`/`ShowDraw`，没有先调用 `SetSessionInfo`。
- 胜负与流局消息没有携带本局结束后的累计分数，客户端无法显示正确多局状态。

## 新状态边界

### 服务端权威

服务端仍然是唯一牌局权威，负责：

- 执行摸牌、出牌、响应仲裁、胡牌校验、算番与计分。
- 在小局结束时通过 `PlayerWin` 或 `DrawGame` 下发本局结果。
- 在 `OnRoundFinished` 中推进服务端 `GameSession`，并在未结束时等待客户端下一次 `Ready`。

### 客户端镜像

客户端只维护一个用于 UI 和 Ready 流程的 `GameSession` 镜像：

- 收到 `PlayerWin`/`DrawGame` 后，先同步累计分数。
- 按服务端给出的 `completedRounds` 将本地 `GameSession` 推进到同样的小局完成数。
- 将同步后的会话传给 `ResultPanelController.SetSessionInfo`，让按钮根据 `IsSessionOver()` 自动选择“下一局”或“查看总结算”。

## 消息扩展

`PlayerWinMessage` 和 `DrawGameMessage` 增加以下字段：

- `scores`: 本局结算后的四家累计分数。
- `completedRounds`: 本局结束后应视为已完成的小局数。

`completedRounds` 由服务端在调用 `Session.AdvanceRound()` 前计算为 `TotalRoundsPlayed + 1`。客户端收到后用 `while (local.TotalRoundsPlayed < completedRounds) local.AdvanceRound()` 追平。

## 小局结束流程

1. `GameServer` 判定胡牌或流局。
2. 服务端先应用本局分数。
3. `RemotePlayerClient` 下发 `PlayerWin`/`DrawGame`，携带 `scores` 和 `completedRounds`。
4. 客户端 `RemoteServerProxy` 先同步本地 `GameSession`，再转发给 `LocalPlayerClient` 显示结算。
5. 结算面板按钮显示：
   - 未完成整场：`下一局`
   - 已完成整场：`查看总结算`
6. 客户端点击“下一局”后调用 `StartNextRound()`，复用 WebSocket 并发送 `Ready`。
7. 服务端收到 `Ready` 后装配 AI 并启动下一小局。

## 后续改进

- Phase 2 房间系统中，应把 `scores/completedRounds` 抽成统一的 `RoundResultMessage`，避免胜负和流局消息重复字段。
- `seq` 当前仍未用于去重与补包；断线重连前应增加 per-connection 消息缓存。
- 服务端网络分支后续应直接注入纯 C# `WallService`，避免 Headless 场景依赖 `DeckManager.Instance`。
