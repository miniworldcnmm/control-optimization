# 专项设施菜单（罗宾/社区中心/博物馆/晶球）

- **产品生命周期**：`active`
- **关联代码**：`Patches/CarpenterMenuPatches.cs`、`Patches/JunimoNoteMenuPatches.cs`、`Patches/MuseumMenuPatches.cs`、`Patches/GeodeMenuPatches.cs`、`ModEntry.cs`、`ModConfig.cs`

## 1. 目的和用户可见行为

为游戏中交互机制高度特殊、在 Android 端原版几乎无法纯手柄完成的四个专项设施菜单提供完整主机化适配：

1. **罗宾建筑菜单（`CarpenterMenu`）**：
   - **防止打开即秒关**：消除从罗宾对话按 A 进入菜单后，A 键释放事件穿透触发取消按钮（ID 107）导致瞬间关闭的问题。
   - **农田视角全摇杆操控**：左摇杆平滑移动光标，抵达屏幕边缘时自动平移农田视野；建筑幽灵在任意缩放比例下实时紧跟光标。
   - **建造 / 移动 / 拆除 / 换肤**：建造模式下按 A 确认建造（材料或金币不足时不再错误关闭菜单）；移动模式下按 A 选定建筑、移动光标后再按 A 落位；拆除模式下按 A 绿色高亮建筑、再次按 A 确认拆除（移开光标自动取消高亮）；建筑涂装/外观选择器支持手柄切换。
2. **社区中心献祭菜单（`JunimoNoteMenu`）**：
   - **总览页**：十字键/摇杆在献祭包间空间导航，LB/RB 切换房间，按 A 进入捐赠页，返回时记忆上次所在献祭包位置；绘制自定义可见光标。
   - **捐赠页**：6 列背包网格完整导航，不符合当前献祭条件的物品置灰；按 A 捐赠物品；从最右列向右可切入所需材料清单查看提示，向左返回背包；金库献祭包支持对购买按钮按 A 付款。
   - **只读浏览**：从背包或箱子打开卷轴时，手柄直接浏览材料清单，无法捐赠或支付金库款项。详情页按 B 或轻点返回箭头回到总览。从箱子打开时，在总览按 B 或轻点右上角 **×** 返回同一个箱子。
   - **奖励领取**：可导航至礼物盒按 A 打开奖励菜单，按 A 领取整组，屏蔽向奖励盒反向存入或误触发 Y 键，按 B 退出保留未领奖励。
3. **博物馆捐赠与重排菜单（`MuseumMenu`）**：
   - 补绘 Android 隐藏的菜单光标，支持十字键选择背包内可捐赠古物/矿物并按 A 捐赠；支持博物馆重排模式下的全网格方向键移动与落位。
4. **克林特敲晶球菜单（`GeodeMenu`）**：
   - 选中晶球自动显示悬停提示，支持晶球之间的空间方位导航，单次按 A 即可将选中晶球放到铁砧并敲开（拦截 Android 在每次 A 后追加的触控模拟点击，防止误抓剩余堆叠），并修正光标吸附居中偏移。

## 2. 架构与主要调用流程

### 2.1 罗宾建筑菜单（`CarpenterMenuPatches`）

- `ModEntry.OnMenuChanged` 在进入/退出 `CarpenterMenu` 时调用 `OnMenuOpened()` / `OnMenuClosed()` 管理打开宽限期与状态复位。
- Patch `CarpenterMenu` 的 `releaseLeftClick`、`leftClickHeld`、`receiveGamePadButton` 以及 `IClickableMenu.exitThisMenu`（配合反射读写 `onFarm`、`freeze`、`currentBuilding`、`Action`、`moving`、`demolishing`、`buildingToMove`、`_drawAtX`、`_drawAtY`、`price`、`_selectedBottomButton` 等字段）。
- 在农田视图下同步覆写鼠标查询状态以驱动预览坐标并随退出清理，避免关闭菜单后 `GetMouseState` 覆盖泄漏。

### 2.2 社区中心献祭（`JunimoNoteMenuPatches`）

