# Android Consolizer 中文汉化与优化版（control-optimization）

> **Fork 源头与版本说明**
>
> 本项目 Fork 自上游开源项目 [**sonofskywalker3/AndroidConsolizer**](https://github.com/sonofskywalker3/AndroidConsolizer)（原版 Nexus Mods 页面：[#41869](https://www.nexusmods.com/stardewvalley/mods/41869)，原作者：`sonofskywalker3`），是该 Mod 的**中文汉化与持续优化版本**。
>
> - **本项目仓库**：[`miniworldcnmm/control-optimization`](https://github.com/miniworldcnmm/control-optimization)
> - **Fork 上游原项目**：[`sonofskywalker3/AndroidConsolizer`](https://github.com/sonofskywalker3/AndroidConsolizer)（当前代码基线同步至上游 `v4.0.5`）
> - **开源协议**：沿用原项目 **MIT License**，保留原作者署名，允许自由修改与再分发。

让 Android 版《星露谷物语》（Stardew Valley）的手柄体验达到与 Nintendo Switch 主机版一致的水准。累计提供 90+ 项主机化操控修复与优化，覆盖商店、背包、箱子、工具栏、罗宾建筑菜单、社区中心献祭包、家具摆放、游戏菜单全分页、动物出售、物品收纳以及右摇杆自由光标。

如果你在安卓掌机（如 Odin、Ayaneo、Retroid、Logitech G Cloud 等）上游玩，或将手机/平板外接手柄投屏至电视，本 Mod 可以让你基本脱离触屏，流畅完成全流程游玩。

> **项目文档导航**：如需了解代码架构、工程约束与各模块技术行为说明，请查阅 [AGENTS.md](AGENTS.md) 与规范功能文档索引 [document/README.md](document/README.md)。

---

## 当前基线（v4.0.5）核心亮点

当前分支在完整主机控制方案（上游 `v4.0.0` 右摇杆自由光标里程碑）基础上，包含以下最新改进（所有主要功能均可通过 GMCM 独立开关）：

- **中英文切换**覆盖 GMCM 设置、键位对照、模组新增的选项标签、背包丢弃区和商店售价提示。
- **社区中心收集包卷轴**：普通箱子、大箱子和冰箱中，手柄或触屏选中收集包仍需的物品时，金色卷轴会跳动。点卷轴或选中后按 **A** 可查看各房间的材料，关闭后返回原箱子。背包页也支持手柄需求提示。
- **物品收纳设置与防连发修复（v4.0.3 / v4.0.5）**：在配置菜单中补回 Android 原版隐藏的“物品收纳（Item Stowing）”选项（关闭 / 仅手柄 / 开启），并修复 Android 长按工具键自动连发导致单次按键在同一帧内“收起又立刻掏出”的循环问题，确保每次按键只切换一次收纳状态。
- **无手柄脚本化测试支持（v4.0.4）**：内置开发者测试辅助模块（`InputSimulator`），允许在无物理手柄的设备上通过 Mod 目录内的 `sim-input.txt` 脚本模拟手柄按键并输出日志（无该文件时零影响）。
- **玛妮动物出售与移动修复（v4.0.2）**：打开动物信息菜单时自动高亮首个选项，且在“出售确认”弹窗中默认指向确认键，使用手柄按确认即可正常出售动物（新增“Console Animal Menu”独立开关）。
- **以物易物商店整组兑换（v4.0.2）**：在书商等以物品兑换成组物品的商店（如用书换取 20 硬木）交易时，正确交付整组数量而非仅给 1 个。
- **右摇杆自由光标与主机化大系统（v4.0.0 / v3.9.0 / v3.8.0）**：完整支持右摇杆自由光标（瞄准工具/交互/摆放、情境手型/放大镜指针、约 4 秒无操作自动淡出回退面朝格）、移动中蓄力升级版锄头/水壶、主机风格弹弓直瞄、防秒捡掉落保护、选项页恢复工具命中框与缩放滑块并持久化、主机风格对话选项高亮、可缩放 12 格固定工具栏等。

---

## 手柄布局与操控风格支持

### 物理手柄布局（Controller Layout）

- **Switch / Odin**：A = 右，B = 下，X = 上，Y = 左
- **Xbox**：A = 下，B = 右，X = 左，Y = 上
- **PlayStation**：Cross(×) = A，Circle(○) = B，Square(□) = X，Triangle(△) = Y

### 操控风格（Control Style）

- **Switch 风格**：右侧按键确认，下方按键取消
- **Xbox / PS 风格**：下方按键确认，右侧按键取消

> 提示：你也可以在配置中关闭 `Button Remapping`（按键重映射），让所有按键按 Android 原版映射透传。

---

## 功能一览

### 主机风格工具栏（Toolbar）
- 使用主机式 **12 格固定单行工具栏**，取代 Android 原版混乱的滚动条
- **LB / RB** 切换工具栏行（满背包最多 3 行）
- **LT / RT** 在当前行内左右切换格子（含模拟/数字扳机双阈值防抖，避免霍尔扳机掉帧跳两格）
- 视觉样式贴近主机版，且根据角色站位自动在屏幕底部/顶部避让切换
- 拾取的新物品优先进入当前正在查看的工具栏行（非家具不打断当前手持工具，家具拾取后自动切至该家具方便再次摆放）
- 兼容游戏设置中的“工具栏格子大小（Toolbar Slot Size）”滑块（自动限幅避免遮挡右下角体力/血条）与“工具栏边距（Toolbar Padding）”滑块

### 商店（Shops）
- **A 键**：在购买页购买，在出售页整组出售
- **Y 键**：出售单个物品（长按连续快速出售）
- **LB / RB**：调整购买数量（长按连发；常规模式下 ±10，肩键模式下 ±1）
- **右摇杆**：每次快速跳转 5 项（支持长按连发），且购买后正确保持列表滚动位置
- 标签切换按钮显示自适应当前手柄布局的按键图标
- 出售页选中物品旁显示带金币图标的售价提示，不可出售（0g）物品置灰显示
- 购买页与出售页均显示清晰光标
- 完整支持皮埃尔、罗宾、玛妮、铁匠工具升级、沙漠商人、配方购买（正确学习配方）、书商以物易物、Joja、衣柜、鱼缸等全商店场景

### 箱子（Chests）
- **A 键**：在箱子与背包间一键转移整组物品（无需先选中）
- **Y 键**：转移单个物品（长按连续转移；对鱼竿/弹弓可分离附件）
- **X 键**：整理箱子内容
- **RB 键**：快速吸附到“填充堆叠（Fill Stacks）”按钮
- 所有侧边栏按钮均可手柄导航：整理、填充堆叠、切换颜色、整理背包、垃圾桶、关闭
- 完整支持 7×3 箱子调色板网格导航，按 **B** 仅关闭调色板而不关闭箱子
- 背包或箱子已满时支持主机式交换机制：按 **A** 拿起待替换物品，选定目标格按 **A** 互换，按 **B** 取消
- 彻底拦截 Android 原版 **X 键误删物品** 的恶性 Bug（保护铱制工具与贵重物品）
- 解锁后，金色卷轴优先显示在上方「整理箱子」按钮左侧。在这个位置，从箱子第一行最右格按右即可选中卷轴，按 **A** 打开收集包。空间不足时卷轴会自动避开按钮和物品槽位。已提交的材料只在其他已解锁槽位仍需要且品质达标时继续提示。

### 背包（Inventory）
- **A 键**：将整组物品抓起到光标上，再次按 **A** 放置或与目标格互换
- **Y 键**：从堆叠中抓取单个物品（长按连续抓取）
- **X 键**：整理背包
- 手持物品随光标槽位实时渲染
- 支持光标悬停提示及装备槽（帽子、戒指、靴子、衣服、裤子、饰品）属性提示
- 光标停留在可献祭物品上（或抓起该物品悬停于空位/丢弃区）时，社区中心金色卷轴图标自动脉冲跳动提示

### 出货箱（Shipping Bin）
- **A 键**：出货当前格整组物品
- **Y 键**：出货单个物品
- “上次出货”槽位显示与回取正常生效

### 家具、合成物与种子摆放
- 使用单个红/绿幽灵方框精准标示家具实际落点，取代 Android 易混淆的全屏绿色可放区域
- 落点上方渲染半透明家具预览，与主机版表现一致
- 洒水器、机器、小桶等可摆放合成物同样采用单格预览框
- 种子与肥料仅显示单格红/绿目标框（不绘制冗余半透明包体）
- 修复床铺摆放后弹回背包的问题
- 修复 **Y 键** 快速连触发导致家具反复放置/拾取的问题（单次按键只触发一次交互）

### 罗宾建筑菜单（Carpenter Menu）
- 修复手柄打开罗宾建筑菜单时瞬间关闭的问题
- 农场视图下完整支持左摇杆移动光标并在屏幕边缘自动平移视角
- **建造模式**：建筑幽灵实时跟随光标，按 **A** 确认建造；金币/材料不足时不会误关菜单
- **移动模式**：按 **A** 选中建筑，移动光标后按 **A** 确认新位置
- **拆除模式**：按 **A** 绿色高亮待拆建筑，再次按 **A** 确认拆除；移开光标安全取消选中
- 支持手柄切换建筑涂装/外观皮肤

### 社区中心献祭包（Community Center Bundles）
- 献祭总览页：十字键/摇杆选择献祭包，按 **A** 进入捐赠页，返回时自动记忆光标位置
- 捐赠页：支持 6 列背包网格导航，按 **A** 捐赠物品，不符合条件的物品置灰；向右可切入所需物品清单查看提示
- 金库献祭包：在购买按钮上按 **A** 直接付款完成
- 奖励领取：可导航至礼物盒按 **A** 打开奖励页并按 **A** 领取整组（屏蔽向奖励盒反向存入）
- 自动绘制清晰光标（弥补 Android 原版此处隐藏光标的缺陷）

### 博物馆与敲晶球（Museum & Geodes）
- **博物馆**：显示可见光标，支持十字键选择并按 **A** 捐赠古物/矿物，以及在重排模式下使用完整网格导航；修复博物馆奖励书（如《矮人语教程》）直接消耗习得技能而非塞进背包的问题
- **克林特晶球菜单**：按 **A** 即可直接砸开晶球，自动高亮选中晶球并显示悬停提示，支持晶球间的空间方位导航

### 标题界面、读档与信件
- 标题界面与读档界面：支持可见光标与吸附导航（存档选择、列表滚动、删除确认弹窗）
- 冒险家公会怪物讨伐列表与多页信件均支持手柄完整翻页浏览

### 游戏主菜单各分页（Game Menu Tabs）
- **社交（Social）**：浏览村民列表、右摇杆快速滚动、按 **A** 打开好感/送礼档案、**LB / RB** 在档案页切换村民
- **收集（Collections）**：物品与子分页网格导航，手指光标替代红框
- **制作（Crafting）**：手指光标替代红框；**长按 A 键** 可连续快速合成/烹饪，材料耗尽或背包满时自动停止
- **技能（Skills）**：技能图标与等级条网格导航
- **动物（Animals）**：完整浏览农场动物列表
- **特殊能力（Powers）**：手指光标替代高亮
- **选项（Options）**：左摇杆/十字键导航、按 **A** 切换/展开下拉框、右摇杆滚动；恢复“始终显示工具命中位置”复选框与可用的 **缩放比例（Zoom Level, 50%–200%）** 滑块；连接手柄时可选隐藏纯触控虚拟摇杆选项
- **LT / RT**：在所有主菜单分页间切换（含社区中心页）

### 实机主机化玩法（Gameplay）
- **移动中蓄力工具**：按住升级版（铜及以上）锄头或水壶时可自由走位蓄力，松手释放范围效果，不再原地连挥单格
- **主机风格弹弓直瞄**：装备弹弓时自由移动，按住工具键拉弓并用左摇杆直接瞄准，松手发射一次
- **防秒捡掉落保护**：主动丢弃物品时启用游戏原生 ~1.2 秒防自拾取保护，避免刚丢出又吸回背包（不影响怪物掉落与作物收获）
- **工具命中位置框**：可在游戏内选项页开启，跟随右摇杆光标并在光标淡出后自动回到角色面朝格
- **物品收纳（Item Stowing）**：可在 Mod 配置菜单中开启（仅手柄 / 开启），按工具键即可将无法放置的手持物收起或重新取出，且已防连发抖动

### 右摇杆自由光标（Right-Stick Cursor）
- 大世界中拨动右摇杆移动屏幕光标，精准瞄准工具挥动（含斜角格）、地块交互与家具/机器/种子摆放，体验与 Switch 一致
- **情境指针**：悬停在可交互对象（箱子、邮箱、未抚摸动物等）时显示手型图标，悬停在可检视物件时显示放大镜图标
- 无输入约 4 秒后自动淡出，工具与交互自动回退到面朝格；再次拨动时从原位继续，不会强行跳回屏幕中央
- 可通过 `Enable Right-Stick Cursor` 开关；关闭时大世界右摇杆会被归零以杜绝光标漂移

### 其他主机化修复
- **鱼竿鱼饵/鱼钩管理**：背包中对鱼饵/鱼钩按 **A** 选中，对鱼竿按 **Y** 挂载或卸载
- **弹弓弹药管理**：同上，**A** 选弹药，**Y** 在弹弓上装填/卸载
- **跳过过场动画**：可跳过剧情期间连续按两次 **Start** 跳过
- **Start 键短按/长按**：短按打开游戏菜单，长按约 0.5 秒直接打开任务日志
- **对话选项框**：打开时默认选中首项，以主机风格暗红边框 + 手指光标标识，取代 Android 黄色高亮块
- **设置持久化**：游戏内缩放比例与工具命中框选项会随配置保存并在重启后自动恢复，手柄修改的原生选项同样正确落盘
- **SMAPI 启动白屏自愈**：检测并自动恢复 Android SMAPI 偶发启动白屏卡死
- **GMCM 手柄导航**：可配合上游作者提供的 [支持手柄导航的 GMCM 分支](https://github.com/sonofskywalker3/GenericModConfigMenu) 使用

### 肩键替代模式（Bumper Mode）
针对部分蓝牙连接下无法识别扳机键的手柄（例如部分 Android 设备上的蓝牙 Xbox 手柄）：
- 工具栏：**十字键 上/下** 切换行，**LB / RB** 或 **十字键 左/右** 行内移动
- 商店：**LB / RB** 调整购买数量（±1）

---

## 已测试手柄（上游社区实测）

- **AYN Odin Pro**（内置手柄）— 完整支持
- **Ayaneo Pocket Air Mini**（内置手柄）— 完整支持
- **EasySMX S10**（平板）— 完整支持
- **Gamesir X2** — 完整支持（数字扳机）
- **Nintendo Switch Pro** — 支持（自 v3.8.4 起支持纯数字扳机；部分 Android 设备可能需要将 Controller Layout 设为 Xbox）
- **Xbox 蓝牙手柄** — 支持（若扳机无法识别，请开启肩键替代模式）
- **Logitech G Cloud** — 完整支持（上游主要测试设备）

---

## 运行依赖

- Android 版《星露谷物语》 **1.6.15+**
- [SMAPI 4.0+ for Android](https://github.com/NRTnarathip/SMAPI-Android-1.6)（作者：NRTnarathip）
- （可选，强烈推荐）Generic Mod Config Menu（支持手柄导航的分支见 [sonofskywalker3/GenericModConfigMenu](https://github.com/sonofskywalker3/GenericModConfigMenu/releases)）

---

## Android 安装指南

> 注意：普通 Mod 压缩包**不能**通过 SMAPI Launcher 的“Install SMAPI From Zip”按钮安装，该按钮仅用于安装 SMAPI 本体，否则会报错 `Please select file SMAPI-4.x.x.xxxx.zip`。

**1. 安装 SMAPI（仅首次需要）**
1. 从 [NRTnarathip/SMAPILoader](https://github.com/NRTnarathip/SMAPILoader/releases) 下载并安装 SMAPI Launcher 应用。
2. 从 [NRTnarathip/SMAPI-Android-1.6](https://github.com/NRTnarathip/SMAPI-Android-1.6/releases) 下载 `SMAPI.4.x.x-(xxxx).zip` 到手机下载目录（**不要解压**）。
3. 打开 SMAPI Launcher，点击 **Install SMAPI From Zip**，选择上述 SMAPI 压缩包。
4. 等待提示 “Successfully Install SMAPI”。

**2. 安装本 Mod**
1. 获取本项目的发布压缩包 `AndroidConsolizer.X.Y.Z.zip`（或自行编译产出），**不要解压**。
2. 打开 SMAPI Launcher，依次点击 **Mod Manager** → **Install Mod**，选择 Mod 压缩包。
3. 确认安装成功后，Android Consolizer 会出现在已安装 Mod 列表中。其他 Mod（如 Generic Mod Config Menu）按同样方式安装。

**3. 启动游戏**
1. 返回 SMAPI Launcher 主界面，点击 **Start Game**（每次都必须从 SMAPI Launcher 启动，直接点系统原版星露谷图标不会加载任何 Mod）。
2. 连接手柄进入存档后，若底部工具栏变为 12 格固定行即说明加载成功。

如遇异常，请点击 SMAPI Launcher 的 **Share Log** 导出日志，并在本仓库提交 Issue，说明设备型号、手柄型号与复现步骤。

---

## 配置说明

可通过 **Generic Mod Config Menu** 或编辑 Mod 目录下的 `config.json` 修改以下设置：

- 在 GMCM 版本标题下找到「语言 (Language)」或「Language」。`Language` 支持 `Auto`（默认，跟随游戏语言）、`zh`（简体中文）、`en`（English）。游戏字体缺少汉字时，模组显示英文。要在这种情况下使用中文界面，先把游戏本体切换成简体中文。
- **手柄设置**：`EnableButtonRemapping`（按键重映射）、`ControllerLayout`（Switch / Xbox / PlayStation）、`ControlStyle`（Switch / Xbox）
- **核心开关**：`EnableConsoleChests`、`EnableConsoleShops`、`EnableConsoleAnimalMenu`、`EnableConsoleToolbar`、`EnablePickupToActiveRow`、`EnableConsoleInventory`、`EnableConsoleShipping`、`EnableRightStickCursor`
- **独立特性**：`EnableJournalButton`、`EnableCutsceneSkip`、`EnableCarpenterMenuFix`、`EnableFurnitureDebounce`、`EnableConsoleGeodeMenu`、`EnableConsoleFurniturePlacement`、`EnableConsoleCraftablePlacement`、`EnableHoldToCraft`、`EnableMuseumDonationController`、`EnableGameMenuNavigation`、`EnableMoveWhileCharging`、`EnableSlingshotAim`、`EnableConsoleDropBlocker`、`EnableConsoleDialogueCursor`、`FreeCursorOnSettings`、`HideTouchOptionsWithController`、`UseBumpersInsteadOfTriggers`、`ItemStowing`（`Off` / `GamepadOnly` / `Both`）
- **调试选项**：`VerboseLogging`（详细调试日志，默认关闭）

> 游戏内缩放比例与“工具命中位置”复选框位于游戏原生“选项（Options）”分页内，本 Mod 会将其记录于 `config.json`（`SavedZoomPercent`、`SavedAlwaysShowToolHit`、`SavedHideToolHitWhenMoving`）并在重启后自动恢复。

---

## 从源码构建

**环境要求**：`.NET 6 SDK`，以及本地已安装的《星露谷物语》（供 `Pathoschild.Stardew.ModBuildConfig` 解析游戏引用程序集）。

```bash
dotnet build --configuration Release
```

构建产物位于：`bin/Release/net6.0/`（同时会打包生成对应版本的 `.zip` 压缩包）。

---

## 故障排查

- **Mod 未加载**：确认已正确安装 Android 版 SMAPI，并检查日志路径 `/storage/emulated/0/StardewValley/ErrorLogs/`。
- **部分功能未生效**：在配置中临时开启 `VerboseLogging`，复现问题后查看 SMAPI 日志中的报错或状态输出（排查完建议关闭以防影响帧率）。
- **工具栏未显示 12 格**：确认 `EnableConsoleToolbar` 为 `true`；主机工具栏仅在正常游玩（非打开菜单状态）时显示。
- **Xbox 蓝牙手柄扳机无响应**：此为部分 Android 系统对 Xbox 蓝牙扳机的已知限制，请在配置中开启 `Use Bumpers Instead of Triggers`（肩键替代模式）。

---

## Mod 兼容性

- **Stardew Valley Expanded（SVE）** — 兼容
- **Content Patcher** — 兼容
- **Generic Mod Config Menu（GMCM）** — 兼容（可选，推荐）
- **Star Control** — **不兼容**（在 Android 上请勿同时启用）

---

## Fork 源头、上游致谢与开源协议

- **本项目定位**：由 [`miniworldcnmm/control-optimization`](https://github.com/miniworldcnmm/control-optimization) 维护的 **Android Consolizer 中文汉化与优化分支**。
- **上游原作者**：[`sonofskywalker3`](https://github.com/sonofskywalker3) — 原始仓库：[github.com/sonofskywalker3/AndroidConsolizer](https://github.com/sonofskywalker3/AndroidConsolizer) ｜ Nexus Mods：[Stardew Valley Mod #41869](https://www.nexusmods.com/stardewvalley/mods/41869)
- **核心生态致谢**：
  - [SMAPI](https://smapi.io/) 模组加载框架
  - [NRTnarathip](https://github.com/NRTnarathip/SMAPI-Android-1.6) 维护的 Android 版 SMAPI 移植项目
- **开源协议**：**MIT License** — 欢迎在遵守协议并保留上游署名的前提下自由修改与分发。
