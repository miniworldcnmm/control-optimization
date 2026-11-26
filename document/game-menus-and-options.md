# 游戏主菜单、设置注入、对话框与标题界面

- **产品生命周期**：`active`
- **关联代码**：`Patches/GameMenuPatches.cs`、`Patches/CraftingPagePatches.cs`、`Patches/OptionsPagePatches.cs`、`Patches/OptionsPageInjectionPatches.cs`、`Patches/DialogueBoxPatches.cs`、`Patches/LoadGameMenuPatches.cs`、`Patches/LetterViewerMenuPatches.cs`、`ModEntry.cs`、`ModConfig.cs`

## 1. 目的和用户可见行为

补齐 Android 版《星露谷物语》主菜单全分页、系统选项、对话选择框以及标题/读档/信件界面的主机手柄导航体验与设置持久化能力：

1. **GameMenu 全分页主机导航与 LT/RT 切页**：
   - 使用 **LT / RT** 在游戏主菜单所有分页间切换（含社区中心页；从祝尼魔地块直接打开社区中心时屏蔽误切页）。
   - **社交（Social）**：十字键/摇杆导航与滚屏，右摇杆 3 格离散加速快滚，按 A 打开好感/送礼档案（`ProfileMenu`），LB/RB 在档案页切换村民，按 B 返回并恢复原滚动位置。
   - **收集（Collections） / 制作（Crafting） / 特殊能力（Powers）**：完整网格与子分页导航，以主机手指光标替代 Android 红框或光晕高亮。
   - **技能（Skills） / 动物（Animals）**：修复组件边界使网格与列表导航顺畅可用。
2. **长按 A 连续制作/烹饪**：在制作页和独立烹饪界面长按 A 按固定节奏持续合成，原材料不足或背包满时随原版 `showCraftButton` 自动停止，不会过量合成。
3. **选项页功能注入、触控隐藏与跨重启持久化**：
   - 在 Android 原版选项页中重新注入被隐藏但引擎原生可用的功能选项：**缩放比例（Zoom level, 50%–200%）** 滑块（插入音频滑块前）、**始终显示工具命中位置（whichOption 11）** 与 **移动时隐藏工具命中位置（whichOption 12）** 复选框（插入高级制作信息 `34` 后）。
   - 三项标签在选项页构造时按模组的 `Language` 取值，保存语言设置后重新打开游戏菜单即可读取新文案。详见[中英文切换与本地化](localization-and-language-switching.md)。
   - 手柄激活时自动隐藏 5 个纯触控选项（虚拟摇杆控制 `139`、屏幕按钮开关 `140`、隐形按钮宽度 `146`、双指缩放 `147`、调整虚拟按键按钮 `optionsButtonAdjustControls`）。
   - 实时根据选项列表总高度同步 `MobileScrollbox.maxYOffset`，确保 GMCM 按钮等后注入项可滚动到达。
   - 使用手柄调整缩放时经由移动端 `PinchZoom` 管线生效，并在关闭选项页/更改缩放时将缩放值与工具命中选项持久化到 `ModConfig`（`config.json`），同时触发原版 `SaveStartupPreferences` 落盘其他原生设置，解决 Android 冷重启重置选项的问题。
4. **主机风格对话选项框**：打开问题对话（睡觉、NPC 询问、节日选择）时默认选中第 1 项，并在手柄模式下用“原色底框 + 全亮度文字 + 暗红边框 + 右下角手指光标”替换 Android 黄色高亮与变淡文字；纯触控保持原版外观。
5. **标题页、读档页与多页信件/公会讨伐表**：
   - 读档界面异步扫描完成后自动高亮并吸附至第 0 槽位，支持滚动与删除确认框（`ConfirmationDialog`）导航。
   - 冒险家公会怪物讨伐表（单参 `LetterViewerMenu(string)`）与多页信件支持完整手柄吸附翻页。

## 2. 架构与主要调用流程

### 2.1 游戏主菜单分页与连续制作（`GameMenuPatches` + `CraftingPagePatches`）

- `GameMenuPatches` Patch `GameMenu.changeTab`、`GameMenu.receiveGamePadButton`、`SocialPage.update/drawNPCSlot/drawFarmerSlot`、`ProfileMenu.ChangeCharacter/draw`、`CraftingPage.draw`、`CollectionsPage.receiveGamePadButton/draw` 以及通过反射动态定位的 `PowersTab` 成员（`highlightTexture`、`selectedIndex`、`powers`、`currentPage`、`doSelect`）。
- 社交页通过 `ShouldSuppressRightStickForSocial()` 通知 `GameplayButtonPatches` 归零游戏侧右摇杆 Y 轴，由自身前缀读取 `RawRightStickY` 执行离散滚屏。
- `CraftingPagePatches` Postfix `CraftingPage.update(GameTime)`：在长按 A 时按固定节拍重派 `CraftingPage.receiveGamePadButton(Buttons.A)`；原版 `CraftSelectedRecipe` 每次制作后都会重检材料/背包并更新 `showCraftButton`，天然自限。

### 2.2 选项页注入与导航（`OptionsPageInjectionPatches` + `OptionsPagePatches`）

