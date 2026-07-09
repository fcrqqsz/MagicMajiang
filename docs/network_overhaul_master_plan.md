# SuperMajiang 联机化整体改造总计划

## 目标

在保留现有单机体验的前提下，把当前“1 个远程玩家 + 3 个服务端 AI”的联通验证，升级为可上线的 WebSocket 联机框架：

- 服务端权威：发牌、动作仲裁、算番、计分、天赋执行都由服务端决定。
- Headless 部署：正式服务端不依赖 `03_Game` 游戏场景、UI、相机或手牌表现层。
- 房间/席位清晰：连接、房间、席位、牌局生命周期分层管理。
- 多局流转稳定：客户端只维护 UI 镜像，服务端推进真实 `GameSession`。
- 玩家配置在线：每位玩家携带自己的 `DeckConfig` 和 `TalentSlotConfig`。
- 为断线重连打基础：保留 `seq`、快照、消息缓存的演进空间。

## 当前基线

已完成并保留：

- WebSocket 通道：`WebSocketService` / `WebSocketClient`。
- 远程代理：`RemotePlayerClient` / `RemoteServerProxy`。
- 服务端动作校验：客户端不能伪造可信 `PlayerId`。
- Ready 同步：长连接复用时，服务端等待客户端显式 `Ready` 后再启动小局。
- 局间结果同步：`PlayerWin` / `DrawGame` 已携带 `scores` 与 `completedRounds`，客户端可同步本地 `GameSession` 镜像。

当前已知限制：

- 临时服务端路径仍位于 `GameManager` 的 `isNetworkMode && isServer` 分支。
- 网络服务端仍有历史上对游戏场景对象的耦合风险。
- 尚未实现正式 `RoomManager` / `ConnectionRegistry`。
- 尚未实现真实多人房间和每席牌库/天赋上传。
- `seq` 尚未用于去重、补包或断线重连。

## 工作方式

主对话负责：

- 维护本总计划。
- 拆分 Phase、定义边界与验收标准。
- Review 每个子对话结果。
- 验证通过后决定是否进入下一 Phase。

子对话负责：

- 每次只执行一个 Phase。
- 不提前实现后续 Phase。
- 完成后回报变更文件、验证结果和剩余风险。

每个 Phase 完成后，应更新本文档中的“进度状态”和对应 Phase 说明。

## Phase A: Baseline Cleanup

状态：待执行。

目标：稳住当前联机基线，去掉最明显的服务端上线阻碍，但不引入房间系统。

范围：

- 新增 `GameServerOptions`。
- `GameServer` 不再直接读取 `GameManager.Instance.useDebugHand/debugHand`。
- 单机分支继续使用 `DeckManager.Instance`。
- 临时网络服务端分支改为注入纯 C# `WallService`。
- 新增本地环回验证文档，端口统一为 `9876`。

不得做：

- 不创建 `RoomManager`。
- 不创建 `ConnectionRegistry`。
- 不创建 `ServerBootstrap` 或新场景。
- 不实现断线重连。

验收：

- `dotnet build Assembly-CSharp.csproj --no-restore` 通过。
- 单机模式创建 `GameServer(DeckManager.Instance)` 的路径仍可用。
- 临时网络服务端路径不再使用 `DeckManager.Instance` 创建 `GameServer`。
- `GameServer` 中无 `GameManager.Instance.useDebugHand/debugHand` 读取。

## Phase B: Headless Server Decoupling

状态：待执行。

目标：建立正式 Headless 服务端入口，使上线服务端不再依赖游戏场景。

范围：

- 新增专用服务端场景 `00_ServerBootstrap.unity`。
- 新增 `ServerBootstrap`，启动 `WebSocketService` 并准备服务端运行环境。
- 解析命令行参数：`--port 9876`、`--maxRooms`、`--aiFill true`。
- 正式服务端构建首场景使用 `00_ServerBootstrap`。
- 服务端启动路径不得加载 `02_MainLobby` 或 `03_Game`。

不得做：

- 不实现完整房间匹配 UI。
- 不实现断线重连。

验收：

- Headless/Dedicated Server 构建启动后日志出现服务端启动信息。
- 正式服务端启动路径不访问 `GameManager.Instance`、`DeckManager.Instance`、`GameHUDController`、`ResultPanelController`、`HandController`。
- 当前 Editor 进入 `03_Game` 当服务端的临时调试路径可保留，但不作为正式部署路径。

