# 性能研究与复现

[返回文档导航](../README.md)

每份报告只证明其中注明的版本、场景和测量条件。当前测试入口见 [测试矩阵](../TEST_MATRIX.md)，架构约束见 [架构地图](../ARCHITECTURE.md)。

## 专题

- [极高配置的其他战斗压力筛查](veryhigh-pressure-survey-20260908.md)：10组输入、两项正常配置复测，定位药水高分支/GC与超大牌堆压力。

- [极高配置的空回调与并行度优化](veryhigh-hook-dispatch-20260908.md)：第二轮 headless 固定工作量约 2.18 倍、RSS 约 10–12% 成本及验证限制。

- [极高配置下的等质量热路径优化](veryhigh-quality-preserving-20260908.md)：当前上游 A/B、原版费用对照与可见 Steam 验证。

- [Ritsu 目标类型查询缓存](metadata-target-type-cache-20260907.md)：PR #49 的合同、复现条件与性能证据范围。
- [Issue #36 研究与首轮基线](gc-issue36-research.md)。
- [Issue #36 固定工作量复现](gc-issue36-reproduce.md)。
- [Issue #36 静态审计](gc-issue36-code-audit.md)。
- [Issue #36 候选实现与验证](gc-issue36-implementation.md)。
- [Issue #36 第二轮实验](gc-issue36-round2.md)。
- 结构化结果：[首轮](gc-issue36-results.json)、[第二轮](gc-issue36-round2-results.json)。

## 历史资料

- [性能与掉帧复盘](PERFORMANCE_AND_STUTTER_DIAGNOSIS.md)：早期版本结论与演进记录。
- [Fork 性能结果](RF_FORK_PERFORMANCE_RESULTS.md)：历史固定场景的性能与正确性数据。
- [旧性能样例](PERFORMANCE_FIXTURES.md)：保留复现资料；后续策略批次使用仓库的 strategy-replay-iteration skill，不继续维护此旧样例清单。
