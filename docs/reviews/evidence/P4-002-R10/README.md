# P4-002 R10 复核证据

- 独立输出重新构建，运行 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p4-002-r10/build --filter 'FullyQualifiedName~RestReminder|FullyQualifiedName~PauseThenDragStart_PlayerStaysPaused'`，Core 22/22、App 33/33。全量 984/984 为开发自测。
- 用 R8 隔离副本重新构建，0 警告 0 错误。副本设置标题与任务栏入口，延长自动关闭至 140 秒。前台选中隔离小熊，实际右键打开原生菜单并按 Esc，截图中小熊在打开和关闭菜单后均保持闭眼休息姿态。第二次打开菜单后选择 Pause，画面回到待机，随后菜单显示 Resume。选择 Resume 后仍为待机，提醒计数保持 1。日志在 28 秒为 rest，30 秒为 idle，日志独立于截图证据，不能单凭此区间区分自然结束与暂停。
- 经右键 Exit 确认隔离小熊窗口关闭。探针在独立 Dispatcher.Run 中可能继续输出到其自动退出时限。未操作真实锁屏、睡眠或正式存档。
