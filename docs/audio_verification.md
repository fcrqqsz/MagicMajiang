# 客户端声音设置与播放验证

声音是客户端表现，不进入 WebSocket 协议、Room、构筑或服务端存档。配置按本机保存，其他玩家不受影响。

## 组成与维护

- `00_Persistent/AudioManager` 持有 LobbyBGM、BattleBGM、SFX 三个 2D 音源；`ClientAudio.mixer` 为 Master 下的 Music、SFX 两组，公开参数为 `MasterVolume`、`MusicVolume`、`SfxVolume`。
- AudioManager 在 Start 应用用户音量后才播放；播放源负责过渡，Mixer 负责用户音量。主音量或分类音量为零时额外 mute 音源，BGM 不停止计时。
- 登录、大厅、房间等待共用大厅曲；对战、小局结果、中场备牌及最终结果共用对战曲。激活目标场景决定音轨，卸载时回退到仍加载的业务场景。同曲请求不重启，跨类别用 1 秒交叉淡变，新类别从头开始。
- 两首 BGM 保留原 MP3、采样率、立体声和压缩质量，导入改为 Streaming、Load In Background，关闭预加载。现有短天赋音效保留预加载，通过统一 SFX 音源播放。
- 唯一 AudioListener 位于常驻 MainCamera；CameraManager 只切换 Camera.enabled，不停用其宿主。03_Game 不再单独配置监听器或天赋音源。
- `AudioPreferences` 管理立即生效及 0.5 秒防抖保存；设置退出、应用暂停和退出补存。PlayerPrefs 键为 `SuperMajiang.Audio.v1.Master`、`Music`、`Sfx`，值范围 0..1，默认 0.8 / 0.6 / 1。与 profile.json 及服务器地址选择互不影响。
- `AudioSettingsView` 独立绑定大厅原有 UIDocument；每次启用/禁用成对订阅/解除订阅，初始化使用 SetValueWithoutNotify。
- 实际 Dedicated Server 构建禁用 AudioManager，服务器入口场景本身不引用任何客户端音频。编辑器同时定义 UNITY_SERVER 和 UNITY_EDITOR 时仍允许从 00_Persistent 测试客户端；不要为声音改动服务器构建入口。

## 自动验证命令

```powershell
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- audio
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore -- connection-settings
dotnet run --project Tests/NetworkRegression/NetworkRegression.csproj --no-restore
```

音频回归覆盖默认值、恢复、零值、非法值、增益换算、保存防抖与失败重试、场景选择、重复请求及过期播放请求取消。它不替代实际 AudioSource、混音和 UI 交互验收。

## 验收状态与后续回归

2026-09-03：纯 C# focused 与完整回归、Unity 编译及音频运行检查通过，用户已确认人工验收无问题。UnityMCP 检查覆盖混音参数、静音时钟、同曲连续性、快速反向淡变、timeScale=0 下过渡、音轨循环以及滑条保存和布局；真实玩法体验以用户人工验收为准。

后续修改声音或设置页时，使用以下项目回归：

- 实际拖动、键盘调整、试听和恢复默认值；音效不被 BGM 掩盖，静音和恢复没有突响。
- 听完或跳至两首曲目末尾，确认首尾静音、尾奏或接缝是否符合预期；循环机制不会自动裁剪原曲。
- 大厅进入真实对局、结算、下一小局、中场备牌、断线恢复和返回大厅；同类阶段不断曲，切换后不残留旧音乐。
- 小窗口下声音设置、百分比和连接诊断可完整查看，纵向滚动不影响现有连接控制；反复切页后按钮只触发一次。
- 完全重启客户端后音量恢复，已选择静音时启动不会先播放最大音量。
