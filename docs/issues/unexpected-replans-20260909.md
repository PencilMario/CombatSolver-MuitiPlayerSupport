# 2026-09-09 计划外重算：0.34.4 未修复报告分诊

[返回问题目录](README.md)

## 材料与口径

日志站以 `issue=UnexpectedReplan`、`resolution=unresolved`、`modVersion=0.34.4` 查询到 55 份报告，按 `combat.sessionId` 去重为 51 场。全部报告只提取 `report.json`、异常摘要和 CombatSolver 独立战斗日志；本轮没有读取整份 `godot.log`，也没有逐包重跑。

原始包、选择清单、哈希校验结果与逐场本地分类保存在 `.local/issue-bundles/unexpected-replans-0.34.4/`，不进入源码提交。日志站令牌只有 read scope，本轮不能写处理备注；报告保持未修复状态。

## 分组

| 分组 | 独立场次 | 报告数 | 当前处理 |
|---|---:|---:|---|
| 幻影之刃 / 致死性 / 不动参与的 Power 获得顺序 | 20 | 20 | 已修复并完成最小原生差分 |
| 回合开始自动出牌后的 `Y` 历史 | 7 | 9 | 已修复并完成最小跨回合原生差分 |
| 君王之剑跨回合手牌位置 | 5 | 5 | 最小路径未复现，缺少预期整手状态，保留 |
| 君王之剑临时星能费用 | 1 | 1 | 孤立问题，保留 |
| 其余低频或未归因差异 | 18 | 20 | 保留；每个初步触发点仅 1–2 场，不优先投入 |

正常手动偏离单独忽略。报告 `81201f2162284bfcb5516d795c3728cf` 既有一次 `manual_divergence`，也有后续两次独立的 Power 顺序差异；只把后两次纳入第一组。

## 已修复：历史敏感 Power 被临时移除后改变顺序

代表报告 `4b188b2e088c4826ba1b14d0252bd3bf`：第 4 回合实机顺序保持 `Strength, Afterimage, PhantomBlades, Dexterity`，预测状态变成 `Strength, Afterimage, Dexterity, PhantomBlades`。首个差异就是 Power 获得顺序；之后的续用失败是连锁结果。

根因是预测为了避开已经用完的“本回合首次”效果，在出牌前把幻影之刃、致死性或不动数量临时设为 0，出牌后再恢复。Power 生命周期把 `0 → 正数` 视为重新获得，于是预测分支把该实例移动到监听顺序末尾，实机从未移除这些 Power。现在伤害效果继续读取分支中的小刀/攻击历史，不动由格挡乘算 mirror 读取分支格挡历史；出牌入口不再改写 Power 生命周期。

失败基线 `6eba58023b214ead8829ed5effbad6ee` 在 `MIXED-POWER-ACQUISITION-ORDER` 得到 `P[1] expected Strength actual PhantomBlades`。最终 `8f5c8d96d9e342e8a2163e54e359c0d2` Passed：先制造一次攻击和格挡历史，再按报告顺序获得三种 Power，继续出牌并施加新 Power，完整比较生命、格挡、牌堆、有序 Power、ContinuationStamp、Fork 与父分支隔离。

直接证据范围：代表报告只直接核验了包内元数据与独立日志，没有整包恢复；最终通过的是从代表差异抽出的最小实机夹具。其余 19 份报告按相同首个 `P[index]` 差异、相同三种 Power 和相同临时移除/恢复路径静态归组，没有逐包重放。20 份报告 ID：

`4b188b2e088c4826ba1b14d0252bd3bf`、`baff3f1f64cb40058d79c0cd010fc1bc`、`c3cdb552a1d54ad8ba89d3000652a1b4`、`6f733c67442b4f618488924b03e73a5e`、`1f2e2fce2ec14cbeab33cf38b56df1d9`、`7e1175348df345f4ac0b9279308cd70e`、`d5890345771347ecb6c4e9a596bf6c43`、`f8ecd338197b4485bd4dd5dae88b706d`、`5ef653c8897349dc840cb1bc62f0a935`、`8e68629ec65c4a5b81faffad1e54f10d`、`681d989fbda84b889137f0d80b016eca`、`a8dc35c07c80464896048a13ccf2d23c`、`d9084f013d984a2eb7bbb67be41a9e55`、`1b1e109fdbea4db991f26c900b8fbf24`、`9419be5958224bda8f4b2e9ec0b1f1a7`、`81201f2162284bfcb5516d795c3728cf`、`c4beff8ab33b4dd89b9a9010495df2f7`、`b4c978ef34ec4c82975300e21ad9eccb`、`9037ccf3df9545149f09c4f57e23dd97`、`0a5d8947277343ec9bc251e616cc8725`。

