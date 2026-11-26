# 开发诊断与无手柄脚本测试工具

- **产品生命周期**：`active`
- **关联代码**：`Patches/InputSimulator.cs`、`ModEntry.cs`、`Patches/GameplayButtonPatches.cs`、`tools/seed-boiler-room.ps1`、`tools/seed-dwarf-test.ps1`、`tools/dialogue-outline-color-picker.html`

## 1. 目的和用户可见行为

为开发者在缺少物理手柄的 Android 设备（如仅通过 adb 连接的手机/平板）或特定剧情场景下复现与验证手柄补丁行为提供零侵入辅助能力：

1. **无手柄脚本化输入模拟器（`InputSimulator`）**：
   - 运行期间每秒检查 Mod 安装目录下是否存在 `sim-input.txt`；若文件不存在，则完全不产生任何游戏副作用。
   - 若存在 `sim-input.txt`，立即读取并删除该文件，将指令入队逐条执行，并把模拟按键直接注入 `GamePad.GetState` 原始输入层（先于所有映射与抑制逻辑，与真实物理手柄输入等价），同时在模拟按键按住期间输出 `pressUseToolButton` 与 `CanPlayerStowItem` 的诊断日志。
2. **测试存档注入脚本（`tools/`）**：
   - `tools/seed-boiler-room.ps1`：自动备份原存档后，向玩家背包注入原版社区中心锅炉房全部 3 个献祭包所需物品并保留工具/装备，用于快速复现献祭完成与奖励盒场景。
   - `tools/seed-dwarf-test.ps1`：自动备份原存档后，向玩家背包注入炸弹与 4 张矮人卷轴（I–IV），用于快速复现博物馆捐赠与《矮人语教程》奖励领取场景。
3. **对话选框配色预览页（`tools/dialogue-outline-color-picker.html`）**：
   - 本地静态 HTML 工具，用于可视化调试主机风格对话选项框的边框 RGB 颜色并复制 `rgb(...)` 值。
4. **按需诊断日志体系（`VerboseLogging`）**：
   - 默认关闭；开启后输出边沿触发或变更节流的诊断标签（如 `[BtnMap]`、`[SPDiag]`、`[ToolIdx]`、`[StartHold]`、`[RStickCtx]`、`[InputSim]` 等），兼顾排障信息量与 Android I/O 性能。

## 2. 架构与主要调用流程

### 2.1 `InputSimulator` 指令格式与生命周期

- **初始化（`ModEntry.Entry`）**：
  - 调用 `InputSimulator.Init(helper, Monitor)`，计算脚本路径 `Path.Combine(helper.DirectoryPath, "sim-input.txt")`，并使用独立 Harmony ID `<ModID>.InputSim` 对 `Game1.pressUseToolButton` 与 `Game1.CanPlayerStowItem` 挂载仅在 `_button != null` 时输出日志的诊断 Postfix。
  - 挂载 `OneSecondUpdateTicked → InputSimulator.Poll()` 与 `UpdateTicked → InputSimulator.Tick()`。
- **底层注入（`GameplayButtonPatches.GetState_Postfix`）**：
  - 在采集原始按键、摇杆、扳机和执行映射之前，调用 `__result = InputSimulator.Apply(__result)` 将当前活跃模拟按键按位 OR 合入 `GamePadButtons` 与 `GamePadDPad`。
- **支持的 `sim-input.txt` 文本指令（每行一条，空白分隔）**：
  - `<BUTTON> [ticks]`：按住指定按键（默认 `6` ticks，释放后间隔 `6` ticks 再执行下一条）。支持按钮名（不区分大小写）：`A`、`B`、`X`、`Y`、`LB`、`RB`、`Start`、`Back`、`Up`、`Down`、`Left`、`Right`。
  - `slot <N>`：将 `Game1.player.CurrentToolIndex` 设为 `N` 并记录当前手持物品与收纳状态。
  - `item <id>`：创建普通物品 `(O)<id>` 放入首个空背包槽位（满包时替换当前槽）并选中它。
  - `wait <ticks>`：等待指定游戏帧数。
  - `state`：立即输出当前手持物品、`netItemStowed` 与 `stowingMode` 状态日志。

### 2.2 `tools/` 存档注入脚本流程

- 两个 PowerShell 脚本均接受必填参数 `-SavePath "<存档文件路径>"`，使用 `System.Xml.XmlDocument`（`PreserveWhitespace = $true`）安全编辑单行压缩格式的 Android 存档 XML：
  1. 校验路径存在；
  2. 若同目录尚无 `.before-boiler-seed` / `.before-dwarf-seed` 备份则自动备份；
  3. 修改 `/SaveGame/player/items` 并将槽位补齐至 36 格（同步更新 `/SaveGame/player/maxItems`）；
  4. 保存修改后的存档。

## 3. 配置与持久化

- `VerboseLogging`（`ModConfig.cs`，默认 `false`）：控制常规诊断日志输出。
- `InputSimulator` 无需配置开关，完全由 `sim-input.txt` 文件是否存在触发，读取后自动删除，不持久化状态。

## 4. 安全与失败语义

- `InputSimulator.Poll()` 读取或删除文件遇到 `IOException` 时记录警告并放弃本次触发，不会阻塞或崩溃主循环。
- 存档注入脚本**仅应用于专门的测试存档**，切勿直接对正式主力存档执行（即使脚本自带备份，仍应手动留存副本）。
- **隐私与安全边界**：测试产生的日志、截图、设备存档同步数据属于 `.gitignore` 忽略内容，严禁提交到仓库，也严禁在文档中记录本机绝对路径或设备标识。

## 5. 验证方式

- 向设备 Mod 目录推送包含 `item 128`、`Y 6`、`state` 等指令的 `sim-input.txt`，确认文件在 1 秒内被读取删除，且 SMAPI 日志按序打印 `[InputSim]` 步骤与 `pressUseToolButton` / `CanPlayerStowItem` 结果。
- 对副本测试存档运行 `pwsh -NoProfile -File tools/seed-boiler-room.ps1 -SavePath <copy>`，确认自动生成备份文件且存档可被游戏正常加载。

## 6. 已知限制与防回归说明

- `InputSimulator` 当前仅模拟数字按钮与方向键，不模拟模拟摇杆轴值或模拟扳机压力值。
- `InputSimulator.Apply` 必须保持在 `GameplayButtonPatches.GetState_Postfix` 中所有缓存、映射与抑制逻辑之前执行，才能真实复现物理手柄进入管线后的完整行为。
