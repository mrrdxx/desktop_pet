# P4-002 R9 开场与移除实测

使用 R8 隔离探针副本，增加测试用双宠布置、窗口状态记录和对实际 WPF PetCanvas 的帧捕获。首只测试窗口暂停，第二只经 AddPet 创建，再通过同一提醒分发链播放。探针构建零警告、零错误。仅改隔离副本。

- 三张 opening PNG 是播放期间从第二只的小熊窗口画布采集，120×120。实际呈现闭眼、张口变大、收口，未见旧横线，脚底保持对齐。
- 通过鼠标打开第二只右键菜单并点 Remove Pet。窗口清单随后仅余首只。日志第 20 秒开始 closed=true、targetRegistered=false、isRestReminderActive=false，首只全程未表演。
- 菜单开启之前，第 10 秒仍为 rest 且 active=true。菜单开启后、第 12～18 秒尚未选择命令时，画面和动作已变 idle，active 仍为 true。源码：ShowNativeContextMenu 无条件调用 EndDrag(isCancellation:true)，即使没有拖动也会播放 idle。下一轮应复核菜单无命令关闭的行为。
- 本轮未触发真实锁屏或睡眠。
