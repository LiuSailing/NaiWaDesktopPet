# 图片、图标与羽翼来源及许可

核对日期：2026-11-13。本目录保留运行素材、图标原图与编辑定义，以及生成过程和上游许可记录。

本目录图片、图标及其他素材不纳入项目根目录的代码 MIT 授权。仅 `reference-footprint.png` 有下述保留的上游 MIT 许可记录；其他条目按各自状态处理。

| 文件 | 用途与处理方式 | 许可状态 |
| --- | --- | --- |
| `idle-hd-reference-match.png` | 用户提供参考图，经 AI 生成、调整的高清待机图；大笑共用其身体 | 原参考图及角色相关授权待核实 |
| `laugh-peak-expression.png` | 根据参考图生成的大笑表情 | 原参考图及角色相关授权待核实 |
| `listen-thinking.png` | 根据参考图生成的托腮沉思姿势 | 原参考图及角色相关授权待核实 |
| `rest-supine.png` | 根据参考图生成的仰躺姿势 | 原参考图及角色相关授权待核实 |
| `rest-exhale-mouth.png` | 根据躺平图生成的局部呼气口型 | 随对应角色素材，授权待核实 |
| `flight-wing.png` | 根据用户羽翼参考，通过内置 imagegen 生成的独立透明羽翼；程序镜像成双翼 | 参考来源及独立素材许可待确认 |
| `shortcut-face-original.png` | 根据用户截图提取的原始平面奶蛙表情 | 原图及角色相关授权待核实 |
| `shortcut-face.svg` | 使用原始表情进行颜色调整、圆角裁切的编辑定义 | 所引用原图的相关授权待核实 |
| `naiwa-face-rounded.ico` | 按上述原图与编辑定义导出的多尺寸快捷方式图标 | 随所使用原图，授权待核实 |
| `reference-footprint.png` | 180×180 原版待机占位模板，用于保持大小、比例和脚底位置 | 上游标注 MIT，原文保留在本目录 `LICENSE.txt` |

## 参考图来源

项目维护者说明，角色、姿势和图标的参考图片主要从闲鱼、抖音取得，再经 AI 生成、高清化或局部处理。各原帖的具体链接、作者及授权尚未完整对应，不能仅凭发布平台名称确认版权归属或再分发权限。

AI 处理过程记录在 [素材生成记录](GENERATION-NOTES.md)。这些生成记录用于说明处理方式，不构成原参考图、角色形象或第三方素材的授权证明。项目目前未为上述待核实素材授予新的统一许可。

## 占位模板的上游来源

`reference-footprint.png` 是此前 `original/idle/000.png` 的原样副本，PNG 字节未修改。来源记录：

- [NaiWa-Wallpaper 参考素材](https://github.com/Nailong-Studio/NaiWa-Wallpaper/tree/main/pet)
- [liaosongjie/Codex-Voice-Input 原始帧与素材说明](https://github.com/liaosongjie/Codex-Voice-Input/tree/main/assets/pet)
- [保留的上游 MIT 许可](LICENSE.txt)，版权声明为 `Copyright (c) 2026 Codex Voice Input contributors`
- [保留的上游素材说明原文](ASSET-NOTICE.md)

`ASSET-NOTICE.md` 中提到的原始视频、动画和 `sounds/laugh.wav` 等文件属于上游项目说明，不是当前全部图片或 `LaughAloud.mp3` 的统一许可声明。上游 MIT 记录也不代表本项目已独立核实角色形象涉及的全部权利。

## 动画生成方式

角色身体仍使用原有高清待机图。眨眼、呼吸、大笑、跳跃、飞行和呼气等动作由程序围绕相应高清素材生成，来源与许可按上表对应到各素材文件。
