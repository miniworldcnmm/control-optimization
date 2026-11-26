# 商店交易与玛妮动物菜单

- **产品生命周期**：`active`
- **关联代码**：`Patches/ShopMenuPatches.cs`、`Patches/AnimalQueryMenuPatches.cs`、`ModEntry.cs`、`ModConfig.cs`

## 1. 目的和用户可见行为

修复 Android 商店界面与玛妮动物信息弹窗的手柄交互缺口，实现与主机版一致的购买、出售与动物管理体验：

1. **主机风格商店（`ShopMenu`）**：
   - **购买页**：**A 键**按当前数量购买；**LB / RB** 调整购买数量（支持长按连发：常规模式下 ±10，肩键模式下 ±1）；**右摇杆**每次快速跳 5 项（支持长按连发）；左摇杆长按支持平滑连发导航（15 tick 延迟 / 4 tick 间隔）；购买完成后保持商品列表当前滚动位置，不跳回顶部。
   - **以物易物与特殊商品**：完整支持沙漠商人、书商等以物换物（包含整组兑换交付完整数量而非仅 1 个）、铁匠工具升级、食谱/制作配方购买（正确执行 `Item.LearnRecipe()` 并播放学习音效）、限量库存扣减，以及背包已满时完整退还金币与交换物。
   - **出售页**：**A 键**整组出售，**Y 键**出售单个（支持长按连续出售）；不可出售（0g）物品置灰显示且不弹出无效售价提示；选中物品旁渲染带金币图标的售价提示。
   - 售价提示按模组语言显示，例如整叠为 ` 30g（每个 10g）` 或 ` 30g (10g each)`，单件为 ` 10g`。气泡按实际文本宽度定位并限制在视口边缘。详见[中英文切换与本地化](localization-and-language-switching.md)。
   - **通用细节**：购买与出售两页均显示清晰光标（覆盖铁匠、Joja 等原版隐藏光标的商店）；标签切换按钮渲染自适应当前手柄布局的按键图标，且在手柄模式下阻止触控切换按钮误触；适配衣柜与鱼缸等存储型商店的存取规则。
2. **玛妮动物菜单（`AnimalQueryMenu`）**：
   - 使用手柄打开动物出售/迁移对话框时，自动高亮首个可操作按钮（`Move`），不再出现无任何焦点的空白状态。
   - 选择出售进入“出售确认”弹窗时，自动将默认焦点切至确认按钮（`Tick`），使“选中出售 → 按 A 打开确认 → 再按 A 确认出售”链路畅通，同时仍可用十字键左右切回取消。纯触控体验不受影响。

## 2. 架构与主要调用流程

### 2.1 商店补丁（`ShopMenuPatches` + `ModEntry` + `GameplayButtonPatches`）

- **底层右摇杆协作**：`GameplayButtonPatches` 检测 `ShopMenuPatches.ShouldSuppressRightStick()`，在购买页归零游戏侧右摇杆以防止原生滚轮偏移 `currentItemIndex`，由 `ShopMenuPatches` 直接读取 `RawRightStickY` 驱动 5 项快跳。
- **数量调整**：`ModEntry.OnButtonsChanged` 在 `ShopMenu` 打开时根据 `UseBumpersInsteadOfTriggers` 分流调用 `ShopMenuPatches.AdjustQuantity(shopMenu, ±1 或 ±10)` 并启动 `StartLBHold()` / `StartRBHold()`，长按连发由 `ShopMenu.update` 补丁推进；切换至出售页时重置购买数量防止串页。
- **Harmony 钩子**：Patch `ShopMenu` 的 `receiveGamePadButton`、`update`、`receiveLeftClick`、`draw(SpriteBatch)`，并通过反射操作 Android 独有字段 `inventoryVisible`、`quantityToBuy`、`inventoryButton`、`currentlySelectedItem`、`itemsPerPage`、`scrollArea`（`MobileScrollbox`）、`itemButtonHeight` 以及 `updateItemButtons`、`rebuildSaleButtons`、`setCurrentItem`。
- **滚动保持与顶界限幅**：购买导致 `rebuildSaleButtons` 重建列表前捕获 `MobileScrollbox` 的像素滚动偏移并在重建后恢复，配合顶部限幅将十字键上越界滚动固定在 `0`。
- **配方与购买后置**：走自定义购买分支调用 `Object.actionWhenPurchased` 后，显式镜像原版 `ShopMenu.purchaseItem` 的 `Item.LearnRecipe()` 与 `newRecipe` 音效步骤，防止扣钱但不学配方。

### 2.2 玛妮动物菜单补丁（`AnimalQueryMenuPatches`）

- Android 版 `AnimalQueryMenu` 自带移动端 `receiveGamePadButton`，驱动私有枚举字段 `_selectedButton`（`None=0, Move=1, Sell=2, AllowReproduction=3, Cancel=4, Tick=5`）而非主机吸附系统。
- 全反射实现（编译兼容 PC DLL）：
  - Patch `AnimalQueryMenu(FarmAnimal)` 构造器：手柄模式下打开时将 `_selectedButton` 初始化为 `Button_Move (1)`。
  - Patch `OnClickSell` 与 `receiveGamePadButton`：进入 `confirmingSell` 确认态后将 `_selectedButton` 设为 `Button_Tick (5)`，确保二次按 A 落入确认出售而非取消。
  - 全部逻辑通过 `(Config.EnableConsoleAnimalMenu ?? true) && Game1.options.gamepadControls` 门控。

## 3. 配置与持久化

- `EnableConsoleShops`（默认 `true`）
- `EnableConsoleAnimalMenu`（默认 `true`）
- `UseBumpersInsteadOfTriggers`（默认 `false`；决定商店 LB/RB 数量步进为 ±1 还是 ±10）
- `VerboseLogging`（默认 `false`）

## 4. 状态与失败语义

- 商店购买若目标物品无法放入背包，需保证扣减的金币与以物易物材料完整退回，不产生吞钱或吞物品现象。
- 切换买卖分页时清理购买数量与选中缓存，防止产生幽灵购买。
- `AnimalQueryMenuPatches` 所有反射成员均为运行期延迟/构造解析并空值保护，在成员缺失时安全回退原版行为。

## 5. 验证方式

- 在皮埃尔、沙漠商人、书商（成组以物易物）、铁匠（工具升级与配方）、Joja、衣柜/鱼缸测试购买、批量调数、右摇杆快跳、滚屏保持、出售整组/单个、0g 物品置灰与满包退款。
- 在玛妮牧场或农舍动物界面使用手柄打开动物菜单：确认打开即高亮按钮，导航至出售按 A 打开确认框后再按 A 成功售出，或按方向键切至取消安全退出。

## 6. 已知限制与防回归说明

- **Android 商店滚动依赖 `MobileScrollbox`**：`ShopMenu.currentItemIndex` 在 Android 上并非真实滚动源，任何滚动保持逻辑必须读写 `scrollArea` 的像素偏移。
- **出售页不可依赖 `hoveredItem`**：Android 商店出售页不会在手柄吸附导航时可靠更新 `hoveredItem`，必须结合吸附组件与 `currentlySelectedItem` 判定目标物品。
- **配方学习必须显式补齐**：绕过原版 `purchaseItem` 直接调用 `actionWhenPurchased` 时，`actionWhenPurchased` 仅返回 `isRecipe` 布尔值而不会自动教授配方，切勿删除后续 `LearnRecipe()` 调用。