- **注入与滚动适配（`OptionsPageInjectionPatches`）**：
  - Postfix `OptionsPage` 6 参构造器：按 `HideTouchOptionsWithController && Game1.options.gamepadControls` 移除触控项；注入 `whichOption 11/12` 复选框及经反射调用 Android 5 参 `OptionsSlider` 构造器创建的缩放滑块（`whichOption 18`，设定 `sliderMinValue=50`、`sliderMaxValue=200` 后立即重设一次 `value` 属性以修正首次绘制的滑块拇指位置 `_percent`）。
  - Postfix `OptionsPage.update(GameTime)`：反射读取各元素虚属性 `OptionsElement.ItemHeight` 计算动态 `contentHeight`，仅当计算出的 `maxYOffset` 与当前值不同时才调用 `MobileScrollbox.setMaxYOffset`（避免每帧调用重置滚动状态 `havePanelScrolled`）。
- **手柄导航与持久化（`OptionsPagePatches`）**：
  - 通过 `ShouldSuppressLeftStick()` 在非 `FreeCursorOnSettings` 模式下通知 `GameplayButtonPatches` 归零左摇杆；Patch `IClickableMenu.receiveGamePadButton` 与 `OptionsPage.receiveLeftClick`，以十字键上下在可交互项间导航（跳过标题/空行），左右步进调整滑块（±10）与加减控件（±1），A 切换复选框/展开并确认下拉菜单，B 关闭下拉或退出菜单，右摇杆滚屏。
  - 修改缩放时通过 `ApplyMobileZoom` 驱动移动端 `PinchZoom.Instance.SetZoomLevel` 与窗口尺寸更新。
  - `ModEntry.OnMenuChanged` 关闭 `GameMenu` 时调用 `OptionsPagePatches.OnOptionsPageClosed()` 保存配置并触发原生偏好落盘；`ModEntry.OnSaveLoaded` 调用 `OptionsPagePatches.ApplySavedSettings()` 重放 `SavedZoomPercent`（非 0 时）及工具命中框布尔值。

### 2.3 对话框、读档页与信件（`DialogueBoxPatches` + `LoadGameMenuPatches` + `LetterViewerMenuPatches`）

- `DialogueBoxPatches`：Postfix `DialogueBox.setUpQuestions` 在手柄模式下预选索引 `0`（原版 Android 为 `-1`）；Postfix `DialogueBox.draw(SpriteBatch)` 按配置开关渲染主机风格边框与手指光标。
- `LoadGameMenuPatches`：在异步存档列表填充后一次性将 `_joypadSelectedItemIndex` 设为 `0` 并直接绑定 `currentlySnappedComponent = slotButtons[0]`（绕过延迟初始化导致的 `getComponentWithID` 返回 `null`），同步处理 `scrollArea` 滚动与 `confirmBox` 按钮选中。
- `LetterViewerMenuPatches`：Postfix 单参构造器 `LetterViewerMenu(string)` 补齐其缺失的初始吸附逻辑（对照三参构造器行为），保留纯主机选态反馈（不额外叠加鼠标精灵）。

## 3. 配置与持久化

- `EnableGameMenuNavigation`（默认 `true`）
- `EnableHoldToCraft`（默认 `true`）
- `EnableConsoleDialogueCursor`（默认 `true`）
- `FreeCursorOnSettings`（默认 `false`；开启后在选项页与 GMCM 恢复自由光标，依赖 `EnableGameMenuNavigation`）
- `HideTouchOptionsWithController`（默认 `true`）
- **持久化镜像字段**（写入 `config.json`，不在 GMCM 直接暴露为手动滑块，由选项页自动同步）：
  - `SavedZoomPercent`（默认 `0`，表示未修改过，不覆盖移动版默认缩放）
  - `SavedAlwaysShowToolHit`（默认 `false`）
  - `SavedHideToolHitWhenMoving`（默认 `false`）

## 4. 状态与失败语义

- 选项页注入中所有 Android 独有成员（`ItemHeight`、`OptionsSlider` 5 参构造、`MobileScrollbox`、`optionsButtonAdjustControls`）均做空值保护，解析失败仅跳过该子项而不影响页面打开。
- `OptionsPageInjectionPatches` 锚点插入（`34`、`1`）若在改版 UI 中找不到锚点，自动降级追加到列表末尾。

## 5. 验证方式

- 使用 LT/RT 轮切 GameMenu 全部分页，逐页测试社交（含 ProfileMenu 返回滚屏恢复）、收集、制作（长按 A 连续合成）、技能、动物、特殊能力与选项页。
- 在选项页修改缩放比例（例如 75% 或 120%）与“始终显示工具命中位置”，完全退出游戏冷重启并读档，确认设置自动恢复。
- 触发带选项对话（如上床睡觉 Yes/No），确认默认选中首项且显示暗红边框与手指光标。
- 在标题/读档页验证首槽高亮、滚屏与删除确认框；查看公会讨伐表与多页信件确认可翻页。

## 6. 已知限制与防回归说明

- **对话框门控不可依赖 `!lastCursorMotionWasMouse`**：Android 会把确认键合成为触屏事件导致 `lastCursorMotionWasMouse` 为真，因此 `DialogueBoxPatches` 必须以配置开关与手柄上下文门控。
- **选项页滚动上限仅在变更时写回**：切勿每帧无条件调用 `MobileScrollbox.setMaxYOffset`，否则会重置 `havePanelScrolled` 破坏滚动。
- **缩放滑块必须二次触发 `value` setter**：设置 `sliderMinValue=50`、`sliderMaxValue=200` 后若不重设 `value`，首次打开选项页时滑块拇指位置将按旧范围 0–100 错误渲染。
- **手柄检测优先 `Game1.options.gamepadControls`**：部分安卓掌机（如 Ayaneo）的 `GamePad.IsConnected` 可能误报 `false`，选项隐藏判断应继续使用 `Game1.options.gamepadControls`。
