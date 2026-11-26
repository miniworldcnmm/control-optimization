# 背包、箱子与出货箱主机操控

- **产品生命周期**：`active`
- **关联代码**：`Patches/InventoryManagementPatches.cs`、`Patches/InventoryPagePatches.cs`、`Patches/ItemGrabMenuPatches.cs`、`Patches/BundleIconPulsePatches.cs`、`Patches/ChestBundleNote.cs`、`Patches/ShippingBinPatches.cs`、`Patches/FishingRodPatches.cs`、`Patches/SlingshotPatches.cs`、`ModEntry.cs`、`ModConfig.cs`

## 1. 目的和用户可见行为

为背包页（`InventoryPage`）、各类箱子/容器（`ItemGrabMenu`）和出货箱（`ShippingBin`）重建主机式手柄交互，并消除 Android 原版 X 键删除物品等高危缺陷：

1. **主机风格背包管理**：
   - **A 键**：抓起整组物品至光标，再次按 A 放下或与目标格互换；完整支持帽子、戒指、靴子、上衣、裤子、饰品等装备槽穿脱与交换，以及排序按钮与垃圾桶。
   - **Y 键**：从堆叠中抓起单个物品，长按持续逐个抓取。
   - **X 键**：整理背包，同时强制拦截 Android 原版原始 X 键误删槽位物品的缺陷。
   - **视觉与提示**：手持物品跟随当前光标槽位渲染；悬停任意物品及装备槽时显示完整属性提示；若抓起物品期间触碰屏幕，安全退回原槽位；在整理与垃圾桶之间的丢弃区支持主动丢弃。
   - **社区中心需求提示**：手柄停在背包内的物品上，或持物停在空格时，金色卷轴会为已解锁收集包仍需的物品跳动。背包页沿用原生触屏操作。
2. **鱼竿鱼饵/鱼钩与弹弓弹药装卸**：
   - 在背包页对鱼饵/鱼钩或弹弓弹药按 **A** 选中，再对鱼竿或弹弓按 **Y** 挂载；未选中耗材时直接对鱼竿/弹弓按 **Y** 卸载（鱼竿先卸鱼饵再卸鱼钩）并将卸下物置入光标。
3. **主机风格箱子操控**：
   - **A 键**：箱子与背包间一键转移整组物品；当目标容器已满时自动切入主机式替换交换流程（按 A 抓起、移动至目标格按 A 互换、按 B 取消）。
   - **Y 键**：转移单个物品（支持长按连续转移；对鱼竿/弹弓可分离附件）。
   - **X 键**：整理箱子；**RB 键**：快速吸附到“填充堆叠（Fill Stacks）”按钮。
   - **全侧边栏与调色板**：侧边栏全部按钮（整理箱子、填充堆叠、切换颜色、整理背包、垃圾桶、关闭）均可手柄可达；7×3 调色板支持完整网格导航，按 B 仅关闭调色板而不退出箱子；关闭按钮退出后设置 `GameplayButtonPatches.SuppressAUntilRelease` 防止按键穿透。
   - **查看收集包**：普通箱子、大箱子、冰箱和迷你冰箱在原版进度允许时显示金色卷轴。轻点卷轴或导航至卷轴按 **A** 进入只读浏览，关闭后回到同一箱子。
   - **特殊容器保护**：仅取容器（如自动采集器 Auto-Grabber、社区中心奖励箱等）禁止反向存入，奖励菜单屏蔽 Y 键单取；博物馆奖励书（如《矮人语教程》）正确消耗习得 `canUnderstandDwarves` 技能。
4. **主机风格出货箱**：
   - 仅针对真实 `ShippingBin` 生效（避免误伤衣柜、鱼缸等复用 `ItemGrabMenu` 的容器）：**A 键**出货整组，**Y 键**出货单个，并正确刷新“上次出货”槽位显示与回取。

## 2. 架构与主要调用流程

### 2.1 背包与装备（`ModEntry` + `InventoryManagementPatches` + `InventoryPagePatches` + `FishingRodPatches` + `SlingshotPatches`）

