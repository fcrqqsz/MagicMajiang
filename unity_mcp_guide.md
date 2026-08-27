# UnityMCP 使用与排障指南

本文记录 SuperMajiang 项目实际接入 UnityMCP 后验证过的操作顺序、常见问题和安全边界。它用于补充 `AGENTS.md`，不替代玩法测试、网络回归或 Unity Game 视图人工验收。

## 当前接入

- 编辑器：Unity `2022.3.61t9`（团结引擎 `1.6.8`）。
- 项目通过 `Packages/manifest.json` 引入 `com.coplaydev.unity-mcp`。
- `Packages/packages-lock.json` 记录实际解析到的 Git 提交；`manifest.json` 与锁文件必须成对提交。
- 团结编辑器桥接包当前为 `cn.tuanjie.codely.bridge` `1.0.76`。
- MCP 端口属于本机连接配置，不是项目协议或场景配置。不要把某次可用端口硬编码进源码或本文；切换后以实例查询结果为准。

## 推荐操作顺序

1. 保持 Unity 已打开目标项目，并确认不处于 Play Mode 或切换场景过程中。
2. 读取 `mcpforunity://custom-tools`，确认当前项目暴露的工具能力。
3. 读取 `mcpforunity://instances`：
   - 没有实例时先检查 UnityMCP 窗口、传输方式和端口。
   - 只有一个实例时可直接使用。
   - 多个实例时必须先用完整的 `Name@hash` 选择活动实例，不能依赖最近一次连接。
4. 读取 `mcpforunity://editor/state`，仅在以下条件满足后开始修改：
   - `data.advice.ready_for_tools` 为 `true`；
   - `data.compilation.is_compiling` 为 `false`；
   - `data.assets.is_updating` 为 `false`；
   - 活动场景与任务目标一致。
5. 修改前先读取目标场景、GameObject、组件或资源的现状。查询层级和组件时采用分页，先取摘要，确有需要才读取完整属性。
6. 执行场景或资源修改。多个同类操作可批量执行，但应保持目标明确，并在一组修改后统一检查结果。
7. 需要导入或编译时，根据本任务授权执行 Refresh，并等待编辑器重新进入 Ready 状态。
8. 读取 Unity Console，先检查 Error，再检查 Warning；不要在记录问题前直接清空 Console。
9. 明确保存被修改的场景或 Prefab，再用 Git 检查实际落盘文件，防止遗漏或混入编辑器副作用。
10. 运行相关纯 C# 回归。Game 视图布局、字体、阴影、点击输入等仍需 Unity 实际画面验收。

## Refresh 与 Unity 生成资产的边界

- 只有用户在当前任务明确授权时，智能体才可通过 MCP 执行 Unity Refresh；历史授权不自动延续到无关任务。
- 新增 Unity 资产后必须让 Unity/Tuanjie Refresh 权威生成 `.meta`。禁止手写、猜测、复制或修补 GUID。
- 需要在 Scene、Prefab、UXML 或其他序列化资产中引用新资源时，应先 Refresh，确认真实 `.meta` 已生成，再建立引用。
- Refresh 后要等待资源更新、脚本编译和 Domain Reload 全部结束。不要在编译期间连续重复 Refresh，也不要把短暂断连误认为源码失败。
- 禁止编辑 Unity 自动生成的 `Assembly-CSharp.csproj`。未 Refresh 导致的工程缺项也不能通过手工补写工程文件解决。
- MCP Refresh 的授权不包含 batch mode、Player Build 或 Dedicated Server Build；这些操作仍需单独授权。

## 已遇到的问题与判断方法

### `WebSocket is not initialised` Warning

曾在切换连接/端口后看到：

```text
MCP-FOR-UNITY: [WebSocket] Unexpected receive error: WebSocket is not initialised
```

这通常发生在 WebSocket 接收循环尚未完成初始化、正在切换连接，或旧连接已经关闭但异步回调仍返回时。单独出现一次不代表项目资源或游戏逻辑损坏。

按以下证据判断是否有实际影响：

- `mcpforunity://instances` 仍能列出正确的 Unity 实例；
- `editor/state` 不陈旧且 `ready_for_tools=true`；
- 简单只读调用（例如读取 Console 或活动场景）成功；
- Warning 不持续重复，后续修改和保存均能得到明确成功响应。

以上条件满足时，可把它视为连接切换产生的瞬时告警。若实例消失、状态持续陈旧、工具调用超时或同一 Warning 连续出现，则应停止写操作，重新检查 UnityMCP 面板、传输模式和端口，必要时重连后再从实例查询开始验证。不要直接修改 PackageCache 中的 UnityMCP 源码来压掉告警。

### 切换端口后工具仍指向旧实例

