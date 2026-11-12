# P3-001-S3 R9 证据

2026-11-09，HEAD a0f84f9 加未提交S1/S2/S3工作区。关键版本见versions.json，开发原整改快照见developer-response.md。本轮未修改产品/正式测试、未访问真实用户存档、未做真实鼠标操作、未提交推送。

独立命令（均编译当前源码）：

- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter 'FullyQualifiedName~MultiPetIsolationTests' --logger 'trx;LogFileName=s3-r9.trx' --results-directory artifacts/p3-001-s3-r9`：退出0，29/29通过，0跳过。
- `dotnet test artifacts/p3-001-s3-r9/probe/ReviewProbe.csproj --logger 'trx;LogFileName=reviewer-s3.trx' --results-directory artifacts/p3-001-s3-r9`：退出1，3/3预期反例断言失败，0跳过。源码与TRX在本目录。

反例实际结果：

- 删除bear-2再添加：实际新ID仍为bear-2，违反不复用已删除ID的断言。
- 两只时RemovePet("bear-1")：实际返回true，违反保留原首只的断言。
- OnRecoverRequested后：第二只窗口Left=1100，存档内WindowLeft=900，最终偏移未标脏。这里直接检查生产内存状态，未将其声称为已重跑防抖文件写入；按SaveCoordinator待写状态引用和无后续MarkStateDirty的代码分析，该旧位置会进入防抖保存。正常退出再次全量刷新可以掩盖此缺陷。

原正式29项全绿与独立反例3失败覆盖不同断言，不混作全量失败。开发报告688/688仍是开发自测，本轮未独立全量。右键Exit/最后一只移除的语义与保存后果来自静态路径核对，未在测试宿主调用Shutdown。
