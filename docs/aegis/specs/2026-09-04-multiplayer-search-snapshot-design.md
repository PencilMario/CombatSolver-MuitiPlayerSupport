# 多人搜索快照重算开关设计

## 目标

多人模式新增一个默认开启的设置，控制后台搜索完成前发生遗物、其他玩家攻击等属性变化时是否丢弃并重算。关闭时，一次搜索固定使用开始时捕获的根快照；只有结束后再次搜索或打断后重新搜索才捕获新快照。

## 架构与数据流

- `SolverSettingsData` 持久化开关，`SolverSettingsSnapshot` 在主线程捕获不可变值。
- `SolverSearchSession` 在创建时锁存本次是否为多人搜索以及该开关，避免搜索期间读取全局设置。
- `CompleteSearchCore` 继续执行战斗对象、可玩阶段和任务生命周期校验；仅在“多人且关闭开关”时跳过计算期间 `LiveCombatStamp` 变化导致的结果丢弃。
- 搜索结果仍保留开始时的 stamp，不伪造 live 状态等价。部署入口继续由原生卡牌实例、目标和费用匹配保护；匹配失败沿现有安全重算/中止路径处理。
- 单人模式、开关开启的多人模式、显式重算和取消后新搜索维持现有语义。

## UI 与兼容性

设置页在多人回合数设置旁增加开关，默认开启并支持旧 JSON 缺失字段回退。关闭时显示“本次计算固定使用开始时快照”的说明；修改后对下一次搜索生效。

## 验证与非目标

覆盖设置默认/往返/UI 重载，以及多人开关两种完成判定和单人回归。真实多人联网战斗仍受现有夹具边界限制，不在本次新增通用多人动作或跨玩家预测。

## 工作草稿

- TaskIntentDraft：增加多人搜索期间状态漂移策略，默认安全重算，可选固定根快照。
- BaselineReadSetHint：`SolverSettings.cs`、`SolverSettingsPanel.General.cs`、`SolverController.cs`、`SolverControllerSessions.cs`、多人搜索计划与测试矩阵。
- ImpactStatementDraft：Runtime 设置/搜索完成边界/UI 测试；保持单人和部署安全校验不变。
