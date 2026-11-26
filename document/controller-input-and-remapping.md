# 手柄映射、全局快捷键与启动修复

- **产品生命周期**：`active`
- **关联代码**：`Patches/GameplayButtonPatches.cs`、`ButtonRemapper.cs`、`Patches/BootDiagnosticPatches.cs`、`ModEntry.cs`、`ModConfig.cs`

## 1. 目的和用户可见行为

为 Android 版《星露谷物语》提供统一、可控的手柄按键映射与底层输入治理，同时补齐主机版常用全局快捷操作与 Android SMAPI 启动稳定性修复：

1. **手柄布局与操控风格映射**：支持 Switch/Odin、Xbox、PlayStation 三种物理按键布局，以及 Switch 风格（右确认/下取消）与 Xbox/PS 风格（下确认/右取消）两种操控风格；允许关闭重映射以恢复原版透传。
2. **Start 键短按/长按分流**：正常游玩时短按 Start 打开游戏菜单（保持原版行为），长按约 0.5 秒（30 ticks）直接打开任务日志（Quest Log）。
3. **过场动画双击跳过**：在可跳过的过场剧情中，第一次按 Start 触发模拟触屏显示原生跳过图标，3 秒（180 ticks）内再次按 Start 跳过剧情。
4. **SMAPI 启动白屏自愈**：当 Android SMAPI 启动阶段因内存偏移误读导致 `TitleMenu.fadeFromWhiteTimer` 卡死时，自动检测并强制推进倒计时，避免永久白屏。

## 2. 架构与主要调用流程

### 2.1 `GamePad.GetState` 底层输入管线（`GameplayButtonPatches`）

同时 Patch `GamePad.GetState(PlayerIndex)` 与 `GamePad.GetState(PlayerIndex, GamePadDeadZone)`（通过 `_inOneParamGetState` 防止重入），仅处理 `PlayerIndex.One`，并按 `Game1.ticks` 缓存当帧计算结果以避免帧内状态抖动：

1. **注入测试输入**：优先调用 `InputSimulator.Apply(__result)` 混入脚本化按键。
2. **采集原始状态**：缓存 `RawStartPressed`、`StartPressedThisTick`、`RawRightStickX/Y`、`RawLeftStickX/Y`、`RawLeftTriggerButton/RightTriggerButton`，并将数字扳机折算进有效扳机值 `RawLeftTrigger/RightTrigger = Math.Max(analog, digital ? 1f : 0f)`，确保 Switch Pro 等纯数字扳机可正常驱动工具栏状态机。
3. **上下文冲突轴/键归零**：
   - 商店购买页（`ShopMenuPatches.ShouldSuppressRightStick()`）：归零右摇杆，改由 `ShopMenuPatches` 读取 `RawRightStickY`。
   - 社交分页（`GameMenuPatches.ShouldSuppressRightStickForSocial()`）：归零右摇杆 Y 轴，防止触发平滑滚轮。
   - 大世界右摇杆（`EnableRightStickCursor == false` 或当前持有 `Slingshot`）：归零右摇杆，防止光标漂移或干扰弹弓左摇杆瞄准。
   - 选项分页（`OptionsPagePatches.ShouldSuppressLeftStick()`）：归零左摇杆，防止自由光标跑出列表。
   - 主机工具栏生效期间（`ShouldSuppressTriggers()`）：同时归零模拟扳机与数字 `Buttons.LeftTrigger / RightTrigger`，避免原版切格与 Mod 逻辑叠加。
4. **按键重映射**：
   - **A/B 交换**（全场景生效）：当物理布局（Switch vs Xbox/PS）与目标操控风格不一致时交换。
   - **X/Y 交换**（按场景区分，钓鱼 `BobberBar` 视为游玩场景）：Switch 布局在游玩时交换 X/Y、菜单中保持原始值；Xbox/PS 布局在菜单中交换 X/Y、游玩时保持原始值，使左侧键始终承担“使用工具/堆叠”，上方键始终承担“打开制作/整理”。
   - `ButtonRemapper.Remap(...)` 目前为向后兼容的透传层，实际映射已统一前移至 `GetState` 阶段。
5. **后置抑制与工具键边沿追踪**：
   - `ApplyButtonSuppression`：处理 `SuppressAUntilRelease`（如关闭箱子后防误触直至松手）以及在可跳过事件或启用 Start 长按日志时抑制逻辑 `Start`。
   - `FinalizeToolHeldTracking`：基于最终游戏侧 `Buttons.X` 计算 `GameUseToolHeld`、`GameUseToolPressedEdge`、`GameUseToolReleasedEdge`，并在下降沿通知 `ToolUsePatches.OnUseToolReleased()`。

