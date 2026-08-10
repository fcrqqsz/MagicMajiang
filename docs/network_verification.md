# 联机框架验证指南

本文档是 SuperMajiang 联机框架的长期验证入口，覆盖 Dedicated Server、房间、多真人、多小局和断线重连。协议当前为 v3，携带构筑 schema 为 v2，默认端口为 `9876`。

## 1. 自动检查

在项目根目录执行：

```powershell
dotnet restore Assembly-CSharp.csproj
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore
dotnet build Assembly-CSharp.csproj --no-restore
git diff --check
```

预期结果：

- NetworkRegression 输出 `Network regression tests passed.`
- Assembly-CSharp 编译为 0 错误
- `git diff --check` 无空白错误；LF/CRLF 提示不视为空白失败

NetworkRegression 只覆盖可脱离 Unity 运行的协议、策略、快照和生命周期行为。真实 WebSocket、Unity 场景、3D 牌桌和 UI Toolkit 恢复必须执行后续人工矩阵。

## Room-only 一人 AI 补位

先在项目根目录执行以下自动检查：

```powershell
pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"
```

预期 `Network regression tests passed.`。该命令仅验证可脱离 Unity 的边界和场景序列化守卫；以下矩阵必须在 Unity Editor、Dedicated Server 和客户端中人工执行并逐项记录结果：

1. 使用默认 `--aiFill=true` 启动 Dedicated Server。
2. 启动一个普通客户端，登录后创建 `Single` 房间并点击 Ready，确认 1–3 号席位为 AI。
3. 完成该小局，查看最终结算，返回大厅后再创建一个房间。
4. 使用 `HalfGame` 重复，至少验证两次局间推进。

5. 在主回合决策期间断线，使用 username 和 room ticket 重连，确认快照恢复，并在下一个决策边界由 AI 交还真人控制。
6. 停止 Dedicated Server 后尝试创建房间，确认客户端停留在大厅。
7. 分别在 Editor 和 development build 中直接运行 `03_Game`，确认记录 `MissingNetworkRoomForGameScene` 并返回 Persistent。

## Plan 1：天赋基础与异化值验证

先复用上方自动检查命令。自动回归应覆盖 Low 40 / Standard 80 / High 120 预算边界、服务端重建构筑、未激活备选槽不计成本、他席精确总异化值不泄露、发牌后 Peek、跨两小局 runtime 生命周期与六个既有天赋。

手工验证时：

1. 以三档异化值分别创建或加入房间，确认超预算或非法构筑在变更房间状态前被拒绝；非本家仅能看到档位，不能看到精确总值、完整构筑或私有 Peek。
2. 在发牌后确认 Peek 显示的是洗牌和发牌后的牌山顶部；完成两小局，确认同一个 Room runtime 跨局存在而小局状态按规则重置。
3. 确认异化牌只在权威物理牌跨越弃牌、公开副露、加杠或和牌边界时产生公共揭示；手牌与私有 Peek 不得提前公开。
4. 人为触发异常终局路径，确认完成消息只发出一次，Room 仍以异常安全的终局回退收束。

## 2. 构建 Dedicated Server

1. 在 Unity 中执行 `Tools > Build > Dedicated Server (Windows)`。
2. 构建脚本只包含 `Assets/Scenes/00_ServerBootstrap.unity`，不会修改客户端 Build Settings。
3. 默认输出为：

   ```text
   Builds/DedicatedServer/SuperMajiangServer.exe
   ```

4. 启动服务端：

   ```powershell
   .\Builds\DedicatedServer\SuperMajiangServer.exe --port 9876 --maxRooms 8 --aiFill true --reconnectWindowSeconds 120 --messageCacheSize 256 --heartbeatTimeoutSeconds 10
   ```

5. 日志应包含 `ServerBootstrap started` 和 WebSocket 监听 `ws://0.0.0.0:9876/game`。客户端连接地址使用 `ws://127.0.0.1:9876/game`。

## 3. 基础环回

1. 启动 Dedicated Server 和一个 Standalone 客户端。
2. 使用 development username 登录，创建 `aiFill=true` 房间。
3. Ready 后确认 1 真人 + 3 AI 能进入 `03_Game`。
4. 完成一小局，确认手牌、牌河、副露、分数和结算一致。
5. 点击下一局，确认服务端开始新局并收到新手牌。

## 4. 房间与构筑矩阵

分别验证：

- 1 真人 + 3 AI
- 2 真人 + 2 AI
- 3 真人 + 1 AI
- 4 真人，`aiFill=false`

每种组合确认创建、加入、席位昵称、构筑摘要、Ready、加载场景和离房行为。不同客户端使用不同牌库和天赋，确认服务端锁定四席配置；非法 34 张牌库或天赋槽配置必须返回稳定 `RoomError`。

## 5. 多局验证

使用 `EastOnly` 完成东一至东四：

- 每局结算后都能推进下一局
- 圈风、门风、当前局数和累计分数正确
- 最终结算停留在东四，不出现第五局或南风
- 每局牌河、手牌、副露和天赋状态正确重置

HalfGame 和 FullGame 修改局数上限后沿用同一检查方法。

## 6. 重连矩阵

使用两个不同 username 的客户端，至少覆盖：

1. 等待房间、Loading、主回合、响应阶段、局间 Ready、普通结算和最终结算分别强制关闭客户端并重启。
2. 确认自动执行 `Hello + Reconnect`，等待房间恢复到大厅房间视图，对局状态恢复到游戏场景。
3. 恢复后的本家手牌、副露、牌河、对手暗牌数量、风位、分数、牌山余量和结算信息必须一致。
4. 仅当本家仍拥有未过期决策时恢复操作 UI；已提交、已过期或 AI 已锁定的决策不得重复操作。
5. 心跳确认中断 10 秒后，客户端应禁用输入并按 `0、1、2、4、8、10` 秒、之后每 10 秒自动重试。
6. `aiFill=false` 四真人房断线后，已打开决策保留原控制者至截止时间，后续决策由 AI 托管；真人在下一安全边界交还控制。
7. 1 真人 + 3 AI 房中真人断线后，因无在线真人，房间立即关闭。
8. 同 username 并发连接返回 `IdentityInUse`；旧连接离线后允许原身份恢复保留席位。
9. 在恢复遮罩中主动离房，确认清除本地票据且不会再次自动恢复。
10. 重启 Dedicated Server 后，旧票据收到 `RoomNotFound` 或等价终止错误并安全返回登录页或大厅。

## 7. 隐私与一致性

- 任意客户端不得收到其他真人的完整暗手牌、牌库、天赋配置或私有窥探结果。
- 公共广播分别进入各席消息流，私有消息不得出现在其他席缓存。
- 重连后不得出现重复手牌、牌河、副露、动画、分数、提示或动作提交。
- 旧 WebSocket 的迟到 `OnMessage` / `OnClose` 不得影响已经重绑的新连接。

## 8. 当前生产边界

- username 仅为开发期身份凭证，不是正式账号鉴权。
- 生产部署必须使用 TLS/WSS；证书和反向代理不由游戏进程管理。
- Dedicated Server 重启不会恢复房间。
- 当前恢复策略始终请求完整权威快照；消息缓存仍用于席位有序流和未来增量恢复扩展。
