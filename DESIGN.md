---
name: "地宫争锋 / Tomb Duel — Title Extension"
description: "独立 Unity / PICO XR 启动封面的已实现视觉规范；不替代牌局 UI。"
colors:
  jade-rest: "rgb(3.5% 6.4% 5.4% / 94%)"
  jade-hover: "rgb(14% 26% 21% / 98%)"
  bronze-edge: "rgb(61% 49% 28% / 95%)"
  gold-edge-hover: "rgb(100% 84% 48%)"
  gold-label: "rgb(94% 86% 65%)"
  ivory-label-hover: "rgb(100% 97% 83%)"
  tomb-dark: "rgb(0.8% 1.2% 1%)"
typography:
  label:
    fontFamily: "LegacyRuntime.ttf"
    fontWeight: 400
rounded:
  square: "0px"
components:
  title-button:
    backgroundColor: "{colors.jade-rest}"
    textColor: "{colors.gold-label}"
    typography: "{typography.label}"
    rounded: "{rounded.square}"
  title-button-hover:
    backgroundColor: "{colors.jade-hover}"
    textColor: "{colors.ivory-label-hover}"
    typography: "{typography.label}"
    rounded: "{rounded.square}"
---

# Design System: 地宫争锋 / Tomb Duel — Title Extension

## Overview

**Creative North Star: "青铜玉印地宫入口"**

本规范记录独立启动封面的现状。用户于 2026-09-08 确认沿用 TOMB DUEL / GUANDAN TREASURE、中央徽记、暗黑地宫、青铜与玉石，以及下方菜单的参考方向。地宫建筑与古金标题承担画面气氛，实时按钮承担清晰的选择与反馈。

作用域仅为启动封面扩展。现有 GuandanTomb 的牌局、模型、相机、牌面 UI 与规则继续由原实现定义；本文件不建立全局牌局的新规范。

**Key Characteristics:**

- 中央圆形玉印、两侧雕刻石壁与大型古金英文标题。
- 两个垂直居中的实时控件，以玉色底和细青铜边回应交互。
- 固定世界空间画布，桌面窄窗口通过相机取景保留完整菜单。

依据为 `Assets/Scripts/Title/TombTitleBootstrap.cs`、`PRODUCT.md`、`DesignSources/Title/来源与设计.md`，以及 `VisualQA/Title-20260908/` 下的 `01-title.png`、`02-title-compact.png`、`03-pressed.png`、`05-desktop-live.png`。独立 finish reviewer 结论为 **ship，无 material fixes**。这些图像覆盖桌面常规、紧凑、按下和运行画面；本记录没有将它们当作 PICO 真机或模拟器交互验证，也不宣称已验证头显舒适度。

## Colors

画面以低亮度的石壁、暗玉与青铜组织层次；古金标题和按钮文字提供主要亮度对比。前置色值忠实记录 Unity `Color` 的归一化通道，以 CSS 百分比表达，属于控件输入色，不能作为截图像素的精确色样。

### Primary

- **暗玉底 / jade-rest**：两个按钮的静止填充。
- **亮玉底 / jade-hover**：鼠标、控制器指向或键盘聚焦后的填充。

### Secondary

- **青铜细边 / bronze-edge**：静止轮廓；与场景雕刻的材质呼应。
- **金色亮边 / gold-edge-hover**：指向或聚焦时增强轮廓。
- **古金字 / gold-label**：实时按钮标签的静止颜色。
- **暖象牙字 / ivory-label-hover**：按钮指向或聚焦后的文字颜色。

### Neutral

- **地宫暗场 / tomb-dark**：相机清屏和切场淡出使用的暗色。
- 操作提示使用独立的灰玉色（Unity RGB 0.79、0.81、0.71），仅记录该说明文字的现状，不扩展为全局正文色。

## Typography

大型 TOMB DUEL 与副标题 GUANDAN TREASURE 均已烘焙在 `Assets/Resources/GuandanUI/TitleCover.png` 中。其古金衬线字形、纹理及相对比例属于图像资产；没有可提取或要求安装的显示字体。不要用推测的字体名称重建它们。

实时文字使用 Unity 内置 `LegacyRuntime.ttf`，Normal、居中对齐。ENTER TOMB 为 32 个画布字号单位，EXIT 为 27，操作提示为 20；世界空间投影会改变屏幕像素大小。这是启动场景的小型操作层级，不作为游戏全局字体标尺。

**The Asset Lettering Rule.** 主标题与副标题随已批准封面资产维护，功能标签继续以实时文本绘制。

## Layout

画布为 WorldSpace，设计矩形 1920 × 1080，统一缩放 0.004；对应世界空间宽 7.68、高 4.32 个 Unity 单位。按项目默认米制解释为 7.68 × 4.32 米。2026-09-09 按用户截图将完整封面及控件上移 0.32 米，画布中心现为 `(0, 1.94, 0)`；设计眼位 `(0, 1.72, -4.25)` 与原视线参考 `(0, 1.62, 0)` 不变。背景 RawImage 铺满画布；源图生成尺寸为 1344 × 768，当前实现映射到 16:9 画布，没有运行时保宽高裁切组件。新视觉与交互证据位于 `VisualQA/Title-20260909-Raised/`。