## Phase C: Room V1

状态：待执行。

目标：实现最小可用房间/席位系统，把服务端连接映射和 Ready 分发迁出 `GameManager`。

范围：

- 新增 `ConnectionRegistry`，维护 `connectionId -> endpoint -> roomId -> seatIndex`。
- 新增 `Room` / `RoomManager`。
- `Room` 持有 `roomId`、`GameMode`、席位、每席牌库、每席天赋、`GameSession`、`GameServer`。
- `RoomManager` 处理创建房间、加入房间、Ready、离开、断线标记、AI 补位。
- 新增房间协议：
  - `Hello`
  - `CreateRoom`
  - `JoinRoom`
  - `RoomJoined`
  - `PlayerJoined`
  - `PlayerLeft`
  - `RoomReady`
  - `Ready`
  - `RoomError`
- `GameManager` 不再处理服务端连接映射、Ready 分发或房间启动。

首版默认：

- 支持 1 到 4 真人。
- 不足 4 人时用 `SimpleAIClient` 补位。
- 不做账号鉴权，身份由连接和席位绑定。

验收：

- 1 真人 + 3 AI 可开局。
- 2 真人 + 2 AI 可开局。
- 4 真人可开局。
- 客户端动作仍由服务端根据连接映射写入真实 playerId。

## Phase D: Player Config + Talent Online

状态：待执行。

目标：让每位玩家的自定义牌库和天赋配置进入联机房间，并由服务端执行。

范围：

- 创建/加入房间时上传当前选中 `DeckConfig` 和 `TalentSlotConfig`。
- 服务端验证牌库总数为 34。
- 不合法牌库返回 `RoomError`。
- 服务端保存每席配置，建墙时使用四席真实配置。
- 天赋统一由服务端执行。
- 客户端只接收 `TalentInfo`、`PeekWall` 等结果消息。

验收：

- 不同客户端上传不同牌库时，服务端建墙 ownerId 和牌张构成正确。
- 异化分显示正确。
- 已有天赋在联机下由服务端触发，客户端只表现结果。

## Phase E: Reconnect + Robustness

状态：待执行。

目标：引入断线重连、消息去重和基础健壮性。

范围：

- `seq` 改为服务端每连接递增。
- 客户端记录 `lastSeq`。
- 服务端保留最近 N 条房间消息缓存。
- 新增：
  - `Reconnect { roomId, lastSeq }`
  - `ReconnectState { snapshot, missedMessages }`
- 断线席位进入 AI 托管。
- 重连后重新绑定 endpoint。
- 接入心跳与断线判定。
- 服务端清理过期连接和空房间。

验收：

- 主回合断线后可重连恢复。
- 响应阶段断线后可重连恢复。
- 结算面板断线后可重连恢复。
- 局间 Ready 前断线后可重连恢复。

## 全局测试清单

- 编译检查：`dotnet restore Assembly-CSharp.csproj` 后 `dotnet build Assembly-CSharp.csproj --no-restore`。
- 本地环回：服务端 `ws://127.0.0.1:9876/game`，客户端连接并完成一局。
- 多局测试：EastOnly 完成 4 小局。
- 房间测试：1 真人、2 真人、4 真人组合。
- 配置测试：不同牌库和不同天赋。
- 断线测试：主回合、响应阶段、结算阶段、局间阶段。

## 架构原则

- `GameServer` 保持服务端权威，不迁移核心裁决到客户端。
- `GameServer` 尽量保持纯逻辑，不访问 UI、场景表现层或客户端控制器。
- `GameManager` 只负责单机和客户端场景生命周期。
- `RoomManager` 负责正式服务端的房间与牌局生命周期。
- 客户端不能提供可信 `PlayerId`。
- UI 继续使用 UI Toolkit，禁止引入 Canvas/UGUI。

## 后续子对话启动模板

每个 Phase 子对话使用以下格式：

```text
目标：执行 SuperMajiang 联机化整体改造计划的 Phase X。
范围：只实现 Phase X，不提前实现后续 Phase。
必须保持：单机模式可用；当前已完成的联机环回能力不回退。
参考文档：docs/network_overhaul_master_plan.md。
验收：dotnet build 通过，并完成该 Phase 对应手测/静态验证。
```
