# artifacts 清理记录（2026-11-10）

用户明确授权检查并清理无必要的artifacts内容。清理前约745.6 MiB，清理后约193.4 MiB，释放约552.2 MiB（按文件长度合计，不代表文件系统实际占用）。

检查.gitignore/GIT_WORKFLOW、目录文件分类、文档引用及运行进程；artifacts未受Git跟踪，未发现从中运行的进程。早期审查确有仅在artifacts的探针/原始日志，不删除整目录。

初次限定在artifacts内的批量Remove-Item命令被自动审批拒绝，仅返回blocked by policy；命令未执行，未试图换语法绕过。改用.NET官方clean按实际项目/构建配置清理：145个本地csproj默认配置、60套自定义artifacts输出路径的Debug/Release、5个可唯一定位的额外探针项目输出，全部命令成功。

保留：独立探针源码/脚本、原始TRX/日志、截图、隔离测试存档及构建元数据。部分脱离项目构建记录的DLL/EXE和旧工具缓存未被clean删除；不能将剩余全部称为必要证据。若需进一步逐项删除，应另行建立准确清单并通过环境删除权限，不能整目录盲删或借别的解释器绕过审批。

未修改应用/正式测试或监理历史证据，未提交推送。官方clean可能同时清理被引用项目的bin/obj，因此后续测试须正常build，不能直接--no-build。旧隔离副本执行文件可能已清掉，继续预览应重新运行tests/Integration/Run-ProbeLive.ps1 -Interactive构建，不直接-Restart。源码、真实存档、docs/reviews/evidence全部保留。未做功能改动，无需重跑应用测试；检查清理命令结果和git diff --check。

本机命令清单及失败记录在artifacts/cleanup-*-2026-11-10.*（无失败项）。P3-001完成及F-01～F-08关闭结论不变。
