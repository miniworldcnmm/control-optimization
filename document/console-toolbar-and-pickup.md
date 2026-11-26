# 主机风格工具栏与拾取入当前行

- **产品生命周期**：`active`
- **关联代码**：`Patches/ToolbarPatches.cs`、`Patches/FarmerPatches.cs`、`Patches/CarpenterMenuPatches.cs`（家具拾取分支）、`ModEntry.cs`、`ModConfig.cs`

## 1. 目的和用户可见行为

取代 Android 原版连续滚动且易误触的侧边/底部工具栏，提供与主机版一致的 12 格固定单行工具栏体验：

1. **12 格固定行显示**：一次展示当前行全部 12 个槽位，默认居中停靠屏幕底部；当角色移动到屏幕下半区时自动切换至屏幕左上角以避让角色与右上角时钟。
2. **换行与行内切格**：
   - 标准模式：**LB / RB** 循环切换工具栏行（随背包容量支持 1～3 行），**LT / RT** 在当前 12 格内循环左右移动。
   - 肩键替代模式（`UseBumpersInsteadOfTriggers`）：**十字键 上/下** 切换行，**LB / RB** 或 **十字键 左/右** 在行内移动（适配 Android 蓝牙 Xbox 手柄等无法识别扳机的场景）。
3. **缩放与边距适配**：响应游戏原生“工具栏格子大小（Toolbar Slot Size）”滑块（最小 32px，自动按屏幕宽度扣减右下角体力/血条安全区 `HudSafeMarginX = 130` 限幅），并将原版“工具栏边距（Toolbar Padding）”滑块（0～160）复用为靠边停靠间距（折半映射为 0～80px）。
4. **拾取物品优先入当前行**：新获得的采集物、掉落物、礼物、商店购入物等若当前查看行有空位，则优先落入当前行而不堆积在第 0 行；非家具拾取绝不改变当前选中槽位，家具拾取后则同步切行并自动选中，便于立即重新摆放。

## 2. 架构与主要调用流程

### 2.1 工具栏自绘与兼容修补（`ToolbarPatches`）

- **替换 `Toolbar.draw(SpriteBatch)`**（Prefix 返回 `false`）：
  - 每次绘制时将 Android 独有字段 `Toolbar._itemSlotSize` 置为 `0`，防止 `DialogueBox.GetWidth()` 误判存在竖向侧边工具栏而在小屏上把对话框文字挤压成极窄条。
  - 通过反射读取 `Options.toolbarSlotSize`（`ResolveSlotSize`）与 `Game1.toolbarPaddingX`（`ResolvePadding`）计算实际尺寸与停靠边距。
  - 绘制单个槽位物品时（`DrawSlotItem`），临时设置 `Item._itemSlotSize = slotSize` 与 `Item.drawInToolbar = true` 并在 `finally` 中恢复，确保堆叠数字、品质星标和缩放比例随格子大小正确对齐。
- **水壶水量条修复**（`WateringCan.drawInMenu` Prefix/Postfix）：
  - Android 原版水壶水量条偏移量依赖全局 `Options.toolbarSlotSize`，在工具栏/背包/箱子不同槽位尺寸下容易错位。补丁使用 `[ThreadStatic]` 临时将其替换为当前物品自身的 `_itemSlotSize`，绘制完毕后立即还原。

### 2.2 行锁定与扳机防抖状态机（`ModEntry` + `FarmerPatches`）

1. **`OnSaveLoaded`**：切格再切回以强制完成读档后的首次工具装备，并同步 `currentToolbarRow = original / 12`。
2. **`OnUpdateTicking`（帧前）**：
   - 无条件快照 `_preUpdateToolIndex = player.CurrentToolIndex`，作为换行后首次扳机按压的干净基线，避免叠加原版 `pressSwitchToolButton` 的位移。
   - 若 `_triggerSlotTarget >= 0`，在 `Game.Update` 前强制回写 `CurrentToolIndex`。
3. **`OnButtonsChanged`（按钮事件）**：
   - 按下肩键或十字键左右时清除 `_triggerSlotTarget`；但在标准模式按下 LB/RB 换行后立即将 `_triggerSlotTarget` 重新武装为新行目标槽位，防止换行后的第一次扳机触发 +2 跳格。
   - 标准模式下显式 `Suppress` SMAPI 层的 `LeftTrigger / RightTrigger`。