- **按钮事件分发（`ModEntry.OnButtonsChanged`）**：在 `GameMenu.inventoryTab` 下优先拦截原始 `SButton.ControllerX`（当 `EnableConsoleChests` 开启）；随后按顺序尝试：
  1. `A`：`InventoryManagementPatches.HandleAButton` → 未接管时回退通知 `FishingRodPatches.OnAButtonPressed` 与 `SlingshotPatches.OnAButtonPressed` 记录选中的鱼饵/弹药。
  2. `Y`：`FishingRodPatches.TryHandleBaitTackle` → `SlingshotPatches.TryHandleAmmo`（单件长按拆分由 `InventoryManagementPatches.OnUpdateTicked` 轮询驱动）。
  3. `X`：`InventoryPagePatches.SortPlayerInventory()`。
- **绘制与提示修补**：
  - `InventoryManagementPatches` Patch `InventoryPage.draw` 与 `InventoryMenu.draw`，绘制跟随光标的手持物品，并恢复 Android 剥离的装备槽 `drawToolTip` 调用。
  - 丢弃区标签「丢弃」或「Drop」跟随模组语言。丢弃区定位使用游戏本体的本地化农场名格式，按两种游戏字体中较宽的结果估算并保留边距。详见[中英文切换与本地化](localization-and-language-switching.md)。
  - `InventoryPagePatches` Patch `GameMenu` / `InventoryPage` / `InventoryMenu` 的 `receiveGamePadButton`、`receiveLeftClick`、`leftClickHeld` 与 `draw`，拦截触控模拟残留并维护装备槽 `Farmer.Equip` 路径。
- **菜单退出清理**：`ModEntry.OnMenuChanged` 在离开 `GameMenu` 时调用 `InventoryManagementPatches.OnMenuClosed()`、`FishingRodPatches.ClearSelection()` 与 `SlingshotPatches.ClearSelection()`。

### 2.2 箱子与容器（`ItemGrabMenuPatches`）

- `ModEntry.OnMenuChanged` 打开 `ItemGrabMenu` 时调用 `ItemGrabMenuPatches.FixSnapNavigation(itemGrabMenu)`，发现并按对象引用缓存侧边栏按钮（避免重复 `myID` 冲突）及关闭按钮引用，补全导航邻接关系。
- Patch `ItemGrabMenu` 的 `receiveGamePadButton`、`update`、`receiveLeftClick`、`draw`、`snapToDefaultClickableComponent` 以及 `IClickableMenu.exitThisMenu`、`Farmer.canUnderstandDwarves` 属性写入：
  - 统一驱动 A 整组快传、Y 单件长按快传、X 整理、RB 跳至 `FillOutStacks`、满格互换状态机、7×3 调色板网格导航、手指光标绘制、自适应屏幕的悬浮提示定位，以及退出时的状态清理（`OnMenuClosed`）。
  - 存/取物品时触发 `behaviorFunction` / `behaviorOnItemGrab` 等原版钩子，确保任务进度、献祭钩子与容器源移除一致生效。

### 2.3 社区中心卷轴与材料提示

- 卷轴归属 `EnableConsoleChests`。仅处理来源为箱子、类型为普通或大箱子的 `ItemGrabMenu`，排除自动采集器、出货箱与奖励菜单。解锁条件沿用原版，包含失踪收集包阶段。
- 卷轴复用可见的原版组件，缺少组件时由当前箱子绘制。64×64 的点击框优先放在上方「整理箱子」按钮左侧，与按钮垂直居中，间隔 12 像素。若箱子网格与按钮之间只剩 64–75 像素，则将卷轴放在间隙中央。首选位置放不下时，卷轴自动在安全区域内避开关闭按钮、侧栏与两个物品网格；没有空位时隐藏。调色板展开时暂停卷轴点击与手柄导航，调色板遮住卷轴时隐藏卷轴。
- 卷轴在整理按钮左侧时，手柄可在「箱子第一行最右格 ↔ 卷轴（ID 898）↔ 整理箱子」之间双向移动。从卷轴向上到关闭按钮，向下到填充堆叠（缺少时依次找切换颜色、整理背包）。从关闭按钮向下仍回到整理箱子。回退布局中，关闭按钮向下可到卷轴；卷轴位于侧栏右侧时，也可从填充堆叠或切换颜色向右进入。
- 提示按当前箱子或背包槽位取物，空格时检查满格互换或光标上的物品。触屏优先检查拖动中的物品，再检查手指按住的格子或最近触摸网格中仍选中的格子。点空白处、转走选中物品后，卷轴恢复静止。
- 游戏菜单打开、符合条件的箱子打开和从只读浏览返回箱子时刷新 `CommunityCenter.refreshBundlesIngredientsInfo()`。开箱期间逐帧比对 `NetWorldState.Bundles` 的槽位状态，变化后刷新材料缓存及当帧判定。物品和类别、最低品质均交给原版 `couldThisIngredienteBeUsedInABundle` 判断。已提交的槽位不再触发正弦跳动；同一物品仍可满足其他已解锁的空缺槽位。
- 空手时轻点卷轴或选中后按 **A** 可只读查看收集包。返回后保留原箱子和内容，手柄焦点回到卷轴。详见[社区中心菜单](special-menus-carpenter-cc-museum-geode.md)。