两个按钮沿中心轴排列：ENTER TOMB 的中心偏移为 `(0, -328)`、尺寸 424 × 76，对应 1.696 × 0.304 个世界单位；EXIT 为 `(0, -430)`、300 × 64，对应 1.2 × 0.256 个世界单位。操作提示中心偏移为 `(0, -508)`，文本区为 1600 × 32。这里的布局数字是画布单位，不能直接视为设备像素或推荐的 VR 可读性阈值。

桌面相机始终看向设计中心，基础垂直视场角为 58°。当宽高比较窄时，按 `max(58°, 2 × atan(3.98 / (4.25 × max(0.6, aspect))))` 扩大视场；紧凑截图显示上下暗场与完整封面。没有移动端断点或按钮重新排版系统。

XR 开始时使用已跟踪头部姿态校正 origin，将眼位与水平朝向放到设计起点；尝试 Floor tracking，无法取得有效本地位置时以 1.60 米作为设计眼高回退。R 键可重新对齐。此为代码实现的适配策略，真机视野、可达性和舒适度仍需在设备上验证。

## Elevation & Depth

地宫的透视、石刻浮雕、雾与明暗来自单张静态图像。实时按钮使用纯色填充、细边和缩放反馈，没有额外投影阴影。按钮的命中根节点位于画布前 4 个画布单位（0.016 个世界单位），过渡遮罩位于画布前 5 个画布单位（0.020 个世界单位）；这只用于叠放，不能推断为完整三维地宫场景。

## Shapes

按钮为直角矩形，四条独立的 1.5 画布单位青铜线构成轮廓。入口比退出更宽、更高；两者共享状态语言。中央圆形印记及雕花轮廓保留在图像内，不被提取为新的可交互图标系统。

## Components

### 封面图像

已批准视觉资产来自 Tripo Nano Banana image-to-image，任务 `58bafa5a-e0c2-45c1-91d6-c21beba218ec`。来源与检索记录见 `DesignSources/Title/来源与设计.md`；[生成图源](https://tripo-data.rg1.data.tripo3d.com/tcli_c6fae067c9334bd9af3d07029c0e7b00/20260908/58bafa5a-e0c2-45c1-91d6-c21beba218ec/generated_image.png)。该图像未承载可点击按钮，菜单由 Unity 实时绘制。

### 菜单按钮

两个按钮共用 `TitleActionButton`。指向与键盘聚焦共用同一视觉状态：填充、边框和文字向高亮色插值，视觉放大 3.5%。按下贡献负 5.5% 缩放，叠加公式为 `1 + hover × 0.035 - press × 0.055`；完全指向且按住时为 0.98，不能将其记录为固定 0.945。过渡采用 `1 - exp(-18 × unscaledDeltaTime)` 平滑。

鼠标或 XR 指针按下时记住目标，在同一按钮上释放才激活；移走后释放不执行，应用失焦或跟踪失效取消按下。视觉子节点缩放，BoxCollider 命中区域保持原尺寸、厚度为 12 画布单位。键盘 Tab / ↓ 循环前进，↑ 反向循环，Enter / Space 激活；尚未选中时默认入口。Escape 退出，鼠标移动可清除键盘聚焦。

XR 控制器显示左右两条射线，左为玉绿、右为暖金，线宽从 0.006 收至 0.002 世界单位；没有目标时延伸 8 个世界单位。当前实现沿用牌桌约定，以头部位置为射线起点、控制器朝向为方向，显示与命中使用同一样本。可见线段沿同一射线从眼位前至少 0.35 米开始，剔除近裁面及侧后方片段，避免投影原点产生三角遮挡；点击起点不变。触发键或模拟轴达到 0.65 时按下，若设备支持则在按下起始帧给予幅度 0.22、时长 0.045 秒的触觉反馈。Android 非编辑器分支另接 PICO 手部 AimRay / pinch 输入；未绘制独立手部射线。这些为已实现代码路径，不能代替 XR 运行验证。

### 加载与退出

入口启动后防止重复激活，按钮进入不可交互状态，标签 RGB 乘以 0.70，说明更换为 `OPENING THE TOMB...`。等待 0.20 秒后异步加载 GuandanTomb；就绪后以 0.25 秒暗场淡出，再以 Single 模式激活牌局。无法取得加载操作时恢复按钮并显示重试说明。退出显示 `LEAVING THE TOMB`，等待 0.18 秒后退出应用；编辑器中结束播放。

`.impeccable/design.json` 附带两个按钮的浏览器预览，明确只是 Unity 控件的示意翻译。Unity 源码及本文件的世界空间尺寸是事实依据，CSS 字体回退及时间曲线不构成新实现。

## Do's and Don'ts

### Do:

- **Do** 维持已批准的 TOMB DUEL / GUANDAN TREASURE 图像标题和青铜玉石地宫方向。
- **Do** 让入口、退出的指向、键盘聚焦和按下反馈继续共享同一状态语言。
- **Do** 在桌面窄窗口继续检查完整菜单，并单独验证真实 XR 视野、操作和舒适度。
- **Do** 将本规范限定为启动封面扩展，保留现有牌局实现的权威性。

### Don't:

- **Don't** 将图像标题误记为运行时字体或把整张背景误记为三维场景。
- **Don't** 从桌面截图推断真机、模拟器、触觉或手部跟踪已经通过验证。
- **Don't** 将启动封面的中轴构图、内置操作字体或字号推广成全局牌局的新规范。