### 2.2 Start 全局操作（`ModEntry.OnUpdateTicked`）

- 因为 `Start` 在 `GetState` 层被抑制，SMAPI `OnButtonsChanged` 无法感知，由 `ModEntry.OnUpdateTicked` 轮询 `GameplayButtonPatches.RawStartPressed` 与 `StartPressedThisTick`：
  - **任务日志**：在 `EnableJournalButton` 开启且自由游玩状态（`activeClickableMenu == null && Context.IsPlayerFree`）下，记录按下帧 `_startPressedTick`，按住满 `StartHoldThreshold = 30` ticks 时经反射兼容调用 Android `QuestLog(int)` 或 PC 无参构造器打开日志；未达阈值即松开则打开 `GameMenu`。
  - **过场跳过**：通过反射访问 `Event.skippable`、`Event.skipped`、`Event.skipEvent()`；首按设置 `GameplayButtonPatches.SimulateTouchClickOnTick = currentTick` 触发原生跳过图标，180 ticks 内二按直接执行跳过。

### 2.3 启动白屏自愈（`BootDiagnosticPatches`）

- Postfix `TitleMenu.update(GameTime)`：反射读取 `fadeFromWhiteTimer`、`pauseBeforeViewportRiseTimer`、`logoFadeTimer`；当检测到白屏淡出计时器连续卡住不动时主动强制递减直至进入标题界面，仅运行至恢复完成后停止。

## 3. 配置与持久化

相关配置项位于 `ModConfig.cs` 并注册于 GMCM：
- `EnableButtonRemapping`（默认 `true`）
- `ControllerLayout`（`Switch` / `Xbox` / `PlayStation`，默认 `Switch`）
- `ControlStyle`（`Switch` / `Xbox`，默认 `Switch`）
- `EnableJournalButton`（默认 `true`）
- `EnableCutsceneSkip`（默认 `true`）
- `VerboseLogging`（默认 `false`，开启后在每次物理按键边沿输出 `[BtnMap]` 原始/映射状态日志）

GMCM 内的「键位对照」由 `ModEntry.GetControlsDisplay()` 根据手柄布局与操作风格生成。保存后重开菜单可刷新键位，也可随兼容版本的语言预览一起刷新。PlayStation 符号会根据当前字体回退为按键文字。详见[中英文切换与本地化](localization-and-language-switching.md)。

## 4. 状态与失败语义

- `GetState` 帧缓存通过 `InvalidateCache()` 支持帧内状态变更后即时失效（如 `SuppressAUntilRelease` 置位时）。
- `GetState_Postfix` 内层包裹 `try/catch` 静默保护，防止逐帧异常刷屏或中断引擎输入轮询。
- `OpenQuestLog()` 与过场跳过反射调用均有异常捕获并记录错误日志，不会因签名差异导致游戏崩溃。

## 5. 验证方式

- 切换不同 `ControllerLayout` 与 `ControlStyle` 组合，开启 `VerboseLogging` 检查 `[BtnMap]` 日志确认 `RAW` 到 `GAME` 的映射符合预期，并在大世界、背包、钓鱼 `BobberBar` 中验证 A/B/X/Y 行为。
- 自由游玩下短按 Start 应打开 `GameMenu`，长按 ≥0.5 秒应打开 `QuestLog` 且松手后不会叠加打开 `GameMenu`。
- 可跳过剧情中按两次 Start 应在首按出现跳过图标、3 秒内二按成功跳过。
- 使用纯数字扳机手柄（如 Switch Pro）验证 `RawLeftTrigger` / `RawRightTrigger` 在按压时达到 `1.00`。

## 6. 已知限制与防回归说明

- **同帧一致性不可破坏**：移除 `_cachedState` 帧缓存会导致单帧内多次 `GetState` 调用读取不一致状态，引发蓄力工具误触发单次挥动。
- **数字扳机折算与归零必须成对维护**：若仅抑制模拟轴而遗漏 `Buttons.LeftTrigger / RightTrigger`，纯数字扳机将穿透至原版 `pressSwitchToolButton` 并导致换行后卡格。
- **Start 抑制不可移除**：`Game1.UpdateControlInput` 直接读取 `GamePadState`，若不在 `GetState` 层屏蔽 `Start`，按下瞬间即会打开菜单，导致长按打开日志与双击跳剧情失效。