4. **`HandleTriggersDirectly`（`OnUpdateTicked` 帧后）**：
   - 读取 `GameplayButtonPatches.RawLeftTrigger / RawRightTrigger`（含数字扳机折算）。
   - 采用**双阈值滞回 + 连续释放确认**状态机：`TriggerPressThreshold = 0.5f`、`TriggerReleaseThreshold = 0.15f`、`TriggerReleaseConfirmTicks = 2`，吸收霍尔效应扳机按压途中的短暂掉零同时保留快速连按响应。
   - 执行后检查并强制 `CurrentToolIndex` 留在 `currentToolbarRow` 对应的 12 格范围内。
5. **`FarmerPatches.CurrentToolIndex_Prefix`**：
   - 兜底拦截 Android 原版硬编码的 0～9 循环（例如负数回绕修正为当前行末格、0 行 9→0 修正为 10、0→9 修正为 11、非 0 行 0～9 重映射到当前行）。

### 2.3 拾取入当前行（`FarmerPatches` + `CarpenterMenuPatches`）

- **非家具物品**（`FarmerPatches.AddItemToInventory_Postfix`，Hook `Farmer.addItemToInventory(Item, List<Item>)`）：
  - 若物品未被合并进现有堆叠且落在了非当前行，在当前行 `[rowStart, rowStart + 12)` 寻找首个空位迁入，保持 `CurrentToolIndex` 与当前行不变；若当前行已满则保留原版落点。
- **家具物品**（`CarpenterMenuPatches` 的 `RemoveQueuedFurniture` 路径）：
  - 同样受 `EnablePickupToActiveRow` 控制：当前行有空位则迁入并自动选中；当前行已满时跟随游戏实际落点切换可见行（调用 `ModEntry.SetCurrentToolbarRow`）并选中该家具。

## 3. 配置与持久化

- `EnableConsoleToolbar`（默认 `true`）
- `EnablePickupToActiveRow`（默认 `true`）
- `UseBumpersInsteadOfTriggers`（默认 `false`）
- `VerboseLogging`（默认 `false`；开启后启用 `[SPDiag]` 行失步与按键诊断、`[ToolIdx]` 索引变更堆栈追踪，以及按 1 位小数变更节流的原始扳机值日志）

## 4. 状态与失败语义

- 所有 Android 独有反射字段（`toolbarSlotSize`、`_itemSlotSize`、`drawInToolbar`、`toolbarPaddingX`）均有空值与异常兜底，解析失败时回退至默认 64px 槽位和 0 边距。
- 当打开任何菜单或 `!Context.IsPlayerFree` 时，重置扳机状态机标志，避免菜单内操作泄漏至大世界。
- `Toolbar_Draw_Prefix` 捕获异常时返回 `true` 回退原版工具栏绘制，避免整帧渲染中断。

## 5. 验证方式

- 分别测试 12/24/36 格背包下 LB/RB 换行与 LT/RT 连续快速切换，确认不存在跳两格或回退第 0 行现象。
- 在肩键模式下验证十字键上下换行、LB/RB 及十字键左右移格。
- 在游戏选项页调整“工具栏格子大小”与“工具栏边距”滑块，确认底部居中不遮挡右下角能量/血条、水壶水量条位置准确、对话框文字宽度不被压缩。
- 切换到第 2/3 行后采集野生植物、收起家具，验证落入行与选中槽位行为符合预期。

## 6. 已知限制与防回归说明

- **滞回参数权衡**：`TriggerReleaseConfirmTicks` 当前为 `2`（平衡快速连按响应与霍尔扳机抖动）；若特定手柄出现 2～3 帧模拟掉零引发双跳，需在响应速度与防抖之间谨慎评估。
- **对话框宽度副作用**：切勿移除 `Toolbar._itemSlotSize = 0` 的重置逻辑，否则小屏设备上带立绘的对话框宽度会被严重压缩。
- **扳机日志必须节流**：`HandleTriggersDirectly` 的调试日志必须维持 `_lastTrigLog` 变更去重，禁止逐帧打印模拟扳机浮点值。
