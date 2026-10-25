# INPUT-001 快速移鼠受惊功能移除交接

后续更新：头部拖动修复见 [INPUT-002](INPUT-002-head-drag-fix.md)，当前启动路径以STATUS为准。下文302项及mouse-reaction-removal路径描述本次移除交付时的版本，不用于覆盖后续修复。

时间：2026-10-25，Asia/Shanghai。用户明确要求完全移除并授权修改产品代码；需求裁决见 [D-009](../DECISIONS.md#d-009取消快速移鼠受惊功能)，当前行为以 PRODUCT R-01 为准。本助手是本次修复实现方，下面记录交付自测，不代签独立监理结论。

## 当前交付

- 删除 Core 的 SwipeDetector/MouseSampler 及其共34项专用测试；不留下默认关闭开关、速度阈值、距离阈值、冷却或未来恢复待办。
- MainWindow移除速度采样/DPI缓存/自动dodge及相关公开诊断访问器。约30Hz直接读取当前鼠标位置供悬停，继续使用当前帧Alpha与实际窗口坐标映射。悬停仍为1.2秒触发一次，离开0.5秒后允许重触发，中断后重新完整计时。
- bear与default角色JSON移除dodge注册。受惊PNG保留作为拖动临时脸；既有抚摸、拖动、委屈、存档/DPI/Recover行为保留。通用Once播放器仍用于静态表情及测试，测试自定义动作名改为sample_once，不再混称自动躲避功能。
- 原App互动测试替换为8个剩余契约用例：完整驻留/离开重触发；1/33/100ms高速位置序列均不改变idle；有状态的拖动/菜单/暂停/抚摸中断后清理与重新驻留。未删除悬停Alpha、脚点、情绪和存档等有意义回归。
- 正式输入探针、动画观察宿主已去除旧API/旧受惊预期，均重编译通过；本轮未重新占用鼠标运行整套探针。

## 验证与运行

```powershell
dotnet build DesktopPet.slnx -c Debug --artifacts-path artifacts/mouse-reaction-removal/build -m:1 --ignore-failed-sources --nologo -v minimal
dotnet test DesktopPet.slnx -c Debug --artifacts-path artifacts/mouse-reaction-removal/build --no-build --no-restore -m:1 --nologo -v minimal
```

构建零警告/错误；**Core174+App128=302/302通过，0失败/跳过**。原336减少34项来自已取消功能的专用Core测试，App仍128项。初次还原因受限网络读取NuGet漏洞元数据失败，正常环境原样成功；未禁用审计或新增依赖。

源码引用检查：src及当前可执行测试内无MouseSampler、SwipeDetector、峰值段、速度DPI测试入口或自动PlayAction(dodge)。旧审查/整改/证据内的字符串属于历史，不能批量覆盖以消除搜索命中。

桌面启动脚本仍调用仓库 `tools/Start-DesktopPet.cmd`，现已指向新输出 `artifacts/mouse-reaction-removal/build/bin/DesktopPet.App/debug/DesktopPet.exe`。本轮正常关闭旧版本PID173908并自然退出，启动新版本PID174432，实际可见窗口1个、DPI144（150%）；过程未发送鼠标输入或强杀进程。PID仅为本次记录，后续不能照抄复用。

结果与摘要在 [证据目录](../history/evidence/INPUT-001-removal/verification.txt)；重建后需重新确认当前二进制，旧构建路径不再是桌面入口目标。

## 开发AI接手

1. 重读AGENTS、STATUS、D-009、PRODUCT R-01及本文件，对照实际diff核对并保留本次删除；不要把旧报告中的快划/冷却恢复为待办。
2. 保留小熊资源、必要的拖动受惊脸和其他既有互动。阶段2抛掷的拖动轨迹速度估算另有需求，不因本次删除而取消。
3. 工作区原本已有P1-004撤回、小熊接入、动画验证等未提交改动；本次叠加删除不能成为reset/checkout其他文件的理由。未自动提交、推送或进入P1-004。
4. 本任务完成不代表ART-001独立验收通过。快划触发不再是美术验收缺口；眨眼轮廓一致性、节奏和好奇自动退出建议仍按用户后续决定处理。
