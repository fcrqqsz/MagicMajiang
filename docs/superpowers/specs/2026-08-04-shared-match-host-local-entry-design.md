# 共享比赛宿主与正式本地入口设计

## 背景

当前 `03_Game` 场景同时包含两条启动路径：有活动房间时作为联机客户端，没有活动房间时由 `GameManager` 在 Unity 客户端进程内创建 `GameSession`、`GameServer`、一名 `LocalPlayerClient` 和三名 `SimpleAIClient`。核心麻将规则能够复用，但局间推进、构筑装配、天赋开场效果和结算分别存在于 `Room` 与 `GameManager`。

正常客户端大厅只有“创建新房间”和“加入房间”，没有本地入口。因此本地能力目前只能通过编辑器直接打开 `03_Game` 或外部代码加载该场景触发；它是隐式开发后门，不是完整产品功能。随着跨局天赋运行时、中场备牌和异化预算加入，继续让 `Room` 与 `GameManager` 分别承担比赛权威会让每项功能实现两遍。

本设计在天赋垂直切片实施前增加一个基础步骤：抽取共享的纯 C# 比赛宿主，并让正常游戏客户端可以从大厅显式进入不依赖 Dedicated Server 的本地对战。

## 目标

- 玩家通过正常客户端的“登录 → 大厅 → 本地对战”流程进入一人加三 AI 的比赛。
- 本地对战不需要启动 Dedicated Server，不创建房间，不依赖可用 WebSocket 连接。
- 本地和联机服务端共享同一个比赛权威宿主、局间推进、`GameSession`、每局 `GameServer`、决策谱系和后续 `TalentMatchRuntime`。
- `GameManager` 不再作为第二个比赛服务器，只负责游戏场景表现装配和客户端投影。
- 联机客户端仍只消费 Dedicated Server 的权威消息与快照，不在本机创建第二个权威宿主。
- 场景启动方式显式、一次性且可测试，不再通过运行中随时变化的 `RoomService.HasRoom` 猜测模式。
- Unity Editor 和正式构建都只能通过一次性显式请求进入 `03_Game`；不保留编辑器直开游戏场景的特殊模式。
- 修复本地对战结束后无法返回正常客户端流程的问题。

## 非目标

- 不让本地模式支持真人局域网联机、房主迁移或断线重连。
- 不在本地模式内模拟 WebSocket、endpoint、连接代次或 `SeatMessageStream`。
- 不为本地模式建立第二套协议消息或复制 `Room`。
- 不改变麻将规则、AI 决策质量、现有计分公式或天赋数值。
- 本步骤只建立共享宿主和入口；`TalentMatchRuntime`、三项新天赋、异化预设 UI 和中场备牌仍由后续天赋计划实现。

## 方案比较

### 方案 A：抽取共享 `MatchSessionHost`，本地使用进程内客户端适配器

`Room` 保留网络职责，把比赛生命周期委托给纯 C# `MatchSessionHost`。本地入口创建同一个 host，但提供 `LocalPlayerClient + 3 SimpleAIClient`。本地 UI 继续使用直接的 `IPlayerClient` 回调，联机客户端继续使用 `RemoteServerProxy`。

优点是权威比赛状态只有一套，保留快速本地调试，同时不把网络复杂度带入本地模式。迁移范围集中在比赛编排和启动边界。

### 方案 B：本地启动完整 `Room`，通过内存传输模拟网络客户端

本地客户端创建内存 endpoint、房间和消息流，再让表现层走与远程完全相同的协议。

它的客户端路径一致性最高，但需要为 WebSocket、连接身份、消息序列和重连语义建立内存替身。首期玩法探索不需要这些能力，复杂度明显高于收益。

### 方案 C：保留 `GameManager` 本地权威，只增加大厅按钮

这是最小 UI 改动，但跨局天赋、备牌、异化预算、AI 构筑和结算仍需在 `Room` 与 `GameManager` 分别实现，已经存在的 `starting_capital` 特判会继续扩散。

### 选择

采用方案 A。它保留本地体验和开发速度，同时消除真正造成架构恶化的双重比赛权威；不引入本地模式不需要的网络模拟层。

## 最终架构

```text
正常客户端
  ├─ 大厅“本地对战”
  │    └─ LocalMatchBootstrap ─────┐
  │                                │
  └─ 大厅“创建/加入房间”           │
       └─ Dedicated Server Room ───┤
                                   ▼
                         MatchSessionHost
                           ├─ GameSession
                           ├─ NetworkDecisionTracker
                           ├─ 每小局 GameServer
                           ├─ 四席可信构筑
                           └─ TalentMatchRuntime（后续计划加入）

GameManager
  ├─ Local：绑定 LocalPlayerClient 到 LocalMatchBootstrap
  └─ Network：绑定 LocalPlayerClient 到 RemoteServerProxy
```

