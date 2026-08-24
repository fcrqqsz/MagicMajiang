# GEMINI.md - SuperMajiang Project Context

## Project Overview
**SuperMajiang** 是一款基于 Unity 的 Roguelike 国标麻将游戏，支持 WebSocket 联机；单人游玩同样使用在线 `Room`，由 AI 补足其余席位。
*   **核心规则**: 国标麻将 (MCR/Guobiao)，支持 81 番种计算。
*   **特色系统**: Roguelike 天赋系统、34 张自定义牌库与异化值 (Alienation) 机制。
*   **当前阶段**: Alpha - UI、联机权威、天赋垂直切片与卡组预算编辑器已完成。

## Technical Stack
*   **Engine**: Unity 2022.3.61t9 (Tuanjie 1.6.8)
*   **UI System**: UI Toolkit (UXML/USS) — **严禁使用 Legacy Canvas/UGUI**
*   **Animation**: DOTween (Pro)
*   **Text & Fonts**: UI Toolkit 的 `-unity-font-definition` 统一引用由 `Assets/Font/MSYH.TTC` 生成的 TextCore `Assets/Font/MSYH_UITK.asset`；**不得在 USS 中直接引用 TTC 或 TMP 的 `MSYH_SDF.asset`**。共用 `PanelSettings.asset` 绑定 `SuperMajiangTextSettings.asset`。
*   **Language**: C#

## Architecture & Structure
有关详细的架构模式、目录索引及实现范式，请参阅 **[struct.md](./struct.md)**。

### 核心架构要点
1. **胖客户端，瘦服务端 (Fat Client, Thin Server)**:
   - `GameServer`: 负责洗牌、发牌、状态流转、服务端动作校验与并发仲裁；权威维护 `ServerGameState` 手牌、副露和牌河。
   - `LocalPlayerClient` / `SimpleAIClient`: 本地计算吃碰杠胡权限和算番，将意图发往服务端。
   - **超时取消机制**: 服务端通过 `CancellationToken` 取消客户端 async 操作，`ServerGameState` 提供真实手牌兜底出牌。
2. **联机服务端与房间系统**:
   - Dedicated Server 使用 Headless 模式从 `00_ServerBootstrap` 启动；客户端默认首场景为 `00_Persistent`。
   - 服务端链路：`ServerBootstrap -> WebSocketService -> ConnectionRegistry -> RoomManager -> Room -> GameServer`。
   - 协议版本为 v8，携带构筑 schema 为 v3。连接先经 `Hello` 握手，开发期由 username 生成稳定 `playerId`。
   - 每个真人席位使用连续递增的 `SeatMessageStream`；`RoomGameSnapshot` 只向本家暴露完整暗手牌；客户端通过纯 C# `ClientGameState` 投影原子更新状态。
   - 断线后保留逻辑席位并在安全决策边界由 AI 托管；重连通过完整权威快照恢复。
3. **多局对战系统**:
   - `GameSession`: 管理多局状态（圈风轮转、门风分配、累计分数），支持 Single / EastOnly / HalfGame / FullGame。
   - `WindDirection` 枚举值与牌面 Value 对齐 (East=1..North=4)。
4. **算番系统**:
   - Strategy + Reflection 模式，规则由 `[FanRuleAttribute]` 标记，`FanRuleRegistry` 纯 C# 单例自动注册。
   - 支持 `GetMatchCount` 多重触发与多路径拆解取最大番。
5. **天赋系统 (Roguelike)**:
   - 纯 C# 管道架构，服务端统一执行，由 `[TalentRuleAttribute]` 标记，`TalentRegistry` 自动注册。
   - `Room` 锁定四席构筑后恰好创建一个跨小局复用的 `TalentMatchRuntime`。
   - 携带构筑为 6 个主槽（大×1 + 中×2 + 小×3）+ 3 个备选槽。异化值档位为 Low 40 / Standard 80 / High 120，总成本 = 牌库成本 + 当前激活主天赋成本。
   - 已落地 26 个天赋：点金手、窥探、如龙、厚积、快人一步、初始资金、定心、截流、藏锋、轻装上阵、归色、异彩成章、乘势、褪色、化劲、合围、背水阵、点将、循迹、洞若观火、定调、未雨绸缪、去芜、候潮、预判、障眼法。
   - 已提交动作通过只读小局账本供天赋统计；弃牌天赋管道的最终牌先进入权威牌河，再作为响应窗口唯一目标，超时自动弃牌也遵循同一路径。
   - 半庄/全庄第 4 小局后进入 45 秒中场备牌；胡牌番数拆为基础番、天赋门槛/奖励/惩罚和最终番并逐项归因。

## Critical Constraints (核心开发约束)

