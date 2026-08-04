# SuperMajiang 开发计划 (Development Plan)

本文档记录当前开发任务与长期优化路径。已完成的里程碑见 `milestone.md`。

## 1. 当前开发任务 (Active Backlog)

### High Priority (高优先级 - 游戏性反馈与 UX)
*   **天赋玩法垂直切片（当前主线，按顺序实施）**:
    *   [ ] 前置：完成 [`Room` 唯一权威与移除隐式本地模式](docs/superpowers/plans/2026-08-04-room-authority-remove-local-mode.md)，一人游玩统一使用在线房间 AI 补位。
    *   [ ] 第一阶段：完成[天赋运行时、异化预算与现有六天赋迁移](docs/superpowers/plans/2026-08-04-talent-foundation-and-alienation.md)。
    *   [ ] 第二阶段：完成[主动天赋、三项锚点天赋与中场备牌](docs/superpowers/plans/2026-08-04-talent-actions-and-sideboard.md)。
    *   [ ] 第三阶段：完成[天赋 UI、AI、反馈与玩法测试](docs/superpowers/plans/2026-08-04-talent-ui-ai-feedback.md)。
*   **异化牌视觉反馈**:
    *   [ ] 根据 `TileData` 的异化状态（天赋修改），通过 `TileVisual` 改变牌背颜色或增加发光特效。

### Low Priority (低优先级 - 压力测试与表现细节)
*   **天赋槽图标显示**:
    *   [ ] 为 `TalentDefinition` SO 资产配置天赋图标（Sprite），在牌库编辑器槽位中显示。
    *   [ ] 恢复 `TalentSlotTemplate.uxml` 中的 `IconContainer` 元素，`RefreshTalentSlots` 中绑定图标。
    *   [ ] 可选：Home 页卡组名下方显示已装配天赋的小图标。
*   **逻辑健壮性压力测试**:
    *   [ ] 针对异化牌库（如同种牌 8 张以上）的极端组合进行算番拆解算法验证。
*   **发牌与摸牌动画**:
    *   [ ] 使用 DoTween 实现牌从牌山飞入位置的序列动画，而非瞬间生成。
    *   [ ] 优化"新摸牌"与"已有手牌"之间的 `drawGap` 动态管理。

---

## 2. 长期优化路线图 (Optimization & Infrastructure)

### 性能优化
*   **对象池 (Object Pooling)**: 引入 `TilePool` 管理 `TileVisual` 的生成与回收，减少高频实例化。
*   **资源预加载**: 在 `DeckManager` 初始化时缓存所有牌面 Sprite，消除首帧加载卡顿。

### 架构演进
*   **算番排斥逻辑自动化**: 在 `FanRule` 中引入 `RuleGroup` 判定，减少手动维护 `ExcludedRuleIds` 的成本。
*   **表现层调度器 (View Coordinator)**: 引入 `AnimationCoordinator` 统筹管理复杂的跨角色动画序列（如：A家杠 -> B家补花 -> C家胡牌）。

### 联机生产化
*   **正式账号与鉴权**: 用账号服务替换 development username 身份桥接，提供不可冒用的稳定 playerId、登录态和封禁能力。
*   **WSS 与部署边界**: 通过反向代理或网关启用 TLS/WSS，补充证书轮换、可信代理头和生产端口配置。
*   **服务端持久化**: 评估 Dedicated Server 重启后的房间/会话恢复；当前进程重启会终止全部房间。
*   **可观测性与容量治理**: 增加结构化日志、房间/连接指标、异常告警、限流和多实例调度。
