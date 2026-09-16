# Star station / 星主题空间站

## English

The sunset-gradient revision of the current `warmup_station_v3` room. Indigo decks
and a starfield ceiling surround coral/violet/orange wall ribbons. A ringed sunset
disc replaces the old logo and welcome/name panel; the separate old technical-name
sign is also removed. The observation deck gains a ceiling constellation.

The seven SCP displays now follow the current official SCP:SL Wiki model renders,
using natural proportions and simplified primitive silhouettes. Counts: 049 **15**,
079 **13**, 096 **20**, 106 **21**, 173 **18**, 939 **27**, 3114 **28**.
See the [reference images and feature decisions](model-references.md),
[lineup](../../previews/scpsl-lineup.png), [173 side view](../../previews/scpsl-173.png)
and [939 side view](../../previews/scpsl-939.png).

049 has a hood and long robe; 079 has the white CRT X; 096 has a gaunt rib cage and
long hanging arms; 106 has exposed rotten skin and a cropped black vest; 173 uses
Matthew's asymmetric concrete lobes; 939 uses the current upright 939-168 body,
head spines and claws; 3114 uses adult skull, rib-cage and pelvis proportions.
These are small primitive approximations, not imported game meshes or textures.
The embedded assets and authored room share the same final model recipes.

Exhibit stands and label X/Z coordinates remain fixed. Only the 173 and 939 labels
are raised to clear their revised silhouettes, preserving the horizontal selection
coin anchors used by `Import/AuthoredGalleryAnchors.cs`.

The separate creature's complete hierarchy, transforms and materials are unchanged.
The aim volume and complete parkour shaft geometry, including entry signs, are unchanged.
The generator verifies equality for **658 protected blocks** before applying the light adjustment. New decor has
visibility only and adds no collision. Existing shell collision is retained.
Every light's intensity and range are multiplied by **sqrt(3), approximately 1.732**
relative to the initial star room. The two setting multipliers have a product of 3;
this is not a measurement of perceived brightness. Gallery/hub/observation lights
change from intensity 2.5 to 4.330 and range 18 to 31.177. Code-owned Aim lights use
the same gain. Light colors, positions, room materials and geometry stay unchanged.
Total: **1,107 blocks**, compared with **1,090** in the original. See [preservation report](preservation.json).

For installation, copy this folder to the plugin's `Schematics` directory and set
`authored_station_asset: warmup_station_stars` in the target port's configuration.
The plugin still creates the functional aim range from code. This asset does not
change activity settings. The old v3 file is retained as the immutable source;
existing installations continue to use their configured asset until switched.

Regenerate with `python tools/build_star_station.py` from the plugin directory.
[Generator](../../../tools/build_star_station.py) pins the v3 SHA-256 and refuses
to run against a changed baseline. [Model revision](../../../tools/star_gallery_models.py)
owns complete final recipes; the seven role builders are simple export entry points.

Validation: seven model geometry/readability contracts pass; the plugin builds with
zero warnings/errors; all 86 C# policy tests pass. Protected-block comparisons,
primitive counts, unique IDs and parent references are checked during generation.
[Gallery preview](../../previews/star-gallery.png) and
[observation preview](../../previews/star-observation.png) were rendered and inspected
with the C# D3D11 renderer. Its preview-only JSON remaps negative IDs because that
renderer rejects them; the installable asset retains original opaque IDs.
The renderer omits TextToys, shadows and bloom and approximates game lighting.
Native client appearance and gameplay have not been playtested for this revision.
The initial room is deployed on Rain Yun; the brighter revision uses a separately
named `warmup_station_stars_bright` asset there to preserve rollback to the initial room.

## 中文

本图纸以当前 `warmup_station_v3` 为基础，改为靛蓝地板、星空天花板与
橙色—珊瑚红—紫色渐变墙带。带环的落日星球替代旧 Logo 和欢迎／名称面板，
旧技术署名文字也已移除。观景舱新增天花板星座。

七个 SCP 展品改为参考当前 SCP:SL 官方 Wiki 模型渲染图的简化图元造型。
图元数量：049 为 **15**、079 为 **13**、096 为 **20**、106 为 **21**、
173 为 **18**、939 为 **27**、3114 为 **28**。模型使用自然比例与主要轮廓，
不是导入的原游戏网格或贴图；来源与造型依据见[参考说明](model-references.md)。

049 改为兜帽长袍，079 使用白色 X 屏幕，096 恢复瘦削胸廓与下垂长臂，106 使用
腐朽皮肤和敞胸短背心，173 使用 Matthew 的不对称混凝土团块，939 使用现行直立
939-168 的头部尖刺与利爪，3114 使用成人颅骨、胸廓和骨盆比例。
展台与标签的 X/Z 坐标保持不变；仅抬高 173、939 标签以避开模型，选择硬币的水平
锚点不变。内嵌模型与房间图纸使用同一套最终生成配方。

独立生物雕塑的完整层级、变换与材质均保留。瞄准区和完整跑酷竖井（含入口标志）
几何保持不变；生成器在调整灯光前逐项验证 **658 个受保护方块**完全一致。新增装饰没有碰撞，
原房间碰撞保留。所有灯光的强度和范围分别乘以 **√3（约 1.732）**，两项倍率相乘为 3。
这不是对实际观感亮度的测量；灯光颜色、位置、房间材质与几何保持不变，代码生成的靶场灯光使用相同倍率。
总方块数为 **1,107**，原房间为 **1,090**。

安装时将本文件夹复制到插件的 `Schematics` 目录，并在目标端口配置中设置
`authored_station_asset: warmup_station_stars`。功能靶场仍由代码生成，活动开关不会改变。
原 v3 保留为不可变源文件；现有服务器在主动切换前仍使用原配置指定的图纸。

七项模型几何／辨识度检查与 86 项 C# 测试全部通过，插件构建无警告、无错误。
预览图已通过 C# 渲染器检查；该渲染器不显示世界文字、阴影或泛光，灯光也不完全等同
游戏客户端。本次改版尚未进行游戏内实测，未修改服务器配置或已部署文件。

Preview regeneration (from the metarepo root):

```powershell
dotnet .tools/mer-render/csharp/bin/Release/net10.0/MerRender.dll warmup-scp-selector/generated/previews/scpsl-lineup.mer.json -o warmup-scp-selector/generated/previews/scpsl-lineup.png --eye -8.4,4,20 --look -8.4,1.2,0 --ortho 7 --size 2100x700 --exposure 1.8 --language en
```
