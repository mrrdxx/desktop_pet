// P5B-003: climb-preview 场景（攀爬动作隔离预览）。
//
// 目标：在「真实 MainWindow + 生产 CreatePetWindow」下顺序播放
//   climb_side（侧边爬升，loop）
//   climb_edge（上缘移动，loop）
//   climb_land（顶部落位，once：收脚 2 帧 → idle_0 站稳，共 3000ms）
// 每 ~100ms 调一次生产 UpdateFrame() 后，用 RenderTargetBitmap 把
// PetImage（120×120，含 FrameOffset/Wobble 变换）渲染成 PNG 落盘，
// 供驱动脚本/Python 合成 120×120 动态 GIF、关键帧与放大对比。
//
// 隔离性：
// - TestSetupIsolatedSave + CreatePetWindow 与既有 window-climb-live 一致。
// - 停止生产 33ms _updateTimer，避免 OnUpdateTick 的夜间/表现状态机
//   干扰预览动作 —— 动作推进完全由本场景手动 PlayAction 控制。
// - 探针无 InternalsVisibleTo（已核实 App 无该特性），
//   _animPlayer（private readonly）/UpdateFrame（private）/
//   PetImage（XAML internal）/ _updateTimer（private readonly）一律走反射，
//   与 FootStabilityTests 的既有反射先例一致。
// - 不修改 MainWindow.xaml.cs，不接线 PlayClimbAction/EndClimbAction，
//   不接触路线/位置/中断规则。
//
// 文件（scenarioDir）：
//   climb-preview-events.jsonl  事件流（stage-frame：action、
//                               frameIndex、offsetX/Y、tMs、png）
//   climb-preview-summary.json  阶段汇总（每阶段帧数、周期、idle 基准文件）
//   frames/*.png                120×120 渲染帧（含 idle_base 基准）

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Persistence;

namespace DesktopPet.DiagnosticProbe;

public static partial class Program
{
    /// <summary>预览阶段定义：动作 id、采集帧数、采帧间隔 ms。</summary>
    private sealed record PreviewStage(string Action, int FrameCount, int StepMs);

    private static object? PreviewGetField(object target, string name)
    {
        var f = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return f?.GetValue(target);
    }

    private static object? PreviewInvokeMethod(object target, string name)
    {
        var m = target.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return m?.Invoke(target, null);
    }

    private static void PreviewSetField(object target, string name, object? value)
    {
        var f = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        f?.SetValue(target, value);
    }

