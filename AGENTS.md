# AGENTS.md

本文件定义仓库的共享架构、长期工程约束与文档维护规范，纳入 Git 版本控制，适用于所有分支。

> **规则读取顺序与优先级**
>
> 1. 协作开发前，先阅读本文件（`AGENTS.md`）。
> 2. **当仓库根目录存在本地 `rule.md` 时，应在阅读 `AGENTS.md` 后继续读取 `rule.md`**（`rule.md` 已被 `.gitignore` 忽略，仅保存用户个人与本机协作规则）。
> 3. **`rule.md` 不得覆盖本文件中定义的共享架构、安全、数据一致性或 CI 约束。**

---

## 1. 项目概览与定位

- **仓库性质**：本项目（`control-optimization`）Fork 自 [`sonofskywalker3/AndroidConsolizer`](https://github.com/sonofskywalker3/AndroidConsolizer)，是 Android 版《星露谷物语》手柄主机化 Mod **Android Consolizer** 的中文汉化与持续优化版本（MIT 协议）。
- **目标运行时**：Android 端 Stardew Valley 1.6.15+，运行于 SMAPI 4.0+ for Android。
- **构建技术栈**：C# / `.NET 6`（`AndroidConsolizer.csproj`），依赖 `Pathoschild.Stardew.ModBuildConfig` (`4.*`)，启用 `<EnableHarmony>true</EnableHarmony>`，`<Nullable>disable</Nullable>`，`<LangVersion>latest</LangVersion>`。
- **可选软依赖**：`spacechase0.GenericModConfigMenu`（`manifest.json` 中 `IsRequired: false`）。

---

## 2. 目录结构与职责边界

```text
.
├── AGENTS.md                        # 共享架构、工程约束与文档维护规范（Git 追踪）
├── rule.md                          # 本地个人与本机规则（Git 忽略，可选存在）
├── README.md                        # 面向用户的中文项目说明与 Fork 来源声明
├── document/                        # 当前分支的规范功能文档目录（唯一功能文档来源）
│   ├── README.md                    # 功能文档索引
│   └── <feature-slug>.md            # 各独立功能域的当前行为规格
├── AndroidConsolizer.csproj         # .NET 6 项目与构建配置（排除历史 Tests 目录）
├── manifest.json                    # SMAPI Mod 清单（版本、入口 DLL、依赖）
├── config.json                      # 仓库内示例默认配置文件
├── ModEntry.cs                      # Mod 入口、事件调度、补丁集中注册、GMCM 注册、工具栏/Start 状态机
├── ModConfig.cs                     # 持久化配置模型（功能开关、布局/风格、Android 选项持久化字段）
├── I18n.cs                          # SMAPI 翻译解析与 SpriteFont 字形保护
├── GmcmLocalization.cs              # GMCM 语言预览、表单刷新与模组简介
├── i18n/                            # 键与占位符一致的英文及简体中文翻译
│   ├── default.json
│   └── zh.json
├── ButtonRemapper.cs                # ControllerLayout / ControlStyle 枚举与透传兼容层
├── Patches/                         # Harmony 补丁与运行期适配模块（共 29 个文件，单一职责拆分）
├── tools/                           # 开发者辅助脚本（测试存档注入脚本、UI 配色工具）
└── .github/workflows/               # GitHub Actions 工作流（发布自动化配置）
```

**职责与边界约束**：
- **入口集中编排**：`ModEntry.Entry()` 负责读取配置、实例化统一 `Harmony` 并按顺序调用各 `Patches/*.Apply(harmony, monitor)`，随后挂载 SMAPI 生命周期事件；不得在业务补丁内部散乱创建额外的顶层事件订阅（`InputSimulator` 拥有独立的 `.InputSim` Harmony ID 属于显式测试隔离）。
- **静态补丁模块化**：`Patches/` 下每个文件对应一个清晰的游戏界面或子系统（如商店、箱子、工具栏、右摇杆光标、选项页注入等），尽量避免补丁之间形成双向强耦合；跨模块共享的底层原始输入状态统一由 `GameplayButtonPatches` 暴露只读/内部字段，工具栏行状态经由 `ModEntry.SetCurrentToolbarRow` 同步到 `FarmerPatches.CurrentToolbarRow`。
- **单向依赖方向**：`ModEntry` → `ModConfig` / `ButtonRemapper` / `Patches/*`；`Patches/*` 仅通过 `ModEntry.Config`、`ModEntry.ModHelper`、`ModEntry.ModMonitor` 及必要的公开静态协调方法访问入口层。

---

## 3. 核心运行时架构与输入管线

本 Mod 的核心挑战是在 Android 移动版输入层与原生主机控制器路径之间重建主机体验，单帧（Tick）核心执行顺序如下：

1. **底层手柄状态拦截（`GameplayButtonPatches`）**
   - Patch `GamePad.GetState(PlayerIndex)` 与 `GamePad.GetState(PlayerIndex, GamePadDeadZone)`（通过 `_inOneParamGetState` 防止重入，并按 `Game1.ticks` 做同帧缓存保证帧内一致性）。
   - 最先混入 `InputSimulator.Apply(__result)` 的脚本模拟按键（仅存在 `sim-input.txt` 时生效）。
   - 缓存原始右摇杆、左摇杆、模拟与数字扳机（将纯数字 ZL/ZR 折算为 `1.0f`）、原始 `Start` 状态，以及最终游戏侧工具键（`Buttons.X`）的按住/上升沿/下降沿状态。
   - 按上下文归零冲突输入（商店/社交页右摇杆、选项页左摇杆、禁用光标或装备弹弓时的大世界右摇杆、主机工具栏接管时的模拟与数字扳机），执行 A/B 与 X/Y 映射及按需按键抑制（如关闭箱子防误触、过场跳过与 Start 长按检测）。
2. **帧前强制校正（`ModEntry.OnUpdateTicking`）**
   - 快照 `_preUpdateToolIndex`，并在存在有效 `_triggerSlotTarget` 时于 `Game.Update` 运行前强制锁定 `CurrentToolIndex`，防止原版扳机逻辑叠加跳格。
3. **游戏主更新与各 Harmony 补丁**
   - 商店、箱子、背包、制作、社区中心、罗宾建筑、博物馆、晶球、选项页、对话、弹弓、工具蓄力、物品收纳、右摇杆瞄准等补丁在各自目标方法的前后置钩子中生效。
4. **帧后状态机与同步（`ModEntry.OnUpdateTicked`）**
   - 驱动工具蓄力/弹弓/右摇杆诊断与指针保持、Start 键短按菜单/长按任务日志状态机、Start 双击跳过过场、家具防抖清理、背包抓取态刷新、工具栏双阈值扳机状态机与行锁定，以及 `InputSimulator.Tick()` / `ItemStowPatches.Tick()`。
5. **菜单切换与 HUD 绘制（`OnMenuChanged` / `OnRenderedHud` / `OnSaveLoaded`）**
   - 在 `MenuChanged` 中完成博物馆 `SnappyMenus` 还原、背包/箱子/罗宾/社区中心/晶球/游戏菜单状态复位及选项页持久化；在 `RenderedHud` 中自绘大世界右摇杆情境光标；在 `SaveLoaded` 中重装备当前工具并恢复持久化缩放、工具命中框及 `Options.stowingMode`。

---

## 4. 关键工程红线与防回归约束

修改或新增补丁时必须严格遵守以下经实机验证的红线：

1. **Android vs PC 编译期反射安全**
   - 本项目在编译期链接 PC 版星露谷引用程序集，但运行于 Android 版游戏程序集。
   - 所有 Android 独有成员（如 `Options.toolbarSlotSize`、`Item._itemSlotSize`、`Toolbar._itemSlotSize`、`Item.drawInToolbar`、`Game1.toolbarPaddingX`、`Character.GetLocationNextToWhereYoureFacing`、`AnimalQueryMenu._selectedButton`、`OptionsElement.ItemHeight`、`OptionsSlider` 5 参构造器、`MobileScrollbox`、`PinchZoom`、`QuestLog(int)` 构造器等）**禁止直接硬引用**，必须通过 `AccessTools` / 反射按名称与签名动态解析并缓存字段/方法引用，且做好 `null` 检查与异常捕获回退，避免 PC 端编译失败或运行时崩溃。
2. **已知运行时硬崩溃禁区**
   - **严禁使用 Harmony Patch `GeodeMenu.releaseLeftClick`**：已知会导致 Android 运行时硬崩溃（详见 [document/special-menus-carpenter-cc-museum-geode.md](document/special-menus-carpenter-cc-museum-geode.md)）。
3. **Android 合成触摸陷阱与 X 键防误删**
   - Android 原生层会将部分手柄确认转换为合成触摸事件，从而改变 `Game1.lastCursorMotionWasMouse` 或额外触发 `receiveLeftClick` / `releaseLeftClick`。菜单与对话框补丁不可盲目依赖 `gamepadControls && !lastCursorMotionWasMouse` 门控。
   - 在背包与箱子界面，必须始终拦截原始 `SButton.ControllerX`（当开启箱子/背包修复时），防止触发 Android 原版删除槽位物品的缺陷。
4. **热路径性能与日志节流**
   - 禁止在 `Update`、`draw`、`GetState` 等高频回调中输出未门控的日志；Android 同步日志 I/O 会直接导致主线程卡顿与按键边沿丢失。
   - 所有诊断日志必须受 `ModEntry.Config.VerboseLogging` 门控，且对连续值/状态变化做边沿去重或变化节流。
5. **配置与持久化一致性**
   - 新增或修改 `ModConfig.cs` 属性时，必须同步更新 `ModEntry.RegisterConfigMenu()` 中的 GMCM 选项及对应功能文档。
   - Android 原版 `StartupPreferences` 不持久化缩放比例与部分选项，因此 `SavedZoomPercent`、`SavedAlwaysShowToolHit`、`SavedHideToolHitWhenMoving` 需在选项页关闭/变更时写入 `ModConfig` 并在 `SaveLoaded` 时重放；`ItemStowing` 需在 `SaveLoaded` 和 GMCM 保存时同步至 `Game1.options.stowingMode`。
6. **安全与发布边界**
   - 严禁将任何 API Key、浏览器 Cookie、凭据文件、本机绝对路径、私有测试存档或本地 `rule.md` 纳入 Git 追踪。
   - 注意 [.github/workflows/publish-nexus.yml](.github/workflows/publish-nexus.yml) 仍保留上游仓库的 Nexus `file_id`（`7118491`）；在本 Fork 尚未明确调整发布目标前，**禁止误触发向 Nexus Mods 的发布工作流**，亦不得擅自改动 CI 凭据或工作流权限。
7. **本地化与字体安全**
   - 模组提供的玩家界面文案通过 `I18n.Get` 获取，`i18n/default.json` 和 `i18n/zh.json` 的键、占位符必须一致。既有配置键、枚举存储值及技术日志沿用英文。
   - 向 GMCM 或游戏 SpriteFont 提供文本前，检查当前字体字形。中文字体不可用时回退英文，缺失的 PlayStation 符号优先替换为对应按键文字。仅在生命周期或设置回调中记录一次字体回退。详见[中英文切换与本地化](document/localization-and-language-switching.md)。

---

## 5. 构建、测试与验证要求

- **标准编译命令**（需具备 `.NET 6 SDK` 及可解析的 Stardew Valley 游戏目录）：
  ```bash
  dotnet build --configuration Release
  ```
- **无物理手柄的设备测试**：可通过 `Patches/InputSimulator.cs` 在设备 Mod 目录投放 `sim-input.txt` 执行脚本化按键序列并观察 SMAPI 日志；也可按需使用 `tools/` 下的 PowerShell 存档注入脚本构造社区中心/博物馆测试场景（使用前脚本会自动备份原存档）。
- **纯文档变更验证**：仅修改文档时不要求执行 `dotnet build`；须执行 `git diff --check`、校验 Markdown 相对链接、核对 `document/README.md` 与实际文件一致、确认 `rule.md` 被 Git 忽略且未追踪，并扫描共享文档确认不含凭据与本机绝对路径。

---

## 6. 功能文档维护规范

- 仓库使用 `document/` 作为当前分支唯一的规范功能文档目录：
  - [document/README.md](document/README.md) 仅维护功能名称、相对链接与简短说明，不复制正文、不记录提交哈希、集成日志或本机环境信息。
  - 每个独立产品能力域维护一个稳定路径 `document/<feature-slug>.md`，后续修复与增强应原地更新，不按分支或发布状态复制副本。
- **同步更新触发条件**：当变更影响用户可见行为、手柄键位映射、Harmony 补丁契约、配置项、持久化、安全或跨模块流程时，**必须在同一次变更中创建或更新对应的 `document/<feature-slug>.md`**（必要时更新 `document/README.md`）。仅修改文案、纯样式微调或不改变外部行为的内部整理不要求新建功能文档。
- **事实来源原则**：修改已有功能前先读对应功能文档；当代码与文档不一致时，以当前分支实际代码、配置与测试为准核实后同步修正文档。禁止在功能文档中写入 Git 集成状态、本机绝对路径、凭据或一次性命令日志。

---

## 7. 提交与协作规范

- 仅在用户明确授权后执行 Git 提交、推送、创建或合并 Pull Request。
- Git 提交信息统一使用**中文**，简明描述变更意图与影响范围。
- 进行中文汉化或功能优化时，保持与上游代码结构的清晰映射与低耦合，避免无意义的大范围重构，便于按需对比或吸收上游修复。
