# 功能文档索引

本目录保存当前检出分支中所有独立能力域的规范功能文档，描述当前代码的实际行为、配置项、模块流程与防回归边界。

| 功能域 | 文档链接 | 简短说明 |
| --- | --- | --- |
| 手柄映射、全局快捷键与启动修复 | [controller-input-and-remapping.md](controller-input-and-remapping.md) | 底层 `GamePad.GetState` 映射与抑制、布局/风格切换、Start 短按/长按与过场跳过、SMAPI 启动白屏自愈 |
| 主机风格工具栏与拾取入当前行 | [console-toolbar-and-pickup.md](console-toolbar-and-pickup.md) | 12 格固定单行工具栏绘制、缩放与边距、LB/RB 换行、LT/RT 防抖移格、肩键模式及拾取物品入当前行 |
| 右摇杆自由光标与主机化实机玩法 | [right-stick-cursor-and-gameplay.md](right-stick-cursor-and-gameplay.md) | 右摇杆自由光标与情境指针、移动中蓄力工具、弹弓直瞄、单格家具/合成物/种子放置预览、家具防抖、物品收纳与防连发、掉落防秒捡 |
| 背包、箱子与出货箱主机操控 | [inventory-chests-and-shipping.md](inventory-chests-and-shipping.md) | 背包抓取/拆分/排序/装备提示、箱子快传/侧边栏/调色板/满格互换/X 键防误删、普通箱子、大箱子及冰箱解锁后在整理按钮左侧显示卷轴，空间不足时自动避让，触屏或手柄选中仍需材料时跳动，已提交槽位停止触发、出货箱、鱼竿与弹弓装卸 |
| 商店交易与玛妮动物菜单 | [shops-and-animal-menu.md](shops-and-animal-menu.md) | 商店购买/出售/批量调数/以物易物/配方学习/滚动保持、玛妮动物出售与移动确认修复 |
| 游戏主菜单、设置注入、对话框与标题界面 | [game-menus-and-options.md](game-menus-and-options.md) | GameMenu 分页导航与 LT/RT 切页、长按 A 连续合成、选项页注入与持久化、对话框主机选中样式、读档/标题页与多页信件 |
| 中英文切换与本地化 | [localization-and-language-switching.md](localization-and-language-switching.md) | SMAPI 翻译资源、GMCM 语言切换、SpriteFont 字形检查与回退 |
| 专项设施菜单（罗宾/社区中心/博物馆/晶球） | [special-menus-carpenter-cc-museum-geode.md](special-menus-carpenter-cc-museum-geode.md) | 罗宾建筑菜单防秒关与农田操控、社区中心献祭与奖励、博物馆捐赠重排、克林特敲晶球菜单与已知崩溃禁区 |
| 开发诊断与无手柄脚本测试工具 | [dev-testing-tools.md](dev-testing-tools.md) | 基于 `sim-input.txt` 的无手柄脚本模拟器、测试存档注入与配色工具脚本、`VerboseLogging` 诊断体系 |
