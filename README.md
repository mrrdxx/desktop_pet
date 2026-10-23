# Windows 桌面宠物

一个强调鼠标互动、情绪记忆、物理手感和双宠社交的 Windows 桌宠项目。
当前实现进度、可运行状态和验证限制统一见 [STATUS](docs/STATUS.md)。

## 从哪里开始

- 开发 agent：先读 [AGENTS.md](AGENTS.md)，再读 [当前状态](docs/STATUS.md)。
- 了解产品：[产品规格](docs/PRODUCT.md)。
- 了解技术：[架构](docs/ARCHITECTURE.md) 与 [决策理由](docs/DECISIONS.md)。
- 执行任务：[任务清单](docs/TASKS.md)。
- 验证结果：[验证规则](docs/TESTING.md)，实际结果记在 STATUS。
- 换新对话继续开发：[完整操作及可复制提示词](docs/CONTEXT_HANDOFF.md)。

## 技术起点

当前采用 C#、.NET 10 LTS、WPF、Win32 API；本地 JSON 存档，MSTest 验证核心及 App 适配逻辑。
首要目标是 Windows 11 x64；具体边界及版本依据见 ARCHITECTURE。
技术基线及当前验证范围分别见 ARCHITECTURE 和 STATUS，不在此重复维护完成状态。

## 环境与命令

需要 Windows 和 .NET 10 SDK。仅安装 .NET Runtime 无法编译。
SDK 官方入口：[下载 .NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。
接手时先执行 `dotnet --info`，再按任务 P0-001 确认环境。

已验证环境：Windows 11 (10.0.26200)，.NET 10 SDK 10.0.402，运行时 10.0.13。SDK 版本由 `global.json` 锁定。

已验证命令（P0-001 实测通过）：

```powershell
dotnet restore DesktopPet.slnx
dotnet build DesktopPet.slnx -c Debug --no-restore
dotnet run --project src/DesktopPet.App/DesktopPet.App.csproj
```

测试命令从 P0-003 创建测试工程后开始使用：

```powershell
dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj -c Debug
```

全部单元测试使用 `dotnet test DesktopPet.slnx -c Debug`。生命周期的隔离集成验证见 [运行说明](tests/Integration/README.md)，使用仓库内临时副本和存档，不触碰默认用户存档。

需要形成便携发布包时再执行，阶段 0 不要求打包：

```powershell
dotnet publish src/DesktopPet.App/DesktopPet.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

## 直接交给下一个 AI 的提示词

工作协议维护在 [AGENTS.md](AGENTS.md)；产品方向、阶段顺序和验收分别按上面的链接读取，不另存一份重复的完整提示词。
有关联监理报告时，按 [审查协作规则](docs/REVIEW_PROCESS.md) 整改并提交复核；开发 AI 不改写监理报告，不自行签发复核通过。

首次接手开发：

> 请接手这个 Windows 桌面宠物项目。先阅读 AGENTS.md、docs/STATUS.md，以及 docs/TASKS.md 的当前阶段，检查真实工作区后继续。仓库文档是长期记忆，不依赖聊天历史。技术基线已在 docs/ARCHITECTURE.md 定义，先核实后沿用，不重复选型或重建规格。依据当前状态执行下一个依赖已满足的未完成任务，不一次实现全部功能，也不停在只写计划。普通实现细节自行决定；遇到必须由我决定的问题时提出具体选项，并继续不受影响的工作。每轮按 AGENTS.md 完成验证、文档更新和交接，明确区分计划、实现与已验证结果。

换新上下文后继续：按 [新对话接续指南](docs/CONTEXT_HANDOFF.md) 操作，分别复制其中“旧对话交接”和“新对话接手”的提示词。