    private static void PreviewRenderPng(Image petImage, string path)
    {
        // 与生产渲染同尺寸：PetImage 120×120；96 DPI 即 1:1 像素。
        var rtb = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(petImage);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static void ClimbPreviewVerification(string scenarioDir)
    {
        // ---- 隔离启动 + 真实宠物窗口（生产路径） ----
        s_app!.TestSetupIsolatedSave(scenarioDir);
        s_app.TestState!.Pets = new List<PetInstanceArchive>
        {
            new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
        };
        s_app.CreatePetWindow(s_app.TestState!.Pets[0]);

        var mainWindow = s_app.MainWindow;
        if (mainWindow is null)
        {
            WriteResult(1, "climb-preview: MainWindow is null after CreatePetWindow.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }
        mainWindow.Show();

        // ---- 反射取引（探针无 InternalsVisibleTo） ----
        var animPlayer = PreviewGetField(mainWindow, "_animPlayer") as AnimationPlayer;
        var petImage = PreviewGetField(mainWindow, "PetImage") as Image;
        if (animPlayer is null || petImage is null)
        {
            WriteResult(1, "climb-preview: reflection failed (animPlayer or PetImage).");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // 停掉生产 33ms tick：预览动作不受夜间/表现状态机干扰。
        (PreviewGetField(mainWindow, "_updateTimer") as DispatcherTimer)?.Stop();

        // P5B-003 F-01（隔离正确性，关键）：
        // 生产 UpdateFrame 在 1844-1849 行会对当前帧做表达式合成覆盖——
        //   releaseFrame = _dragRelease?.Update(...)
        //   blinkFrame   = _idleBlink?.Update(...)
        //   frame = releaseFrame ?? blinkFrame ?? frame
        // 动作 id 为 climb_side/climb_edge 等非 idle 时 idleAvailable=false，
        // 但表达式仍会依据其内部时钟（如距上次眨眼/释放反馈的时间）产出覆盖
        // 帧。诊断（diag_pixels4/5）已证实：side_01/side_02 等非首帧渲染帧
        // 与全部候选素材的差异距离几乎持平、脚带 diff=130，即帧已被合成覆盖，
        // 非任何素材纯净帧。停 _updateTimer 只能阻止 OnUpdateTick 的夜间/表现
        // 状态机，无法阻止 UpdateFrame 内的表达式合成。
        // 因此采帧前把两个表达式置 null（readonly 实例字段，反射可写；
        // UpdateFrame 及 Start/Cancel/Pause/Resume 全部经 ?. 调用，null 安全），
        // 使 frame 恒等于动作帧本身，预览观感 = 素材动作的真实渲染。
        PreviewSetField(mainWindow, "_idleBlink", null);
        PreviewSetField(mainWindow, "_dragRelease", null);

        // ---- 目录与事件流 ----
        var framesDir = Path.Combine(scenarioDir, "frames");
        Directory.CreateDirectory(framesDir);
        var eventsFile = Path.Combine(scenarioDir, "climb-preview-events.jsonl");
        File.WriteAllText(eventsFile, "");

        var stopwatch = Stopwatch.StartNew();
        var eventLines = new System.Text.StringBuilder();

        void WriteEvent(string stage, int index, string png, double offsetX, double offsetY)
        {
            var (frameIndex, frame) = animPlayer.GetCurrentFrame();
            var rec = new
            {
                tMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 0),
                stage,
                index,
                imageFile = Path.GetFileName(png),
                action = animPlayer.CurrentActionId,
                frameIndex,
                imagePath = frame.ImagePath,
                offsetX,
                offsetY,
                completed = animPlayer.IsCompleted,
            };
            var line = JsonSerializer.Serialize(rec);
            eventLines.AppendLine(line);
            File.AppendAllText(eventsFile, line + "\n");
            Console.WriteLine($"[P5B003] t={rec.tMs,5}ms {stage} #{index} " +
                $"action={animPlayer.CurrentActionId} frame={frameIndex} " +
                $"{frame.ImagePath} offset=({offsetX},{offsetY})");
        }

        // ---- idle 基准：用于脚点对比 ----
        animPlayer.PlayAction("idle");
        PreviewInvokeMethod(mainWindow, "UpdateFrame");
        var idlePng = Path.Combine(framesDir, "idle_base.png");
        PreviewRenderPng(petImage, idlePng);
        var (idleOffX, idleOffY) = mainWindow.CurrentFrameOffset;
        WriteEvent("idle", 0, idlePng, idleOffX, idleOffY);

        // ---- 三阶段顺序采集 ----
        var stages = new List<PreviewStage>
        {
            new("climb_side", 9, 100),   // 900ms = 完整 loop 周期（3×300ms）
            new("climb_edge", 9, 100),
            new("climb_land", 15, 200),  // 3000ms = once 全程（260+340+2400）
        };

        // 预热渲染：PlayAction 后立刻 UpdateFrame + RenderTargetBitmap 一次
        // （结果丢弃）。P5B-003 诊断6/8 证实：Image.Source 刚切换时，若立即
        // 渲染则首帧仍画的是切换前的旧图（side_00 实测渲染成 idle_0 内容），
        // 后续 tick 才画新图。预热保证每个阶段的第一个正式采帧就是该动作
        // 的真实首帧，避免预览 GIF/断言出现滞后伪影。
        void InitStage(string action)
        {
            animPlayer.PlayAction(action);
            PreviewInvokeMethod(mainWindow, "UpdateFrame");
            // RenderTargetBitmap 渲染一次并丢弃（预热换图）。
            var warmup = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32);
            warmup.Render(petImage);
        }

        int stageIdx = 0;
        int frameIdx = 0;
        InitStage(stages[0].Action);

        var captureTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(stages[0].StepMs)
        };
        captureTimer.Tick += (_, _) =>
        {
            var stage = stages[stageIdx];

            // 采帧前先走一次生产 UpdateFrame，让帧偏移/晃动变换生效。
            PreviewInvokeMethod(mainWindow, "UpdateFrame");
            var (dx, dy) = mainWindow.CurrentFrameOffset;

            var png = Path.Combine(framesDir,
                $"{stage.Action}_{frameIdx:D2}.png");
            PreviewRenderPng(petImage, png);
            WriteEvent(stage.Action, frameIdx, png, dx, dy);

            frameIdx++;
            if (frameIdx >= stage.FrameCount)
            {
                frameIdx = 0;
                stageIdx++;
                if (stageIdx >= stages.Count)
                {
                    captureTimer.Stop();

                    // ---- 汇总 ----
                    var summary = new
                    {
                        scenario = "climb-preview",
                        petHwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle.ToInt64(),
                        idleBaseline = Path.GetFileName(idlePng),
                        stages = stages.ConvertAll(s => new
                        {
                            action = s.Action,
                            frameCount = s.FrameCount,
                            stepMs = s.StepMs,
                            totalMs = s.FrameCount * s.StepMs,
                        }),
                        eventCount = eventLines.Length > 0
                            ? eventLines.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
                            : 0,
                        note = "Frames rendered through production UpdateFrame + RenderTargetBitmap(PetImage). " +
                               "Foot stability must be verified from rendered pixels, not Offset=0 alone.",
                    };
                    File.WriteAllText(
                        Path.Combine(scenarioDir, "climb-preview-summary.json"),
                        JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

                    s_app?.DiagnosticLogger?.Dispose();
                    WriteResult(0, "climb-preview: three-stage capture completed.");
                    Environment.Exit(0);
                }
                else
                {
                    // 阶段切换：预热换图后进入下一阶段首帧（避免首帧滞后伪影）。
                    captureTimer.Stop();
                    InitStage(stages[stageIdx].Action);
                    captureTimer.Interval = TimeSpan.FromMilliseconds(stages[stageIdx].StepMs);
                    captureTimer.Start();
                }
            }
        };
        captureTimer.Start();

        Dispatcher.Run();

        WriteResult(1, "climb-preview: Dispatcher.Run() returned (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }
}