# Room 唯一权威与移除隐式本地模式设计

## 决策状态

本设计取代 `2026-08-04-shared-match-host-local-entry-design.md`。不再实现 `MatchSessionHost`、`LocalMatchBootstrap`、`GameLaunchMode.LocalMatch` 或不依赖 Dedicated Server 的正式本地入口。

这里删除的是“客户端进程内创建比赛权威”的隐式本地路径，不删除 `GameMode.Single`。`GameMode.Single` 继续表示只进行一个小局；一名玩家与三名 AI 的体验统一由开启 AI 补位的在线房间提供。

## 背景

当前 `03_Game` 场景根据 `ClientRoomService.HasRoom` 动态选择两条路径：有房间时创建 `RemoteServerProxy`，没有房间时由 `GameManager` 在客户端进程内创建 `GameSession`、`GameServer`、一名 `LocalPlayerClient` 和三名 `SimpleAIClient`。正常大厅没有本地入口，所以第二条路径只会被直接打开游戏场景或外部场景加载触发。

在线房间已经支持 `aiFill=true`，默认 Dedicated Server 配置也开启 AI 补位。一名真人创建房间并准备后，会由三名 `SimpleAIClient` 补齐。它与隐式本地路径提供近似相同的麻将玩法，但在线路径额外经过房间身份、`decisionId` 校验、有序消息、快照和重连。

保留两条权威路径会让局间推进、构筑装配、天赋运行时、异化预算和中场备牌分别接入 `Room` 与 `GameManager`。当前阶段优先探索玩法，也已经确认不需要编辑器直开 `03_Game`，因此选择删除隐式本地权威，把一人和多人体验统一到房间服务器。

## 目标

- `Room` 成为运行比赛、推进小局和持有跨局状态的唯一权威。
- `GameManager` 只装配在线客户端表现，不创建 `GameServer`、AI 或权威 `GameSession`。
- 一名真人可以通过正常的“登录 → 大厅 → 创建房间 → 准备”流程与三名 AI 游玩。
- 保留 `Single`、`EastOnly`、`HalfGame`、`FullGame` 四种局数模式。
- `03_Game` 在没有有效房间绑定时不再静默开始本地比赛，而是记录稳定错误并返回 `00_Persistent`。
- 服务端不可用或连接失败时停留在大厅并显示错误，不回落到另一套本地规则路径。
- 后续 `TalentMatchRuntime`、异化预算和备牌只接入 `Room`/`GameServer` 权威链一次。

## 非目标

- 不提供离线游玩、局域网房主、Listen Server 或客户端内嵌 Dedicated Server。
- 不新增“本地对战”按钮，也不模拟 WebSocket 或 `Room`。
- 不删除 `LocalPlayerClient`；它仍是在线客户端把消息投影到 UI 和 3D 手牌的表现适配器。
- 不删除 `SimpleAIClient`；它继续运行在 Dedicated Server 的 AI 席位和断线托管席位中。
- 不改变麻将规则、计分、AI 策略、房间协议或断线恢复语义。
- 不在本步骤实现天赋垂直切片内容。

## 方案比较与选择

### 方案 A：删除客户端本地权威，统一使用 AI 补位房间

`Room` 保留现有权威职责，`GameManager` 收缩为网络投影。单人体验仍是一个真实在线房间，只是其他三席由 AI 补齐。

优点是实现和维护成本最低，网络动作、天赋、跨局状态和恢复天然只有一套。代价是开发和游玩都需要 Dedicated Server。

### 方案 B：抽取共享 `MatchSessionHost`

本地和 `Room` 都委托给共享 host。它保留离线能力，但仍需维护本地场景启动、直接客户端动作、结果返回和无网络错误处理。

这个方案适合离线是近期产品目标的项目；当前已确认不需要，因此属于额外复杂度。

### 方案 C：保留现状，只隐藏本地入口

它没有即时迁移成本，但后续天赋和备牌仍必须实现两遍。隐藏入口不能消除双权威维护成本。

### 选择

采用方案 A。若未来验证出明确的离线产品需求，再以独立里程碑重新设计；不为尚未确认的需求保留当前隐式分支。

## 最终架构