端口切换只改变传输入口，不保证调用端已经选择了新的编辑器实例。切换后应重新读取实例列表；如果存在多个实例，显式设置完整 `Name@hash`。随后重新读取 Editor State，不能沿用切换前缓存的场景和编译状态。

### Refresh 或 Domain Reload 期间短暂不可用

导入资源、修改脚本或安装 Package 会触发 Refresh、编译和 Domain Reload。此时 MCP 调用可能暂时失败或连接重建。正确处理方式是等待 `ready_for_tools=true`，而不是连续发送场景写操作。恢复后首先重新确认活动场景和 Console，再继续任务。

### Console 状态与 MCP 自身告警混在一起

Console 可能同时包含项目编译错误、运行时警告和 MCP 传输警告。排查时应按类型、时间和来源区分：

- `Assets/` 下脚本或资源导入错误通常属于项目问题；
- `Library/PackageCache/com.coplaydev.unity-mcp` 下的堆栈通常属于 MCP 连接层；
- 即使判断 MCP Warning 无害，也要确认后续只读调用和目标写操作确实成功。

### 查询结果过大或信息不清晰

场景层级、组件属性、Console 和资源搜索可能返回大量数据。优先使用分页和较小页大小；组件查询先使用 `include_properties=false`，再对少量目标读取完整属性。场景修改前以稳定的对象路径或实例标识重新定位目标，避免依赖被截断的输出。

## 本次项目中的资源副作用经验

### 动态字体资产会被 Unity 写回

`Assets/Font/MSYH_UITK.asset` 使用 TextCore 动态图集。Unity 实际渲染此前未收录的汉字时，会把字形表、字符表和图集像素写回 `.asset`，因此一次 UI 预览也可能产生较大的 Git 差异。

判断是否合理时应检查：

- 源字体 GUID、Atlas Population Mode 和 Multi Atlas 设置是否保持不变；
- 差异是否主要是新增字符、Glyph 和 Atlas 数据；
- 新增字符是否来自本次或现有界面文本。

合理的字形扩充可以提交；若字体配置本身意外变化，应单独排查，不能把整个资产变化都视为普通噪声。

### 安装或升级 MCP 会修改 Package 文件

UnityMCP 安装会修改 `Packages/manifest.json` 和 `Packages/packages-lock.json`，并可能带来间接依赖。提交前应确认 JSON 有效、Manifest 与 Lock 同步，并检查是否夹带无关的 Package 升级。

`Library/PackageCache/` 是本地缓存，不应提交或直接修改。若必须修复 UnityMCP 本身，应通过明确版本、上游更新或 Package 管理流程处理。

### Refresh 可能生成 `.meta` 和导入设置差异

新增图片、材质、脚本或场景对象后，Refresh 会产生真实 `.meta`，资源导入器也可能更新序列化设置。提交前逐项检查这些变化，尤其关注纹理 sRGB、Wrap Mode、Mipmap、最大尺寸，以及场景是否保存了目标之外的对象状态。

## 场景与视觉修改经验

- 写场景前确认活动场景路径，写完后显式保存；不要假定工具成功响应等于场景已经落盘。
- 修改 GameObject 前先读取组件和 Transform，保留用户已有的未关联调整。
- 在 Play Mode 中产生的对象和属性变化通常不会成为可靠的场景资产修改。正式编辑应在 Edit Mode 完成。
- 布局修改要同时验证极限状态。本项目至少包括 4 行牌河、14 张手牌、4 个杠副露、13 张听牌提示、最多动作按钮和各独立 `UIDocument` 的输入归还。
- 画面截图只能证明当时的视觉状态，不能代替 3D 手牌点击、弹窗关闭后输入穿透和网络状态恢复测试。
- 登录、匹配和进入对局由用户手动完成更稳定；用户进入目标运行场景后，再通过 MCP 截图、查看 Console 和执行视觉验收。

## 提交前检查清单

- Unity 实例与目标项目一致，活动场景正确。
- Editor State 为 Ready，未在编译、Refresh 或切换 Play Mode。
- Unity Console 中没有未解释的 Error；Warning 已按来源和影响判断。
- 需要保存的 Scene、Prefab 和资产均已落盘。
- 新 Unity 资产的 `.meta` 均由编辑器生成，GUID 未被人工改写。
- `manifest.json` 与 `packages-lock.json` 同步且 JSON 有效。
- 动态字体、材质、纹理导入器等自动变化已经逐项审查。
- `git diff --check` 通过，未混入 `Library/`、`tmp/`、本机记忆或截图缓存。
- 相关 focused regression 与必要的完整纯 C# 回归通过。
- Unity Game 视图中的视觉和跨面板点击验收已完成，或在交付说明中明确标记为待人工验收。