- `ModEntry.OnMenuChanged` 在进出 `JunimoNoteMenu` 时调用 `OnMenuChanged()` 重置内部游标与房间状态。
- 总览页补建 `allClickableComponents` 并布设空间邻接关系；捐赠页维护跟踪光标坐标，覆盖 `JunimoNoteMenu` 的 `receiveLeftClick`、`leftClickHeld`、`releaseLeftClick` 以及 `Mouse.GetState` / 输入层 `GetMouseState`（因 Android 原版 `receiveLeftClick` 忽略参数坐标而直接读取 `GetMouseState`），在按 A 时精准回传跟踪光标位置完成 `tryDepositItem` 或按钮点击。
- 箱子卷轴调用原版只读构造路径，由游戏选择初始可见房间。切房产生新菜单实例时传递原箱子的返回引用；箱子来源的 LT/RT 不会跳转至游戏主菜单。返回时恢复箱子导航并隔离本次关闭输入，切换到无关菜单、读档或回标题时清理返回上下文。
- 反射访问 `specificBundlePage`、`ingredientList`、`bundles`、`highlightedBundle`、`presentButton`、`whichArea`、`currentPageBundle`、`heldItem`。

### 2.3 博物馆（`MuseumMenuPatches`）

- Patch `LibraryMuseum.OpenDonationMenu`、`LibraryMuseum.OpenRearrangeMenu`、`Options.SnappyMenus` 属性 Getter、`MuseumMenu` 构造器及 `MuseumMenu.draw(SpriteBatch)`：
  - 仅在博物馆捐赠流程中受控启用 `SnappyMenus`，并在 `ModEntry.OnMenuChanged` 离开 `MuseumMenu` 时通过 `RestoreSnappyOnMenuClose()` 精确还原；重排模式设置 `reOrganizing` 使原版 `receiveKeyPress` 网格寻址正常生效，同时由 `draw` Postfix 自绘可见光标。

### 2.4 克林特敲晶球（`GeodeMenuPatches`）

- `ModEntry.OnMenuChanged` 在菜单切换时调用 `OnMenuChanged()`，并在打开 `GeodeMenu` 时调用 `OnGeodeMenuOpened(newGeodeMenu)` 强制开启 `_showTooltip = true`。
- 将手柄 A 键重定向至原版单次放置并砸开路径，拦截随后由 Android 注入的触控模拟点击，提供仅在有效晶球间移动的空间导航，并将吸附光标修正至槽位中心。

## 3. 配置与持久化

- `EnableCarpenterMenuFix`（默认 `true`）
- `EnableMuseumDonationController`（默认 `true`）
- `EnableConsoleGeodeMenu`（默认 `true`）
- 社区中心菜单随主机菜单生态默认启用，房间切换受 `UseBumpersInsteadOfTriggers` 协调。

## 4. 状态与失败语义

- 四个菜单均在 `ModEntry.OnMenuChanged` 挂接明确的状态清理/还原回调（`CarpenterMenuPatches.OnMenuClosed`、`JunimoNoteMenuPatches.OnMenuChanged`、`MuseumMenuPatches.RestoreSnappyOnMenuClose`、`GeodeMenuPatches.OnMenuChanged`），防止退出菜单后污染全局 `SnappyMenus` 或 `GetMouseState` 状态。

## 5. 验证方式

- 在罗宾处依次测试打开菜单不秒关、资金不足点击建造不退菜单、建造/移动/拆除（含移开取消高亮）与换肤，以及不同缩放级别下的幽灵对齐。
- 在社区中心测试房间切换、总览光标记忆、捐赠可捐/不可捐置灰、查看需求列表、金库付款与奖励领取（可配合 `tools/seed-boiler-room.ps1` 快速构造测试存档）。
- 在博物馆测试捐赠古物、重排展品及领取《矮人语教程》（可配合 `tools/seed-dwarf-test.ps1` 快速构造测试存档）。
- 在克林特处携带多组晶球按 A 连续砸开，验证光标位置、提示显示与剩余堆叠不被误抓。

## 6. 已知限制与防回归说明

- **硬崩溃禁区（极高优先级）**：**严禁使用 Harmony Patch `GeodeMenu.releaseLeftClick`**。已实机验证 Patch 该方法会在 Android 运行时引发硬崩溃；任何针对晶球菜单释放点击的处理必须避开此方法。
- **Android `receiveLeftClick` 坐标陷阱**：在 `JunimoNoteMenu` 与 `CarpenterMenu` 等 Android 菜单中，原生 `receiveLeftClick(int x, int y)` 会忽略形参转而直接读 `GetMouseState()`，且任何 `GetMouseState` 覆盖在菜单关闭时必须立即撤销。
- **博物馆 `SnappyMenus` 还原不可漏掉**：仅在捐赠菜单打开期间覆盖，关闭时必须恢复原值，避免影响游戏其他菜单。