### UI & 输入所有权 (UI Toolkit Input Ownership)
*   **禁止 Canvas/UGUI**: 始终使用 UI Toolkit (UXML/USS/CS)。
*   **动态面板隐藏规范**: 动态弹窗、目标选择器和阶段面板在隐藏时，**必须**让整个 `UIDocument.rootVisualElement` 设置 `DisplayStyle.None`，显示时恢复为 `DisplayStyle.Flex`。仅降低透明度或设置 `PickingMode.Ignore` 不足以解决跨 `UIDocument` 的输入拦截问题。
*   **全屏阶段面板独立性**: 具有独立阶段、全屏输入或独立恢复状态的面板（如 `SideboardPanel`），必须优先设计为独立 `UIDocument` / Scene Object。
*   **清理与恢复**: `Show`、`Hide`、取消、超时、恢复、回合切换和 `OnDestroy` 必须汇入同一套生命周期清理边界（停止 schedule/tween/coroutine，解绑回调，重置状态并归还下层输入）。
*   **HTML/CSS 原型转译与严格子集约束**:
    - **原型流程**: 复杂新界面可先编写纯 HTML/CSS 预览文件供用户查看排版与设计，确认后再 1:1 转译为 UXML/USS。
    - **严禁 USS 不支持的属性**: 严禁 `cursor`（避免 `UpdateRuntimePanels` 运行时贴图警告）、`box-shadow`/`filter`（改用 `border` 或色块）、`transform`、`display: grid/inline/block`（仅支持 `flex` 与 `none`）、`z-index`（由 DOM 声明顺序或 `sortingOrder` 决定）、伪元素 `::before`/`::after`、高级选择器 `:nth-child`、相对单位 `rem`/`vw`/`calc`（仅用 `px` 与 `%`）。
    - **显式 Flex 方向**: HTML 原型所有 flex 容器必须显式写明 `flex-direction: column` 或 `row`（因浏览器默认 `row` 而 UI Toolkit 默认 `column`）。
    - **Emoji 绝对禁令**: HTML 原型与 UXML 文本严禁使用 Unicode Emoji（📋/👑/🔄/🀄/●/✓/✕ 等），避免 TextCore 字体出现 `[□]` 方块乱码。
    - **字体引用**: USS 统一引用 `MSYH_UITK.asset`，对齐使用 `-unity-text-align`，加粗使用 `-unity-font-style: bold;`。

### 编码与架构规范
*   **单例模式**: 逻辑层核心管理器（如 `FanRuleRegistry`, `TalentRegistry`）必须使用纯 C# 懒加载单例，避免对场景 GameObject 的硬依赖。
*   **DoTween 动态绑定**: 针对动态生成/销毁的 GameObject 进行动画时，**必须**链式调用 `.SetLink(gameObject)` 防止销毁报错。
*   **客户端无隐式本地权威**: 客户端不得自行推导分数、轮次、牌河或决策状态，统一从 `ClientGameState` 投影读取。

### 自动化验证与 Unity 集成边界
*   **纯 C# 自动回归优先**: 智能体日常验证以纯 C# 为主（运行 focused regression 或 `NetworkRegression`）。
*   **严禁智能体猜测/手写 `.meta` GUID**: 新增资产允许暂时没有 `.meta`，等待人工在 Unity/Tuanjie 中 Refresh 权威生成；不得预造 GUID。
*   **严禁修补生成工程**: 禁止智能体编辑或修改 Unity 生成的 `Assembly-CSharp.csproj` 等文件。
*   **Unity 人工关口**: Unity Refresh、Console 导入确认、生成工程构建以及最终视觉/音频 smoke test 默认由人工执行。智能体汇报时应明确说明“纯 C# 验证通过，Unity 集成/视觉验收待人工执行”。

### 代码编辑与 Git Diff 规范 (Code Editing & Clean Diff Constraints)
*   **严禁擅自整文件覆盖修改**: 修改已有源文件时，必须严格使用精准局部替换（`replace_file_content` / `multi_replace_file_content`），明确指定唯一的上下文范围与锚点。严禁因局部匹配不准或存在同名方法就退化为整文件重写/覆盖写入（`write_to_file`）。
*   **精准单点追加（Append-Only / Minimal Diff 规范）**:
    - **新增方法末尾单点追加**: 为大型类（如 `LobbyController` 等）新增独立功能、辅助方法或事件回调时，必须在类的安全末尾或指定分区精准追加，**严禁跨越大段包含存量代码的区间做整体重写**，杜绝因“上下文吞噬”误删同一文件内其他存量辅助方法（如轮播、设置、工坊联动等）。
    - **收敛修改锚点与跨度 (Tight Anchor Bounds)**: 修改已有方法时，`StartLine` 到 `EndLine` 必须严格聚焦在目标方法本身的局部范围（通常 5~20 行），严禁跨越多个方法边界大面积框选替换。
    - **编辑后存量方法完整性核验**: 编辑大型单文件控制器后，必须主动确认文件内原有存量功能与声明未被意外破坏或遗漏。
*   **避免 Diff 噪点与换行符污染**: 全量重写会导致跨平台换行符（CRLF/LF）转换及格式化微调，在 Git Diff 中产生大面积“删除整段又原样加回”的严重 Review 干扰。修改必须保持最小变动集（Minimal Diff）。
*   **修改后主动核对 Diff**: 修改代码后，应通过 `git diff` 检查改动范围是否精准聚焦，确保 Diff 中仅包含本次任务必要的增删改行，绝不带入无意义的空白符、格式化或不相关代码重排。

## 开发与参考 (Development & References)
*   **进度跟踪**: 参阅 `summary.md` 获取最新快照、关键决策及排故日志。
*   **任务与规划**: 参阅 **[plan.md](./plan.md)** 获取当前待办与长期优化路线图。
*   **里程碑归档**: 参阅 `milestone.md` 查看历史已完成功能。
*   **架构索引**: 参阅 **[struct.md](./struct.md)** 获取详细类结构与数据流。
*   **联机验证指南**: 参阅 `docs/network_verification.md`。
*   **回归测试命令**:
    *   网络自动回归: `dotnet run --project Tests\NetworkRegression\NetworkRegression.csproj --no-restore`
    *   GameServer 算番/遥测回归: `dotnet run --project Tests\GameServerTelemetryRegression\GameServerTelemetryRegression.csproj --no-restore`
