# 自己回合暗杠/加杠选择设计

## 目标

玩家在自己摸牌后的决策面板中，能够明确选择“暗杠”或“加杠”；若同一种杠有多个合法目标，必须选择具体牌后才提交动作。

## 范围

- 包含：暗杠、加杠的候选牌判定、强类型 UI 选择、二级牌目标选择、单机与联机客户端提交、纯 C# 回归测试。
- 不包含：抢杠胡、杠后响应窗口、AI 主动杠策略、服务端协议版本变更、牌面资源重做。

## 现状与根因

~ActionValidator.CheckSelfActions~ 将暗杠和加杠均折叠为 ~AllowedActions.CanGan~。~ActionPanelController~ 只回调字符串 ~"Gan"~，~LocalPlayerClient~ 因而固定优先提交暗杠，且直接选择各候选列表的第一个目标。服务端、协议和副露模型已使用不同的 ~ClientActionType.AnGan~ / ~ClientActionType.JiaGang~，无需修改线协议。

## 设计

### 纯规则模型

新增 ~SelfTurnKongOptions~ 和 ~SelfTurnKongResolver~，只依赖 ~TileData~ 与 ~Meld~：

- ~AnGangTargets~：手牌中每种数量至少为 4 的代表牌。
- ~JiaGangTargets~：每个 ~Pon~ 副露所对应、且当前手牌仍持有的代表牌。
- 两组目标按现有手牌/副露遍历顺序返回，保持玩家看到的顺序稳定。

~AllowedActions~ 将明确使用 ~CanMingGan~、~CanAnGan~、~CanJiaGang~。弃牌响应只设置 ~CanMingGan~；自摸判定只设置 ~CanAnGan~、~CanJiaGang~。现有 ~CanGan~ 删除，避免新旧含义并存。

### UI 与提交

~ActionPanelController~ 将字符串回调改为 ~ActionPanelChoice~ 枚举。主面板具有独立的“明杠”“暗杠”“加杠”按钮：

- 弃牌响应显示“明杠”；
- 自摸决策显示“暗杠”和/或“加杠”；
- 点击仅有一个目标的暗杠/加杠时直接回调类型与牌；
- 同类型目标超过一个时，面板显示临时二级按钮，文案为 ~暗杠\n5万~ 或 ~加杠\n5万~，选择后回调准确目标；
- 超时、取消、结算时 ~Hide()~ 清除所有主/临时回调，不能提交陈旧选择。

~LocalPlayerClient~ 使用同一份 ~SelfTurnKongOptions~ 同时决定显示与提交，永不再从 ~HandController~ 重新求值或索引 ~[0]~。服务端继续使用已存在的 ~AnGan~/~JiaGang~ 分支验证并在广播后补牌。

### 兼容性与边界

- ~RemoteServerProxy~ 保持现有序列化：提交的 ~ClientActionType~ 已原样写入网络消息。
- ~ClientGameState~ 已能把 ~JiaGang~ 由 ~Pon~ 升级到 ~Kan_Added~，本期补回归覆盖即可。
- ~SimpleAIClient~ 继续沿用“自摸胡否则出牌”的既有策略；它适配新的字段名但不新增自动杠行为。
- 抢杠胡留待第二期：届时为加杠引入声明/响应/确认的权威阶段，不能在本期直接补牌前临时插入 UI 逻辑。

## 验收标准

1. 仅暗杠时，只出现“暗杠”，并提交正确牌。
2. 仅加杠时，只出现“加杠”，并提交正确牌。
3. 两者同时可用时，两按钮同时出现，互不抢占。
4. 同类杠有多个目标时，必须经二级选择；没有默认第一个目标。
5. 明杠仍只在弃牌响应中出现。
6. 联机 ~ActionResolved(JiaGang)~ 仍将原碰升级为加杠，且本家暗手数量减少一张。

