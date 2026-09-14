# Star station / 星主题空间站

## English

The sunset-gradient revision of the current `warmup_station_v3` room. Indigo decks
and a starfield ceiling surround coral/violet/orange wall ribbons. A ringed sunset
disc replaces the old logo and welcome/name panel; the separate old technical-name
sign is also removed. The observation deck gains a ceiling constellation.

All seven SCP displays have remodeled silhouettes at their original primitive
budgets: 049 **12**, 079 **7**, 096 **14**, 106 **11**, 173 **11**, 939 **13**,
3114 **18**. Exhibit stands and SCP label positions remain fixed, preserving the
label-based selection-coin anchors. Embedded SCP assets use the same builders.

The separate creature's complete hierarchy, transforms and materials are unchanged.
The aim volume and complete parkour shaft, including entry signs, are unchanged.
The generator verifies exact equality for **658 protected blocks**. New decor has
visibility only and adds no collision. Existing shell collision is retained.
Lighting outside the protected areas is capped at 2.5 to retain the gradient colors.
Total: **1,051 blocks**, down from **1,090**. See [preservation report](preservation.json).

For installation, copy this folder to the plugin's `Schematics` directory and set
`authored_station_asset: warmup_station_stars` in the target port's configuration.
The plugin still creates the functional aim range from code. This asset does not
change activity settings. The old v3 file is retained as the immutable source;
existing installations continue to use their configured asset until switched.

Regenerate with `python tools/build_star_station.py` from the plugin directory.
[Generator](../../../tools/build_star_station.py) pins the v3 SHA-256 and refuses
to run against a changed baseline. [Model revision](../../../tools/star_gallery_models.py)
owns the final shapes on top of the original component recipes.

Validation: seven model geometry/readability contracts pass; the plugin builds with
zero warnings/errors; all 86 C# policy tests pass. Protected-block comparisons,
primitive counts, unique IDs and parent references are checked during generation.
[Gallery preview](../../previews/star-gallery.png) and
[observation preview](../../previews/star-observation.png) were rendered and inspected
with the C# D3D11 renderer. Its preview-only JSON remaps negative IDs because that
renderer rejects them; the installable asset retains original opaque IDs.
The renderer omits TextToys, shadows and bloom and approximates game lighting.
Native client appearance and gameplay have not been playtested for this revision.
No server configuration or deployed files were changed.

## 中文

本图纸以当前 `warmup_station_v3` 为基础，改为靛蓝地板、星空天花板与
橙色—珊瑚红—紫色渐变墙带。带环的落日星球替代旧 Logo 和欢迎／名称面板，
旧技术署名文字也已移除。观景舱新增天花板星座。

七个 SCP 展品均已重新塑形，图元数量保持不变：049 为 **12**、079 为 **7**、
096 为 **14**、106 为 **11**、173 为 **11**、939 为 **13**、3114 为 **18**。
展台与 SCP 标签坐标保留，因此基于标签定位的选择硬币锚点保持一致。
内嵌 SCP 模型使用同一套生成器。

独立生物雕塑的完整层级、变换与材质均保留。瞄准区和完整跑酷竖井（含入口标志）
保持不变；生成器逐项验证 **658 个受保护方块**完全一致。新增装饰没有碰撞，
原房间碰撞保留。改造区域的灯光强度上限为 2.5，避免冲淡渐变色。
总方块数从 **1,090** 降为 **1,051**。

安装时将本文件夹复制到插件的 `Schematics` 目录，并在目标端口配置中设置
`authored_station_asset: warmup_station_stars`。功能靶场仍由代码生成，活动开关不会改变。
原 v3 保留为不可变源文件；现有服务器在主动切换前仍使用原配置指定的图纸。

七项模型几何／辨识度检查与 86 项 C# 测试全部通过，插件构建无警告、无错误。
预览图已通过 C# 渲染器检查；该渲染器不显示世界文字、阴影或泛光，灯光也不完全等同
游戏客户端。本次改版尚未进行游戏内实测，未修改服务器配置或已部署文件。
