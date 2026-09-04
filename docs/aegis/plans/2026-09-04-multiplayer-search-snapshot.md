# 多人搜索快照重算开关 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use aegis:subagent-driven-development (recommended) or aegis:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让多人搜索可配置为在计算期间属性变化后保留开始时快照结果，默认仍在变化时重算。

**Architecture:** 设置在主线程持久化并捕获为 snapshot；搜索 session 在启动时锁存多人标识和策略。完成回写只在关闭策略时跳过多人 live stamp 变化造成的 stale 丢弃，部署仍使用现有实例匹配与安全重算边界。

**Tech Stack:** C# / .NET 9 / Godot / 内嵌无人测试协议。

**Baseline / Authority Refs:** `AGENTS.md`；`docs/ARCHITECTURE.md`；`docs/TEST_MATRIX.md`；`docs/aegis/specs/2026-09-04-multiplayer-search-snapshot-design.md`。

**Compatibility Boundary:** 单人行为、多人回合上限、搜索 worker 不读全局设置、取消/显式重算重新捕获根快照、部署原生匹配保护均保持不变。

**Verification:** 先运行新增设置/判定测试观察失败，再运行目标无人测试；最后执行 `dotnet build CombatSolver.csproj -c Release`、`pwsh -NoProfile -File tools\\verify-refactor-boundaries.ps1`。

---

### Task 1: 设置与 UI 合同

**Files:**
- Modify: `src/Runtime/SolverSettings.cs`
- Modify: `src/UI/SolverSettingsPanel.General.cs`
- Modify: `src/Testing/UnattendedTestRunner.ControllerSessions.cs`

**Why this task exists:** 玩家需要可持久化、默认开启且在下一次搜索生效的多人状态变化策略。

**Impact / Compatibility:** 旧 JSON 缺失字段必须回退 true；现有设置校验和单人 snapshot 不变。

**Verification:** 扩展现有多人设置测试，先确认缺少属性时断言失败，再实现字段、往返和 UI 重载断言并确认通过。

### Task 2: 搜索 session 锁存与完成判定

**Files:**
- Modify: `src/Runtime/SolverControllerSessions.cs`
- Modify: `src/Runtime/SolverController.cs`
- Modify: `src/Testing/UnattendedTestRunner.ControllerSessions.cs`

**Why this task exists:** 搜索期间不能因全局设置变化或其他玩家属性变化而改变本次搜索的语义。

**Impact / Compatibility:** 只改变多人关闭开关的 `LiveCombatStamp` stale 分支；任务故障、取消、combat replacement、CanSolve 失败仍拒绝回写。

**Verification:** 新增纯判定 helper/生命周期断言覆盖多人开关开/关和单人三态，先红后绿；运行对应 headless controller fixture（若多人 runtime fixture 不可用则记录边界）。

### Task 3: 文档与回归验证

**Files:**
- Modify: `docs/DEVELOPMENT_NOTES.md`
- Modify: `docs/TEST_MATRIX.md`

**Why this task exists:** 记录玩家可见开关、快照边界和本轮证据，避免设置与运行语义漂移。

**Impact / Compatibility:** 仅文档与测试证据，不改变运行时行为。

**Verification:** Release 构建、边界门禁；不重复已通过的无关完整战斗场景。