### 2.4 出货箱（`ShippingBinPatches`）

- Patch `ItemGrabMenu.receiveGamePadButton`，严格校验菜单来源确认为出货箱（排除衣柜、鱼缸等其他商店/储物容器），沿用游戏原生出货流处理 A 整组 / Y 单件出货并更新“上次出货”记录。

## 3. 配置与持久化

- `EnableConsoleChests`（默认 `true`；控制箱子卷轴、背包/箱子 X 键整理与原始 X 键防误删）
- `EnableConsoleInventory`（默认 `true`；控制背包 A/Y 抓取放置与鱼竿/弹弓装卸）
- `EnableConsoleShipping`（默认 `true`）
- `EnableConsoleDropBlocker`（默认 `true`；由 `InventoryManagementPatches` 主动丢弃流程读取）

## 4. 状态与失败语义

- 关闭背包或箱子时清空内部手持/互换/选中耗材引用；从箱子进入只读收集包时暂存箱子实例，避免跨菜单残留空指针或幽灵物品。
- 背包抓取期间若发生触屏中断，将暂存物品退回来源槽位，保证物品不丢失、不复制。
- 箱子关闭按钮退出后通过 `GameplayButtonPatches.SuppressAUntilRelease = true` + `InvalidateCache()` 防止按住的 A 键在关闭瞬间立刻重新打开箱子。

## 5. 验证方式

- 在背包中测试 A 抓放/互换、Y 单件与长按拆分、X 整理（确认绝不会删除当前格物品）、装备槽穿脱与提示、鱼竿装卸鱼饵/鱼钩、弹弓装卸弹药。
- 在普通箱子、自动采集器、鱼缸、衣柜、出货箱中分别测试 A/Y/X/RB、侧边栏导航、7×3 调色板、满格互换与关闭按钮退出，确认无物品复制或丢失。
- 在普通箱子、大箱子与冰箱上分别用手柄和触屏选择合格、品质不足及已提交的材料，并测试点击空白、转走物品和切换输入方式。让另一位玩家在箱子保持打开时提交当前材料，检查跳动是否随同步停止，以及其他包仍需要该物品时是否继续。
- 从箱子第一行最右格连续按右到卷轴和整理箱子，再连续按左原路返回；从关闭按钮按下应到整理箱子。改变缩放，检查回退位置不遮挡按钮或槽位，展开和收起调色板后检查手柄导航。打开卷轴后切换房间、查看材料，再分别用 B 和关闭按钮返回原箱子。

## 6. 已知限制与防回归说明

- **侧边栏按钮按引用识别**：Android `ItemGrabMenu` 中部分侧边栏按钮共用 `myID`，切勿改回仅按 `myID` 匹配。
- **出货箱来源校验必须收紧**：若放宽 `ShippingBinPatches` 的容器判定，会误拦截衣柜/鱼缸导致衣物消失或鱼类复制。
- **容器移除钩子不可绕过**：从储物类容器执行 Y 单件拿取时必须触发源容器的移除回调（否则鱼缸等会出现复制物品问题）。