```text
正常客户端
  └─ 登录 / 大厅
       └─ 创建或加入房间
            └─ WebSocket + ClientRoomService
                 └─ RemoteServerProxy
                      └─ LocalPlayerClient
                           └─ UI / 3D 表现

Dedicated Server
  └─ Room（唯一比赛权威）
       ├─ GameSession
       ├─ NetworkDecisionTracker
       ├─ 每小局 GameServer + WallService
       ├─ StableSeatController / SimpleAIClient
       ├─ TalentMatchRuntime（后续计划加入）
       └─ SideboardDecisionTracker（后续计划加入）
```

`GameManager.Session` 只保留为客户端展示投影。`RemoteServerProxy` 和恢复快照可以更新它，但任何客户端代码不得通过它推进权威局数、应用权威分数或决定房间状态。

## 组件职责

### `Room`

`Room` 继续并唯一负责：

- 锁定四席可信构筑，并在 AI 补位时使用标准可信构筑；
- 创建整场唯一的 `GameSession` 和 `NetworkDecisionTracker`；
- 每小局创建新的 `WallService` 与 `GameServer`；
- 组装 `StableSeatController` 和 `SimpleAIClient`；
- 在 `OnRoundFinished` 中唯一执行 `Session.AdvanceRound()`；
- 管理下一局 ready、总结算、断线托管和重连；
- 后续持有整场唯一的 `TalentMatchRuntime` 和中场备牌状态。

本次前置重构不强行从 `Room` 抽出 host。只有出现第二个确定的权威运行环境时，才重新评估共享编排抽象。

### `GameManager`

`GameManager.Start()` 只接受一个有效前置条件：`NetworkManager.Instance.RoomService.HasRoom == true`。满足时创建本席 `LocalPlayerClient` 与 `RemoteServerProxy`，应用已有投影或恢复快照。

不满足时：

1. 记录 `MissingNetworkRoomForGameScene`；
2. 不创建 `GameSession` 权威、`GameServer`、AI 或本地牌山；
3. 清理可能已经创建的客户端表现代理；
4. 通过持久场景协调器返回 `00_Persistent`；若协调器不存在，直接加载 `00_Persistent`；
5. 不重新加载 `03_Game`，避免循环。

`GameManager` 删除以下职责：

- `StartGameWithConfig`、`StartSession` 和本地分支的 `StartNextRound`；
- `BuildTalentConfigs` 与 `starting_capital` ID 特判；
- 本地 `GameServer`、四席构筑、AI 和局结束订阅；
- `Session.AdvanceRound()` 和向本地 clients 广播 `OnSessionEnd`；
- 本地调试手牌和 AI 作弊字段。

`StartNextRound()` 只向 `ClientRoomService` 发送 `ReadyPhase.NextRound`。本席相对座次始终来自已绑定房间的 `SeatIndex`，不再假设本地玩家固定为 0。

### `GameHUDController` 与结果面板

HUD 的牌山剩余数只来自 `IPlayerClient.OnWallCountChanged` 或恢复快照，不再每帧读取 `DeckManager.RemainingCount`。`DeckManager` 可以继续提供场景牌面资源兼容，但不再是游戏场景内的权威牌山。

结果面板的“下一局”只发送房间 ready；“查看总结算”只展示客户端已收到的权威 session 投影；“返回主菜单”先离开房间再加载大厅。缺少 `NetworkManager` 的异常回退加载 `00_Persistent`，不得加载 `03_Game`。

### 大厅与一人 AI 房间

本步骤沿用现有“创建新房间”流程，不新增本地按钮。Dedicated Server 的 `ServerBootstrapOptions.DefaultAiFill` 保持 `true`，`RoomReadyPolicy.CanMarkMatchReady(true, 1)` 继续允许一名真人准备开局。

房间页继续明确显示 AI 补位状态。创建者仍需点击准备，避免误触创建后立即开局。若服务端使用 `--aiFill=false` 启动，则必须等四名真人，客户端不伪造 AI 补位。

“一人游玩”与 `GameMode.Single` 是两个维度：前者描述真人数量，后者描述只打一小局。所有四种 GameMode 都允许一真人加三 AI。

## 错误处理

