# 对战菜单与退出流程验证

## 当前集成状态

2026-09-04：原型已获用户确认；客户端源码、共享音频 UI、纯 C# 回归和 `03_Game` 独立菜单接线已完成。UnityMCP Refresh 后编译完成，导入阶段 Console 无错误或警告；所有新 `.meta` 由 Unity 生成。

本次通过 MCP 在 Play Mode 创建临时本地服务器，运行真实 WebSocket/Room/GameServer，已验证：

- 菜单首页、设置页及退出确认页在 1920×1080 Game 视图中正常渲染，中文字体完整；确认页默认聚焦安全选项。
- 菜单隐藏时根节点为 None，显示时本地输入门禁生效；菜单打开期间摸弃牌及服务端超时出牌继续。
- UI 回调将总音量设为 0 后，管理器及百分比同步为 0；恢复默认得到 80% / 60% / 100%。
- 经菜单确认退出后回到大厅，健康 WebSocket 保持 Open，加载层隐藏，菜单销毁，输入门禁解除。
- 对战中设置 70% 后大厅滑条显示 70%，AudioManager 实例未重建；再次建房开局成功，重复退出共享同一任务。测试结束已恢复原音量并停止临时服务器。

运行中出现现有的超时出牌 Warning，以及最后真人离房关闭房间时的 `[GameServer] 游戏已被强制终止。` Error 级日志；未出现菜单异常、资源导入错误或场景加载异常。后者是现有服务端取消日志，不代表导航失败。

MCP 按钮验证使用 UI Toolkit 事件派发；其后用户完成真实 Esc、鼠标/3D 手牌、备牌/结算/重连层级交互、不同窗口比例和实际听音验收，并于 2026-09-04 确认测试无问题。纯 C#、Unity 集成、交互、视觉与音频验收均已完成。

## Unity 接线

1. 按 `AGENTS.md` 的持续授权直接使用 UnityMCP Refresh，等待所有导入、编译和 Domain Reload 完成；MCP 不可用时由人工执行。新资产 `.meta` 仅接受 Unity 生成版本。
2. 在 `Assets/Scenes/03_Game.unity` 根层级新建 `UIDocument_BattleMenu`，挂载 `UIDocument` 与 `MahjongGame.UI.BattleMenuController`。
3. 文档 Source Asset 绑定 `Assets/UI/BattleMenu.uxml`，Panel Settings 复用 `Assets/UI Toolkit/PanelSettings.asset`，Sorting Order 为 200。控制器的文档字段可引用同对象的 UIDocument，也可由 `GetComponent` 取得。
4. 保留当前 HUD、备牌、结算控制器宿主。三处 `BattleMenuButton` 自动绑定独立菜单控制器；不要将菜单挂在 HUD 上，也不要创建 Canvas。
5. 保存游戏场景。客户端首场景仍为 `00_Persistent`，服务端启动场景和 Build Settings 不变。

## 自动回归

```powershell
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- battle-menu
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- battle-exit
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- scene-navigation
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- audio
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore
```

这些测试保护状态转换、过期权限、关闭当帧点击阻断、退房发送/失败/5 秒超时、旧消息隔离、重连与场景切换竞态，以及真实 Room 的 AI 补位和阶段推进。UXML/USS 的静态检查只能验证结构与资源路径，不能替代实际点击。

## 对战与输入

- 从登录流程进入一人在线房间。菜单隐藏时，ActionPanel、3D 手牌及天赋目标选择可用。
- 右上角“菜单”和 Esc 都可打开首页；“继续对战”关闭，设置页 Esc 返回首页，首页 Esc 关闭。
- 确认页默认聚焦“继续对战”，Esc 返回首页；点击背景遮罩不关闭，也不出牌。点击菜单内按钮、滑条和关闭动作不得影响背景手牌。
- 设置保持打开至本家出牌超时，对局应继续推进。关闭后不得出现旧决策按钮或恢复旧出牌权限。
- 小局结束、流局、开始新局、进入备牌、终局和重连时，普通菜单自动关闭；已经确认的退出不会被取消。
- 备牌与结算页自己的“菜单”按钮可以点击；菜单层级高于这两页，加载/重连层级高于菜单。关闭高层界面后输入立即归还。
- 以 1920×1080、1280×720 和较窄窗口验收布局、滚动、中文字体、按钮及滑条的完整可见性。

## 退出与恢复

- 一真人房退出：返回大厅且房间关闭，健康 WebSocket 保留，可以再次建房开局。
- 两真人房退出：剩余玩家继续，离开席位由永久 AI 接替；原席位不可用旧票据恢复。
- 在主回合、响应窗口、局间等待和中场备牌分别退出，其余席位不得卡住；备牌离席使用服务端合法兜底方案。
- 连点确认仅发送一次 LeaveRoom；发送失败或等待达到 5 秒也能返回大厅，无持续自动恢复。
- 重连恢复进行到一半时退出，迟到快照/RoomReady 不能把玩家带回游戏场景。保留登录身份与所选服务器环境。
- 权威 SessionEnd 后菜单显示“返回大厅”，直接返回；不重复发送 LeaveRoom。结算页原有返回按钮走同一导航流程。
- 场景加载或卸载失败时，菜单显示重试按钮；从结算页返回失败时应重新显示结算面板及“重试返回大厅”，不能留下无法操作的牌桌。

## 音频

- 三路音量实时影响现有 Mixer；0% 完全静音，试听音效服从总音量和音效音量。
- 在对战中调整后返回大厅、重新进入对战、重启客户端，值保持一致；恢复默认值为 80% / 60% / 100%。
- 打开/关闭菜单不暂停或重播 BGM，返回大厅沿用已有音乐切换。
- 音频管理器不可用时显示不可用文案并禁用控件，不影响返回与退出按钮。

## 代码归属

- `BattleMenuState`、`BattleMenuInputGate`：纯客户端菜单状态与额外输入限制，不改变服务端决策权限。
- `BattleMenuController`：独立文档、音频绑定、焦点与生命周期；通过有序消息和恢复事件收口阶段边界。
- `ClientRoomService.LeaveRoomForLobbyAsync()`：一次性离房、票据清理、消息隔离及连接处理。
- `NetworkManager.LeaveBattleToLobbyAsync()` / `ClientSceneNavigation`：共享大厅导航，串行处理不能取消的 Unity 加载操作，撤销旧路由的场景激活权限。
- `AudioSettings.uxml` / `AudioSettingsStyles.uss` / `AudioSettingsView`：大厅与对战共用控件，复用常驻音频管理器和本机偏好存储。
