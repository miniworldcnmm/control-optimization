# 右摇杆自由光标与主机化实机玩法

- **产品生命周期**：`active`
- **关联代码**：`Patches/RightStickCursorPatches.cs`、`Patches/ToolUsePatches.cs`、`Patches/SlingshotAimPatches.cs`、`Patches/FurniturePlacementPatches.cs`、`Patches/ItemStowPatches.cs`、`Patches/CarpenterMenuPatches.cs`（家具防抖与床弹回修复）、`Patches/InventoryManagementPatches.cs`（掉落保护）、`ModEntry.cs`、`ModConfig.cs`

## 1. 目的和用户可见行为

补齐 Android 大世界实机游玩与主机版的核心操控差距，使工具使用、瞄准、摆放、收纳与物品丢弃均符合 Switch/主机习惯：

1. **右摇杆自由光标**：拨动右摇杆在大世界移动屏幕光标，可精准瞄准工具挥动（含斜角格）、地块交互（箱子、邮箱、检视、抚摸未抚摸动物）及家具/机器/种子摆放；无输入约 4 秒后自动淡出，工具与工具命中框同步回退至角色面朝格；再次拨动从上次位置继续，不跳回屏幕中央。关闭开关时归零大世界右摇杆防止漂移。
2. **移动中蓄力工具**：按住升级版（铜及以上）锄头或水壶时允许移动并正常蓄力范围效果，松手触发蓄力释放；轻点仍执行单格使用。基础工具、镐与斧头不受影响。
3. **主机风格弹弓直瞄**：仅由物理工具键驱动弹弓——按住拉弓并用左摇杆直接瞄准，松手发射一发，非拉弓时左摇杆正常走位，消除 Android 触控层劫持导致的移动即发射或连发问题。
4. **单格摆放预览与家具防抖/防弹回**：将 Android 易混淆的多格绿色可放区域替换为主机式单格红/绿目标框；家具渲染半透明实体预览，合成物（洒水器、小桶等）与种子/肥料仅显示精准单格目标框；限制 Y 键对家具的交互频率（~500ms 冷却）避免连触发，并修复床铺放置后弹回背包的缺陷。
5. **物品收纳（Item Stowing）与防连发**：在配置菜单暴露 Android 隐藏的 `Options.stowingMode`（关闭 / 仅手柄 / 开启），且每次按住工具键只执行一次收起或取出，修复 Android 按键连发导致的反复闪烁收放。
6. **掉落防秒捡**：玩家主动丢弃物品时写入 `Debris.DroppedByPlayerID`，重新激活引擎原生 ~1.2 秒防自拾取延迟，不影响怪物掉落与作物收获。

## 2. 架构与主要调用流程

### 2.1 右摇杆大世界光标（`RightStickCursorPatches` + `GameplayButtonPatches` + `ModEntry`）

- **解除输入饥饿**：开启 `EnableRightStickCursor` 且未装备弹弓时，`GameplayButtonPatches` 放行大世界右摇杆，使引擎原生 `Game1.UpdateControlInput` 移动鼠标位置并刷新 `timerUntilMouseFade`（~4000ms）。
- **防止回中与声明活跃指针**：
  - `Game1.setMousePositionRaw` Postfix（以及 `OnUpdateTicked` 中的幂等兜底 `EnforceTick`）在右摇杆有输入时重新断言 `Game1.lastCursorMotionWasMouse = true`，阻止淡出后被引擎重置回屏幕中央。
- **交互与工具瞄准重定向**（均限定在本地玩家、大世界无菜单/事件、未装备弹弓且 `timerUntilMouseFade > 0`）：
  - `Game1.pressActionButton` Prefix：在交互判定前恢复 `lastCursorMotionWasMouse = true`。
  - `Character.GetLocationNextToWhereYoureFacing(int)`（Android 独有方法，字符串反射 Patch）Postfix：返回光标所在世界像素坐标，使工具与手持物摆放瞄准光标格。
  - `Character.GetToolLocation(bool)` Prefix：去除 `isAnyGamePadButtonBeingHeld()` 对点击坐标的忽略，支持斜角格工具命中。
  - `Character.GetToolLocation(Vector2, bool)` Prefix：当 `!Game1.wasMouseVisibleThisFrame`（光标已淡出）时强制 `ignoreClick = true`，使“始终显示工具命中位置”方框与实际命中面朝格保持一致。
- **自绘情境光标**（`ModEntry.OnRenderedHud` → `RightStickCursorPatches.DrawCursor`）：
  - Android 引擎大世界不绘制鼠标精灵；补丁在 `timerUntilMouseFade > 0` 时读取 `Game1.isActionAtCurrentCursorTile`、`isSpeechAtCurrentCursorTile`、`isInspectionAtCurrentCursorTile` 以及当前场景未抚摸 `FarmAnimal` 碰撞箱，映射为手型（`cursor_grab`）、对话（`cursor_talk`）、放大镜（`cursor_look`）或默认指针并按 `mouseCursorTransparency` 绘制。

### 2.2 移动中蓄力工具（`ToolUsePatches`）