`MatchSessionHost` 是比赛规则层的唯一权威编排者。`Room` 是网络外壳，`LocalMatchBootstrap` 是进程内装配器，`GameManager` 是表现入口。三者不互相复制局间规则。

## 组件职责

### `GameLaunchRequest` 与 `GameLaunchContext`

增加显式启动模型：

- `GameLaunchMode.NetworkRoom`
- `GameLaunchMode.LocalMatch`

本地请求在本步骤包含对战模式和经过可信重建的本家构筑；网络请求只记录这是一次房间/恢复启动，房间详情继续来自 `ClientRoomService`。请求是一次性的，`GameManager.Start()` 必须消费后清空，避免退出后再次加载场景沿用旧模式。后续异化计划再把 `AlienationPreset` 加入本地请求和 host 配置，默认使用玩家设置中的 `Standard`。

`RoomReady` 和联机恢复在加载 `03_Game` 前写入 `NetworkRoom` 请求。大厅本地按钮在加载前写入 `LocalMatch` 请求。`GameManager` 在整个场景生命周期内缓存已消费的模式，`IsNetworkClient` 不再动态读取 `HasRoom`。

Unity Editor 和正式客户端构建的缺失请求处理完全相同：记录 `MissingGameLaunchRequest`，不创建本地服务器，返回 `00_Persistent` 重新进入登录流程。`useDebugHand` 等 Inspector 调试配置仍由正常大厅的本地入口加载 `03_Game` 后传给本地 host，不需要第二种启动模式。

### `SelectedLoadoutProvider`

当前选中 Profile 构筑的读取逻辑在 `ClientRoomService.TryBuildSelectedLoadout` 内部，直接复用它会让本地入口依赖网络服务。将“索引检查、默认标准牌库、空天赋兼容和本地消息构建”抽为纯 C# `SelectedLoadoutProvider`：

- 大厅本地入口请求它返回规范化的 `DeckConfig`、`TalentSlotConfig` 和可信构筑；
- `ClientRoomService` 请求它生成相同内容的 `PlayerLoadoutMessage`，再发送到服务器；
- provider 不连接 WebSocket、不读取 RoomState、不显示 UI；调用方把稳定错误码转换为界面文案；
- 后续 v2 构筑和异化预算只修改 provider/codec 边界一次，本地和联机创建不会再次分叉。

### `MatchSessionHost`

纯 C#、非单例，不依赖 `MonoBehaviour`、场景、HUD、`NetworkManager`、WebSocket 或 `GameManager.Instance`。它负责：

- 持有一场比赛唯一的 `GameSession`；
- 持有四席锁定的 `TrustedPlayerLoadout`；后续异化计划为 host 增加统一档位；
- 持有跨小局连续递增的 `NetworkDecisionTracker`；
- 每小局创建新的 `WallService` 与 `GameServer`，停止并释放上一局实例；
- 让调用方提供四个本局 `IPlayerClient`，再统一调用 `GameServer.StartGame`；
- 订阅局结束事件，只在一个地方执行 `Session.AdvanceRound()`；
- 发布 `RoundStarted`、`RoundFinished`、`SessionFinished` 等类型化生命周期事件；
- 后续持有唯一 `TalentMatchRuntime`，并负责中场备牌的生效集合和局间时序。

它不负责：

- 网络 ready、连接、断线、endpoint、消息序列和身份；
- UI 面板、动画、牌面 GameObject 或场景跳转；
- 选择本地 Profile、读取 PlayerPrefs 或创建网络消息。

Host 接受调用方提供的 `GameServerOptions`，但始终自行注入自己的 `NetworkDecisionTracker`。本地与联机服务端因此使用同一 decisionId 规则；本地主动天赋不再拥有绕过决策校验的特殊入口。

### `Room` 网络外壳

`Room` 继续负责：

- 四席网络身份、ready、AI 托管和重连；
- `SeatMessageStream`、私有/公共消息和快照隐私；
- 把服务端网络动作提交给当前 `GameServer`；
- 将 host 生命周期映射为 `RoomState`；
- 在断线、超时和中场选择时调用 host 的权威接口。

`Room` 不再直接创建 `GameSession`、推进局数、执行开场天赋或重建每局 `GameServer`。它在构筑锁定后创建一次 `MatchSessionHost`，并为每局提供 `StableSeatController`/`SimpleAIClient` 集合。

