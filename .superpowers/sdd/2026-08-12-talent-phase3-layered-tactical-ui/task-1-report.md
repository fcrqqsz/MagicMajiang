# Phase 3 Task 1 Report

## 修改摘要

- `SavedDeck` 新增构筑级 `AlienationPreset`，旧数据及未定义枚举在 `Normalize` 中迁移为 `Standard`；`PlayerProfile.Normalize` 逐一归一化所有构筑并保留其他设置。
- 删除全局 `ProfileSettings.SelectedAlienationPreset`。
- 玩家构筑 wire schema 升至 v3，`PlayerLoadoutMessage`、`TrustedPlayerLoadout`、创建/解码/克隆路径完整携带档位。
- 服务端按固定顺序校验 schema、消息/房间档位、重建构筑及预算；新增 `AlienationPresetMismatch`，并将 mismatch 的两个档位与 over-cap 的 `actual/limit` 分开结构化返回。
- 客户端创建房间支持显式 room preset，构筑消息始终携带所选保存构筑的 preset；加入房间同样携带保存 preset；无保存构筑默认 Standard；无效索引不发送。
- 协议版本升至 v4；v3 在 Hello 阶段拒绝，schema v2 在直接解码边界拒绝。
- 增加 `talent-presentation` 聚焦套件并迁移既有 Low-room 测试夹具到 schema v3。

## RED / GREEN 记录

1. Saved-deck 与 schema v3
   - RED：`pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- talent-presentation"`
   - 结果：exit 1；预期编译失败，缺少 `SavedDeck.AlienationPreset`、三参 `CreateMessage`、`PlayerLoadoutMessage.alienationPreset`。
   - GREEN：同一命令。
   - 结果：在完成同一迁移链的服务端/客户端 RED 后 exit 0，`Network regression tests passed.`

2. 服务端 mismatch / over-cap 与客户端命令
   - RED：同一 focused 命令。
   - 结果：exit 1；预期缺少 `AlienationPresetMismatch`、结构化 preset 字段与 CreateRoom 新签名；同时发现并修正测试自身嵌套类型名后实施生产代码。
   - GREEN：同一 focused 命令。
   - 结果：exit 0，`Network regression tests passed.`

3. Protocol v4
   - RED：同一 focused 命令。
   - 结果：exit 1；唯一回归消息为 `protocol v4 rejects protocol v3 before room loadout admission`。
   - GREEN：同一 focused 命令。
   - 结果：exit 0，`Network regression tests passed.`

4. schema 校验顺序
   - RED：同一 focused 命令。
   - 结果：exit 1；唯一回归消息为 `schema v2 is rejected before any preset or loadout reconstruction checks`。
   - GREEN：同一 focused 命令。
   - 结果：exit 0，`Network regression tests passed.`

5. 兼容 CreateRoom 入口
   - RED：同一 focused 命令。
   - 结果：exit 1；唯一回归消息为 `the compatibility create entry uses the selected saved deck preset`。
   - GREEN：同一 focused 命令。
   - 结果：exit 0，`Network regression tests passed.`

## 完整回归

- 命令：`pwsh -NoLogo -NoProfile -Command "dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore"`
- 最终结果：exit 0，`Network regression tests passed.`
- 中间首次全套运行因旧 Low-room 夹具仍生成默认 Standard wire，在 `RoomSessionTests.TestRoomAlienationAdmission` 找不到 `RoomJoined`；显式迁移夹具 preset 后完整回归通过。

## Diff check

- 命令：`pwsh -NoLogo -NoProfile -Command "git diff --check"`
- 最终结果：exit 0，无 whitespace error。
- 分支：`codex/talent-actions-ui-unified`；起始与提交前 HEAD：`2498c163fc27f1ca8db70a80f3222dc582585a9d`。
- 未跟踪 `.superpowers/brainstorm/` 保持未修改、未暂存。

## 自审

- 逐项核对简报接口与固定解码顺序；确认未实现 Task 2+。
- 独立只读 code review 未发现 Critical；发现兼容 `CreateRoom` 静默选 Standard 的 Important 问题，已通过新 RED→GREEN 修复。
- 补充了 trusted clone 保留 preset、Profile 设置保留、真实 Hello v3 拒绝、schema v2 先拒绝、结构化中文错误不解析任意 server message 的覆盖。
- Reviewer 的 Minor 建议为移除 codec 两参兼容 overload；本任务保留它们仅供既有 Standard 构筑测试/内部调用兼容，新提交客户端路径均显式传递保存 preset。

## Commit

- 本报告随 `feat: bind alienation presets to saved loadouts` 提交；最终 commit SHA 由提交结果确认。

## Concerns

- 无阻断性 concerns。
- 未运行 Unity Editor / Dedicated Server 图形或进程级验证；本任务按简报完成 .NET 网络回归。
