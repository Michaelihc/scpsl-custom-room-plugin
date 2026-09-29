# SCP:SL model references / SCP:SL 模型参考

The current **Render** image on each official English wiki page was inspected on
2026-09-14. The reference is the game's model, including current 173 and 939,
not legacy renders or the general SCP Foundation article's illustration.
Original reference images are not embedded in the plugin or distributed as assets.

| SCP | Wiki page | Exact inspected render | Features retained in the primitive study |
| --- | --- | --- | --- |
| 049 | [SCP-049](https://en.scpslgame.com/index.php?title=SCP-049) | [RenderSCP-049.png](https://hub.scpslgame.com/images/8/8e/RenderSCP-049.png) | Black hood/cowl, shoulder cape, long split robe, gray bird mask, black gloves. No brimmed hat. |
| 079 | [SCP-079](https://en.scpslgame.com/index.php?title=SCP-079) | [RenderSCP-079.png](https://hub.scpslgame.com/images/4/44/RenderSCP-079.png) | Beige CRT, black screen, white X, keyboard and disk drive. Peripheral batteries and tape deck omitted for budget. |
| 096 | [SCP-096](https://en.scpslgame.com/index.php?title=SCP-096) | [096rerender.png](https://hub.scpslgame.com/images/7/76/096rerender.png) | Small bald head, gaunt waist, prominent rib mass, long jointed limbs, low hanging hands. |
| 106 | [SCP-106](https://en.scpslgame.com/index.php?title=SCP-106) | [Scp10613.png](https://hub.scpslgame.com/images/d/d0/Scp10613.png) | Dark rotten skin, pale eyes/grin, forward bald head, cropped black vest and exposed abdomen. |
| 173 | [SCP-173](https://en.scpslgame.com/index.php?title=SCP-173) | [RenderSCP-173.png](https://hub.scpslgame.com/images/1/15/RenderSCP-173.png) | Asymmetric fused main/chest/side masses, tapered supports, lateral spurs, green/red marks. |
| 939 | [SCP-939](https://en.scpslgame.com/index.php?title=SCP-939) | [SCP-939tooth.png](https://hub.scpslgame.com/images/3/3f/SCP-939tooth.png) | Current 939-168 upright muscular body, two legs, clawed arms, eyeless muzzle, orange fangs, long head spines; no legacy dog tail. |
| 3114 | [SCP-3114](https://en.scpslgame.com/index.php?title=SCP-3114) | [SCP-3114_V2.png](https://hub.scpslgame.com/images/a/ad/SCP-3114_V2.png) | Adult rounded skull, dark sockets, exposed sternum/ribs, pelvic wings, narrow jointed limbs. |

The model generator is `tools/star_gallery_models.py`. The tests in
`tests/models/scpsl_model_contract.py` protect the identity features above as well
as budgets, bounds, named markers and primitive-format validity. Fine texture,
fingers, facial anatomy and irregular surface damage are simplified at this budget.

## 中文

2026-09-14 逐一检查了官方英文 Wiki 各页面当前的 **Render** 模型渲染图。
参考对象是 SCP:SL 游戏模型，包括现行 173 和 939；不使用旧版模型或一般 SCP
基金会条目的插图替代。上表链接保留了精确来源，原参考图片不会内嵌或作为插件素材发布。

049 保留兜帽、披肩、长袍、灰色鸟嘴面具与黑手套；079 保留米色 CRT、黑底白 X、
键盘和磁盘驱动器；096 保留小头、突出胸廓、瘦腰和垂至膝下的长臂；106 保留腐朽
皮肤、浅色眼睛和牙齿、前倾光头、黑色短背心及外露腹部；173 保留不对称混凝土团块、
侧刺、绿色与暗红标记；939 保留现行双腿直立形态、利爪、无眼口鼻部、橙色獠牙和长
头刺；3114 保留成人颅骨、深色眼窝、开放胸廓、骨盆翼与细长关节四肢。

为维持低图元数量，细小纹理、手指、精细面部解剖与不规则破损被简化。模型生成器与
独立身份／几何契约的源文件路径见上方英文说明。