### `LocalMatchBootstrap`

它是本地模式的薄装配器，不是另一套 host。职责为：

- 接收已经验证的 `LocalMatch` 请求；
- 为本家创建 `LocalPlayerClient`，为其余三席创建 `SimpleAIClient`；
- 在 AI 天赋预设完成前为三席 AI 使用标准牌山和空天赋可信构筑；后续异化/AI 计划再按所选档位生成预设构筑；
- 创建一个 `MatchSessionHost` 并把生命周期投影给 HUD/结算面板；
- 本地玩家点击下一局时调用 host，而不是自行推进 `GameSession`；
- 退出或场景销毁时停止并释放 host。

本地 host 使用纯 C# `WallService`。`DeckManager` 只保留牌面资源/兼容表现职责，不再作为本地权威牌山；牌山剩余数通过 `IPlayerClient.OnWallCountChanged` 投影给 HUD，与联机路径保持一致。

### `GameManager`

`GameManager` 消费启动请求并选择客户端适配器：

- `LocalMatch`：创建 `LocalMatchBootstrap`；
- `NetworkRoom`：创建 `RemoteServerProxy` 并应用 `ClientGameState` 快照。

它保留场景对象引用、对手视图映射、恢复表现和结果面板绑定，但移除：

- 本地 `GameSession` 的权威创建与推进；
- 本地 `GameServer` 的创建和停止；
- `starting_capital` 等天赋 ID 特判；
- 本地四席构筑和 AI 的权威装配；
- 通过实时 `HasRoom` 切换模式。

为兼容现有 UI，`GameManager.Session` 可以暂时作为只读投影：本地返回 host 的 session，联机返回客户端投影 session。任何调用方不得通过它推进局数或修改权威分数。

## 正常客户端本地入口

大厅 Home 页在“创建新房间”旁增加独立的“本地对战”按钮，避免把本地与联网概念混成同一按钮。进入流程：

1. 玩家正常启动 `00_Persistent`、登录并进入 `02_MainLobby`；
2. 选择牌库和对战模式；异化预设 UI 完成后还会使用当前档位；
3. 点击“本地对战”；
4. `SelectedLoadoutProvider` 读取当前 Profile，客户端使用 `PlayerLoadoutCodec` 重建并验证本家构筑；
5. 验证失败时停留大厅，显示稳定错误码对应的可理解提示；
6. 验证成功后写入一次性 `LocalMatch` 请求；
7. 使用现有 LoadingScreen 加载 `03_Game` 并卸载大厅；
8. `GameManager` 消费请求，创建本地 bootstrap 和共享 host；
9. host 启动一名本地玩家加三名 AI 的比赛。

该流程不调用 `ClientRoomService.CreateRoom/JoinRoom`，也不要求 `ws://127.0.0.1:9876/game` 可连接。`NetworkManager` 仍可作为正常客户端的持久场景与 LoadingScreen 协调器存在，但本地开始方法不得触发 RoomService 连接。

Home 页只在客户端没有活动房间时提供本地按钮。若存在未完成的重连提示，玩家点击本地对战等价于放弃该恢复：清理本地重连票据、停止重试并清空客户端房间投影，然后再建立 `LocalMatch` 请求；不会在后台继续重连并把客户端切回网络比赛。已经绑定活动房间时必须先走现有离开房间流程，不能直接覆盖房间身份。

本步骤先使用现有 `PlayerLoadoutCodec` 校验牌数、天赋 ID、重复和槽位。本地入口建立后，后续异化计划将它升级为与创建房间相同的预算校验；构筑超出当前档位时停留大厅，玩家可返回编辑器调整或切换档位。

## 局间和结束流程

本地与联机服务端都由 host 决定小局是否结束、是否进入下一局或整场结束。

- 联机：`Room` 收到 host 的 `RoundFinished` 后设置 ready 状态；全部真人 ready 后请求 host 启动下一局。
- 本地：结算面板“下一局”直接请求本地 bootstrap 调用 host；不修改 session。
- 整场结束：host 只发布一次 `SessionFinished`，调用方负责显示总结算。
- 返回大厅：正常客户端无论本地或联机都通过持久场景协调器加载 `02_MainLobby`；只有网络模式调用 `RoomService.LeaveRoom()`。
- `03_Game` 没有启动请求时直接加载 `00_Persistent`，不进入结算或退出流程，也不重新加载 `03_Game` 形成循环。

## 与天赋垂直切片的关系

