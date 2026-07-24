# Phase E 联机重连人工验收

> 状态（2026-07-25）：Phase E 自动检查与 Unity 真人联机验收均已通过。

## 准备

1. 启动 Dedicated Server，例如：

   ```powershell
   .\Builds\DedicatedServer\SuperMajiangServer.exe --port 9876 --maxRooms 1 --aiFill true --reconnectWindowSeconds 120 --messageCacheSize 256 --heartbeatTimeoutSeconds 10
   ```

2. 启动两个客户端，使用不同 development username；`username` 仅为开发期身份桥接，不是正式账号鉴权。
3. 两人创建/加入同一房间并准备，确认可进入游戏。保留 `Builds/DedicatedServer/server.log` 用于核对断线、重连及超时记录。

## E4.1 有序状态路径

1. 在游戏加载或一方刚出牌后，短暂断开客户端网络或强制关闭一个客户端。
2. 观察另一个客户端仍按服务端决策继续，不出现由旧端点重复触发的动作。
3. 恢复网络或重新启动被关闭客户端。确认恢复后的手牌、副露、牌河、剩余牌数、风位、分数和结算信息都与服务端/另一客户端一致，且没有重复动画、重复副露或旧操作按钮。

## E4.2 决策与桌面原子重建

1. 在本家主回合强制关闭客户端，10 秒内以相同 username 登录。
2. 确认重连覆盖层显示连接/同步状态；完整快照到达后，只有仍未到期且由本家真人控制的主回合才恢复出牌输入。
3. 在他家打牌后的响应阶段重复上述操作。若本家仍在 eligibleSeats 且未提交，确认可见响应操作；若已过期、已提交或席位显示 `AiControlled`，确认没有可操作输入。
4. 在暗杠存在时重连，确认所有玩家都能看到国标麻将声明的暗杠牌面；对手的其余暗手牌只显示背面与数量。

## E4.3 启动恢复、覆盖层和离开

1. 在等待房间阶段强制关闭客户端（不要点离开）。同 username 再次登录后，确认自动发送 `Hello + Reconnect`，并自动切到大厅的房间视图，而不是 Home；再次输入同一房号不应作为恢复手段。
2. 在 Loading、InRound、WaitingForNextRound 及 SessionCompleted 四个阶段分别强制关闭并重启。确认恢复快照自动路由到游戏场景，且没有 Login/Lobby/Game 叠加场景或陈旧桌面。
3. 断开服务器或网络，确认覆盖层禁用游戏输入，并按 `0、1、2、4、8、10` 秒、之后每 10 秒显示/执行重试。
4. 在恢复覆盖层点击 `Leave Room`。确认本地票据被清除、覆盖层关闭并回到大厅；服务端仍按已有离线席位/过期策略处理该席位，客户端不会再次自动恢复。
5. 停止并重启 Dedicated Server，再启动携带旧票据的客户端。确认收到 `RoomNotFound` 后清除票据并留在 Login/Lobby，不加载旧游戏场景。

## 回归矩阵

- 两真人、`aiFill=true`：主回合、响应阶段、结算阶段、局间阶段各执行一次强制关闭恢复。
- 四真人、`aiFill=false`：断线后确认当前决策直到 deadline 仍由既有控制者完成，后续决策才按 E3 接管策略执行。
- 一真人 + 三 AI：真人断开后房间应按“无在线真人”关闭，不应提供成功恢复。
- 同 username 并发：第二个连接必须显示 `IdentityInUse`，不得创建/加入房间；旧连接断开后新连接可重试恢复。
- EastOnly：完成四局，并在任一局断线恢复；确认没有重复手牌、副露、牌河、分数、提示或动作提交。

## 自动检查

```powershell
dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore
dotnet build Assembly-CSharp.csproj --no-restore
git diff --check
```

## 验收记录

- 2026-07-25：Phase E 真人联机测试通过，未发现重连、托管、场景恢复或多小局推进阻断。
- 心跳确认超时后能够先进入恢复状态，再关闭旧连接并自动重连。
- 本地恢复票据缺失时会清理 Hello/握手状态、关闭旧连接并进入终止失败，不会逐帧重复处理。
- NetworkRegression、Assembly-CSharp 编译和差异空白检查通过。

Phase E 已在总计划中标记为完成。