- `MissingNetworkRoomForGameScene`：安全返回 Persistent，不创建本地比赛。
- `RoomClosed`：沿用现有房间关闭路由，卸载游戏场景并返回大厅。
- 创建房间连接失败：停留大厅并展示现有 `RoomError`/恢复信息，不加载游戏场景。
- `aiFill=false` 且真人不足四人：ready 被服务端拒绝，保持房间等待状态。
- 总结算离开：保留结果席位快照直到结果 UI 读取完毕，然后由 `LeaveRoom` 清理房间票据和客户端投影。
- 直接打开 `03_Game`：Editor 与正式构建行为相同，不设编辑器后门。

## 测试策略

### 纯 C# 回归

- 游戏场景入口策略只在 NetworkManager、RoomService 和有效 room binding 同时存在时允许初始化。
- 缺失任一条件时入口策略返回 Persistent，不存在 Local fallback。
- `RoomReadyPolicy.CanMarkMatchReady(true, 1)` 保持为真，`aiFill=false` 时少于四名真人保持为假。
- Single/EastOnly/HalfGame/FullGame 的总局数保持 1/4/8/16。
- 客户端 session 投影只应用服务端消息或快照；局结束只有 `Room` 权威执行一次 `AdvanceRound`。
- 源码守卫确认 `GameManager.cs` 不再包含 `new GameServer`、`new SimpleAIClient`、`Session.AdvanceRound`、`starting_capital` 或 `DeckManager.Instance` 权威牌山引用。
- 现有动作、房间、快照和重连回归全部继续通过。

### Unity 手工验证

1. 启动 Dedicated Server 默认配置和一个客户端；创建房间、选择任意 GameMode、单人准备后由三 AI 补位进入游戏。
2. 完成 Single，确认只结算一次并可返回大厅。
3. 完成至少两小局 HalfGame，确认下一局由房间 ready 驱动、累计分数不重复推进。
4. 在局中断开客户端并重连，确认快照恢复和 AI 托管不退化。
5. 不启动 Dedicated Server，创建房间失败后停留大厅，不进入本地比赛。
6. Editor 直接运行 `03_Game`，确认记录 `MissingNetworkRoomForGameScene` 并返回 Persistent。
7. 正式构建以调试方式直接加载 `03_Game`，确认与 Editor 行为一致。
8. 总结算返回大厅后重新创建房间，确认旧票据和结果投影已清理。

## 与天赋垂直切片的关系

实施顺序调整为：

1. 删除隐式本地权威并统一为 `Room` 唯一权威；
2. 天赋运行时、异化预算和现有六天赋迁移；
3. 主动天赋、三项锚点天赋和中场备牌；
4. UI、AI、反馈与玩法测试。

后续计划必须遵守：

- `TalentMatchRuntime` 由 `Room` 持有整场唯一实例；`GameManager` 不持有；
- `Room` 在每局创建 `GameServer` 时显式传入同一个 runtime；
- `StartingCapital`、`DrawReward`、`Peek` 和跨局状态只接入 `Room` 权威生命周期一次；
- `SideboardDecisionTracker` 由 `Room` 持有，客户端只提交选择并展示权威投影；
- AI 构筑和主动技能策略只在 Dedicated Server 的房间席位运行；
- 不为未来离线模式预留第二套运行时、启动模式或条件分支。

## 验收标准

- 正常客户端不存在可进入的客户端内本地比赛路径。
- 一名真人在默认 AI 补位房间中可以完成 Single 和多局比赛。
- `GameManager` 不创建 `GameServer`、AI、权威构筑或权威 `GameSession`，也不推进小局。
- `Room` 是唯一调用 `Session.AdvanceRound()` 的比赛编排组件。
- 无房间直接进入 `03_Game` 时安全返回 Persistent，Editor 与构建行为一致。
- HUD 不再把 `DeckManager` 当作牌山权威，结果返回不再重新加载 Game 场景。
- NetworkRegression 与 Unity 编译通过，联机、重连、AI 托管和快照隐私无退化。
- 三份天赋实施计划明确把跨局运行时和备牌所有权放在 `Room`，不再引用 `MatchSessionHost`、`LocalMatchBootstrap` 或 `GameManager` 本地权威。