本设计是 `2026-08-04-talent-foundation-and-alienation.md` 的前置计划。实施顺序固定为：

1. 共享比赛宿主与正式本地入口；
2. 天赋运行时、异化预算和现有六天赋迁移；
3. 主动天赋、三项锚点天赋和中场备牌；
4. UI、AI、反馈与玩法测试。

后续天赋计划必须调整为：

- `TalentMatchRuntime` 由 `MatchSessionHost` 持有，不由 `Room` 和 `GameManager` 各持有一份；
- `StartingCapital`、`DrawReward`、`Peek` 和跨局状态只接入 host 生命周期一次；
- Sideboard 的生效集合、预算和一次性触发由 host 维护；`Room` 只传输选择与断线锁定命令，本地 bootstrap 直接调用同一接口；
- AI 构筑和主动天赋策略同时适用于本地与 Dedicated Server host。

## 错误处理

- `MissingGameLaunchRequest`：正式构建不开始比赛，清理残留上下文并返回 Persistent。
- `LaunchRequestAlreadyPending`：重复点击不覆盖首个请求；按钮进入禁用状态直到场景切换成功或失败。
- `InvalidLocalLoadout`：停留大厅，不创建 host；后续异化计划增加 `AlienationLimitExceeded`。
- `LocalHostStartFailed`：停止部分创建的 `GameServer`，清理一次性请求，恢复大厅并显示错误。
- `NetworkLaunchWithoutRoom`：不回落本地，进入现有重连/终止恢复流程。
- `LocalLaunchWhileRoomBound`：停留房间页，要求先离开当前房间，不覆盖房间和重连票据。
- 场景加载失败：恢复本地按钮，清理 pending request，避免下一次误消费。
- Host 重复开始同一小局、结束后再开始或席位不足四个：抛出明确的 `InvalidOperationException`，测试必须覆盖。

## 测试策略

### 纯 C# 回归

- `GameLaunchContext` 请求只能消费一次，失败/退出会清理，Network 和 Local 不互相污染。
- Editor 和正式构建缺少请求时都不创建 host，并返回 Persistent。
- `MatchSessionHost` 对 Single/EastOnly/HalfGame/FullGame 使用正确总局数，并且每局只推进一次。
- 每局替换 `GameServer` 但保留同一 `GameSession`、`NetworkDecisionTracker`，后续计划再验证保留同一 `TalentMatchRuntime`。
- 本地与 Room 适配器使用同一构筑输入时，发牌/动作/结算和 round/session 生命周期顺序一致。
- Host stop/dispose 可重复调用且不会留下 `OnRoundFinished` 订阅。
- 本地模式不调用 `ClientRoomService.CreateRoom`、`JoinRoom` 或 WebSocket `Connect`。
- 放弃未完成恢复后进入本地模式时，重连 retry、票据和旧客户端投影均被清理，不会在本地局中恢复旧房间。

### Unity 手工验证

1. 不启动 Dedicated Server，正常启动客户端、登录、进入大厅并点击“本地对战”，成功进入一人加三 AI。
2. 本地模式使用当前选中牌库和 GameMode；返回大厅后选择仍保留。
3. 本地完整完成 Single 和至少一个多局模式，下一局和总结算只触发一次。
4. 本地总结算返回大厅，不创建/离开房间，不重新加载 Game 场景。
5. 同一客户端随后可以创建联机房间，联机模式不会创建本地 host。
6. 联机 RoomReady、重连到局中和重连到结算继续进入 Network 模式。
7. Editor 直接运行 `03_Game` 且没有请求时安全返回 Persistent，不开始比赛；通过大厅本地入口进入后仍可使用 `useDebugHand`。
8. 正式构建通过调试方式直接加载 `03_Game` 且没有请求时同样安全返回，不静默开始本地比赛。

## 验收标准

- 大厅存在清晰、可用的“本地对战”入口。
- Dedicated Server 和本地 WebSocket 均未启动时，本地对战仍可完整进行。
- `GameManager` 不再创建 `GameServer`、推进 `GameSession` 或按天赋 ID 执行效果。
- `Room` 与本地模式通过同一 `MatchSessionHost` 创建每局 `GameServer` 和推进比赛。
- 本地、联机、联机恢复三种启动由一次性显式请求决定，不动态猜测。
- 本地结束可以正常返回大厅，之后仍可创建联机房间。
- NetworkRegression 新增 host/launch 回归并全部通过；现有联机、重连和动作校验回归无退化。
- 后续天赋计划明确把 `TalentMatchRuntime` 所有权放在 host，而不是复制到 `Room`/`GameManager`。