## 已修复：抽牌自动出牌历史在阶段开始被清零

代表报告 `c68216599d1a439b972d4161a2135988`：第 2、3 回合的首个差异均为 `Y expected=0/0/0 actual=0/0/1`。独立日志显示玩家持有狂战士，回合开始抽到 Strike 并自动打出；没有选择或手动偏离介入。`167b73f525e14c7397dba954ed856da8` 是同一 session 的重复导出。

根因是无回合开始选牌时，预测先抽牌并完成狂战士自动出牌，之后才调用阶段开始处理；阶段开始入口同时承担了新回合历史归零，把刚记录的 `CardPlayStarted` 又清掉。实机的回合窗口在抽牌前已经切换，不会清除本回合事件。现在阵营回合历史在回合身份切换后、`BeforeSideTurnStart` 和抽牌前统一归零；Blur、镀层、Slow 等阶段开始效果仍留在原来的 `AfterSideTurnStart` 时点。

`HELLRAISER-TURN-START-HISTORY` 最终 `5bbb6f1081aa4206ae077b8aff733194` Passed：从第 1 回合推进到第 2 回合，抽到的 Strike 由狂战士自动打出，预测保留 1 次开始事件并与实机完整状态及 ContinuationStamp 一致。既有 `NORMALITY-AUTOPLAY` 回归 `f014ee4080bd4191805e67096eef7e8b` Passed，确认普通出牌开始计数、Fork 隔离与下一回合归零仍成立。

直接证据范围：代表报告只直接核验元数据和独立日志，未整包恢复；最终通过的是最小跨回合实机夹具。其余 8 份按相同 `Y` 首差异、同一抽牌自动出牌时序静态归组。手动加求解器 session `5079bb48a48b4904b514ca43a4defac0` 的两份报告记录的也是 `reason=state_mismatch`，不是正常的 `manual_divergence`，因此纳入；未把任何正常手动重算算入修复数。9 份报告 ID：

`c68216599d1a439b972d4161a2135988`、`167b73f525e14c7397dba954ed856da8`、`50c42b8d127146a48cc1934794e44f89`、`130a8ad1a036491eaa9d4a2695353f04`、`64bd001977434ee8a850f24cf99d488e`、`48e252dcae8d46e4a30b5940b0bc51c8`、`3c21fc10dcd1458a9383b66a05c52d53`、`e3fb071748a1462e958667762428fb7c`、`9017fe21ebb14d0b8c6c40bd8483c625`。

## 尚未处理

君王之剑位置组共 5 场：`b293142d539f4db2acc91a55d3fe939d`、`062da41d5ccb43e68193e897964f7654`、`0ae4b39658ce4b8eb0299e4fd2bfea23`、`f1ec15685c634898bcfcc55471660dc6`、`f70123edb0934eb58521dfcf3e35d5df`。共同现象是下一回合手牌首个位置差异的一侧为君王之剑，但触发动作并不一致；独立日志只保存首差异，没有保存缓存计划的完整预期手牌。最小夹具先验证单独保留跨回合，`28693119238a446fa8c6a26238602d10` Passed；再加入报告中最常见的淬炼刀刃后跨回合，`e7f09dd4f7bd4d1781a926453ab9e8ef` 仍 Passed。因此没有根据牌名猜测插入规则，也没有提交试验夹具。后续需要缓存路线的完整预期手牌，或能保留该状态的代表检查点。

`75cc48cefac8427586a71c4ab797b8d7` 是君王之剑当前星能费用 `expected=0 actual=1`，与上述牌序组不是同一状态差异，只有 1 场，本轮保留。

其余 20 份 / 18 场保持未处理。其中只有两组有重复表象：Lagavulin Matriarch 的敌方格挡 `expected=12 actual=0` 为 2 场（`38693964c78149a29afd20c5a17857b2`、`cee1db19012c47be828cb60e5470f62a`）；Exoskeletons 的 `E2.hp expected=9 actual=18` 是同一 session 重复导出的 3 份（`a0ab5a6647b64d5d9917cc963a05f812`、`5585bbcecdf8424183d2ce3e54ad0932`、`714d24b0bc48439bbd326b25214636b7`），只算 1 场。其他首差异分散在玩家/敌人生命、牌堆、Power、遗物计数、怪物行动、苦难层数和卡牌费用，每个触发点仅 1 场；本轮没有按字段硬合并。

本批最终在最新 0.34.4 的 55 份 / 51 场中修复并归组 29 份 / 27 场，保留 26 份 / 24 场。查询时其他旧 Mod 版本另有 70 份未修复报告，本轮按“优先最新版本”没有展开，不能视为已覆盖。
