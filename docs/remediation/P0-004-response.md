# P0-004 整改记录

维护者：监理助手，本轮由用户明确授权担任修复实现者；开发 AI 停手交接。后续由开发方接续维护。
更新时间：2026-10-22（Asia/Shanghai）。对应 [P0-004 审查报告](../reviews/P0-004-review.md)，流程见 [REVIEW_PROCESS](../REVIEW_PROCESS.md)。
当前边界：实现、正式 VSTest 55/55 及十场景隔离集成自测完成；独立复核未进行。本记录不是审查结论。
版本、共用测试环境/命令、原始证据及权限限制唯一维护于 [修复交接](../handoffs/P0-004-lifecycle-fix.md)，不另复制整套版本表。

## P0-004-review.md / F-01

- 开发处置：已修改，正式单元测试及隔离自测通过，待复核。
- 修改：ArchiveService 在未知高版本反序列化失败时返回受保护的运行默认对象，保留 unknown 标记；state/settings 文件级只读保护独立，保存时重新检查磁盘版本。TrayService 配置明确的只读提示，App 启动时调用。
- 自测：新增 8 个用例覆盖两份文件的新字段类型、替换 DTO、直接 Save 的新服务、未来版本备份及两份文件保护互不干扰；正式 VSTest 55/55 通过。十场景集成中的 archive-cases、readonly-state、readonly-settings 均通过，含实际正常退出后文件原文保留。
- 未验证：系统是否实际展示通知气泡不作保证；此前测试环境失败及最终成功结果见交接。
- 证据与复核请求：见交接；请求核对保护边界及正常 v1 损坏回退仍有效。未填监理通过。

## P0-004-review.md / F-02

- 开发处置：已修改并完成隔离集成自测，待复核。
- 修改：App.FlushFinalState 总是捕获最终位置、暂停及显示器标识并重新标脏后 Dispose/Flush；关闭过程防重入。MainWindow 的手动 Recover、布局恢复会 MarkStateDirty。
- 自测：旧失败场景 recover-after-save（等 5.7 秒后再次找回再退出）通过；fast-final 防抖前退出及 restart-position 通过。未知高版本的退出保护同时通过。
- 未验证：未实际点击 Explorer 托盘图标；本次通过实际 ToolStrip 菜单事件验证退出链。
- 证据与复核请求：见交接；请求针对最终状态和保护逻辑交互复核，不自行关闭问题。

## P0-004-review.md / F-03

- 开发处置：已修改并完成隔离启动自测，待复核。
- 修改：App 创建 MainWindow 后、显示前依据存档调用 PauseAnimation，菜单使用同一暂停状态。
- 自测：paused-start 显示 paused=true、timerEnabled=false，等一秒帧不变化；normal 中 Pause、暂停下找回、Resume 后更新均通过。
- 未验证：真实锁屏/休眠不在本轮测试范围，沿用既定后续适配边界。
- 证据与复核请求：见交接；请求复核启动状态/行为一致性。

## P0-004-review.md / F-04

- 开发处置：已修改并完成隔离启动自测，待复核。
- 修改：不直接赋值旧坐标；SourceInitialized 获得显示器信息后使用既有 WindowPositionAdapter 恢复并约束，非有限坐标回退默认值。保存时记录实际所在显示器标识。
- 自测：offscreen-start 的旧屏/越界位置变为完整可见；restart-position 的有效位置 `(350,260)` 保持。单屏当前 150%。
- 未验证：100% 本轮启动链、真实混合 DPI 双屏/断屏尚未实测，不宣称已完成。
- 证据与复核请求：见交接；请求核对启动恢复，不重开 P0-003 既往结论。

## 后续记录

最终获批重试正式 VSTest 成功，55/55 通过。开发 AI 先核对交接与证据，再提交独立复核；后续有修改才按影响范围补验证并追加真实结果。原报告仍由用户指定的审查者维护，复核或用户裁决只链接原出处，不代签。