- 针对 `UpgradeLevel >= 1` 的 `Hoe` / `WateringCan`，Patch `Farmer.FireTool`、`Game1.pressUseToolButton` 与 `Game1._mobileUpdateControlInput`：
  - **阶段 1**：利用 `_holdActive` 与 `_allowTick` 确保单次物理按住工具键仅放行首次触发，吞掉 Android 逐帧自动连发导致的重复起手与蓄力清零；松手由 `GameplayButtonPatches.FinalizeToolHeldTracking` 回调 `OnUseToolReleased()` 结束会话。
  - **阶段 2**：在 `_mobileUpdateControlInput` 后置中恢复蓄力按住态，避免触控移动清理逻辑在走位时打断蓄力递增。

### 2.3 弹弓直瞄（`SlingshotAimPatches`）

- Postfix `Game1._mobileUpdateControlInput`：在装备弹弓时用 `GameplayButtonPatches` 的物理工具键状态与边沿（`GameUseToolHeld` / `PressedEdge` / `ReleasedEdge`）驱动引擎使用标志，并强制关闭传统反向瞄准（`useLegacySlingshotFiring`），同时 `GameplayButtonPatches` 归零右摇杆避免干扰。

### 2.4 摆放预览与家具防抖（`FurniturePlacementPatches` + `CarpenterMenuPatches`）

- `FurniturePlacementPatches`：Patch `StardewValley.Object.drawPlacementBounds` 与相关绘制路径，在手柄模式（`weaponControl` 不满足触控门控时）对家具强制走单框 + 半透明家具预览，对可摆放合成物（`EnableConsoleCraftablePlacement`）走单格预览框，对种子与肥料（Category `-19`）仅绘制单格红/绿框。
- `CarpenterMenuPatches`：Patch `GameLocation.removeQueuedFurniture` 修复床铺放置后被移除弹回背包的问题，并在工具键触发家具交互后启用防抖抑制，由 `ModEntry.OnUpdateTicked` 在松键/冷却后清理。

### 2.5 物品收纳与防连发（`ModEntry` + `ItemStowPatches`）

- `ModEntry.ApplyItemStowing()` 在 `SaveLoaded` 及 GMCM 保存时将 `Config.ItemStowing` 同步写入 `Game1.options.stowingMode`。
- `ItemStowPatches`：Patch `Game1.pressUseToolButton`（Prefix/Postfix），通过 `__state` 记录调用前后 `Farmer.netItemStowed.Value` 是否变化；一旦单次调用切换了收纳状态且 `Buttons.X` 仍被按住，即置位 `_heldSinceToggle = true` 吞掉本次按住期间的后续自动连发，直至 `ItemStowPatches.Tick()` 检测到工具键松开才复位。纯工具挥动不改变收纳状态，完全不受影响。

### 2.6 掉落防秒捡（`InventoryManagementPatches`）

- 当 `EnableConsoleDropBlocker` 开启时，主动丢弃物品逻辑通过 `TagAsPlayerDrop` 标记 `Debris.DroppedByPlayerID`，交由引擎自带拾取延迟机制生效。

## 3. 配置与持久化

- `EnableRightStickCursor`（默认 `true`）
- `EnableMoveWhileCharging`（默认 `true`）
- `EnableSlingshotAim`（默认 `true`）
- `EnableConsoleFurniturePlacement`（默认 `true`）
- `EnableConsoleCraftablePlacement`（默认 `true`）
- `EnableFurnitureDebounce`（默认 `true`）
- `EnableConsoleDropBlocker`（默认 `true`）
- `ItemStowing`（`"Off"` / `"GamepadOnly"` / `"Both"`，默认 `"Off"`）

## 4. 并发、状态与失败语义

- 所有大世界钩子严格限定 `Game1.activeClickableMenu == null`（及必要时的 `!Game1.eventUp`），防止污染菜单交互。
- 右摇杆情境光标诊断 `DiagnosticTick()` 通过 `_lastCtxKey` 状态指纹去重，仅在悬停语义变化且 `VerboseLogging == true` 时输出单条日志。
- `RightStickCursorPatches` 中每个补丁方法均独立包裹 `try/catch`，异常时自动退回原版面朝格行为。

## 5. 验证方式

- 右摇杆移动光标到斜角地块挥动工具、交互箱子/未抚摸动物、放置家具，并静置 >4 秒确认光标与工具命中框同步回退至面朝格。
- 使用铜级及以上锄头/水壶边走边按住工具键，确认蓄力逐级递增并在松手时释放范围效果。
- 装备弹弓移动、按住拉弓用左摇杆瞄准、松手发射，确认无自动连发或移动卡锁。
- 将 `ItemStowing` 设为 `GamepadOnly` 或 `Both`，手持不可放置物品长按工具键 6+ ticks，确认只收起一次而非反复收放（可用 `InputSimulator` 配合验证）。
- 从背包丢弃物品，确认不会立刻吸回，约 1.2 秒后可正常捡起。

## 6. 已知限制与防回归说明

- **严禁在渲染帧中调用 `Utility.checkForCharacterInteractionAtTile`**：该引擎辅助方法在每帧遍历 NPC 并附带副作用，曾实测导致工具栏切格卡顿与放置预览抖动；因此右摇杆情境指针明确不探测普通村民对话/送礼气泡。
- **`ItemStowPatches` 依赖实际状态变化判定**：仅在 `netItemStowed.Value` 发生变化时才吞掉连发，因此不得将其泛化为吞掉所有 `pressUseToolButton` 重复调用，以免破坏普通工具连续使用逻辑。
- **弹弓与右摇杆互斥**：装备弹弓期间必须保持右摇杆归零且 `RightStickCursorPatches` 跳过生效，确保与主机版一致。
