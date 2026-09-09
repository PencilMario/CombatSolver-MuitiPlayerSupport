# 2026-09-09 计划外重算：0.34.4 未修复报告分诊

[返回问题目录](README.md)

## 材料与口径

日志站以 `issue=UnexpectedReplan`、`resolution=unresolved`、`modVersion=0.34.4` 查询到 55 份报告，按 `combat.sessionId` 去重为 51 场。全部报告只提取 `report.json`、异常摘要和 CombatSolver 独立战斗日志；本轮没有读取整份 `godot.log`，也没有逐包重跑。

原始包、选择清单、哈希校验结果与逐场本地分类保存在 `.local/issue-bundles/unexpected-replans-0.34.4/`，不进入源码提交。日志站令牌只有 read scope，本轮不能写处理备注；报告保持未修复状态。

## 分组

| 分组 | 独立场次 | 报告数 | 当前处理 |
|---|---:|---:|---|
| 幻影之刃 / 致死性 / 不动参与的 Power 获得顺序 | 20 | 20 | 已修复并完成最小原生差分 |
| 回合开始自动出牌后的 `Y` 历史 | 7 | 9 | 待处理；其中手动操作场 1 场 / 2 份不作为修复依据 |
| 君主之刃手牌、抽牌堆与星能费用 | 6 | 6 | 待进一步归因 |
| 其余低频或未归因差异 | 18 | 20 | 保留；每个初步触发点仅 1–2 场，不优先投入 |

正常手动偏离单独忽略。报告 `81201f2162284bfcb5516d795c3728cf` 既有一次 `manual_divergence`，也有后续两次独立的 Power 顺序差异；只把后两次纳入第一组。

## 已修复：历史敏感 Power 被临时移除后改变顺序

代表报告 `4b188b2e088c4826ba1b14d0252bd3bf`：第 4 回合实机顺序保持 `Strength, Afterimage, PhantomBlades, Dexterity`，预测状态变成 `Strength, Afterimage, Dexterity, PhantomBlades`。首个差异就是 Power 获得顺序；之后的续用失败是连锁结果。

根因是预测为了避开已经用完的“本回合首次”效果，在出牌前把幻影之刃、致死性或不动数量临时设为 0，出牌后再恢复。Power 生命周期把 `0 → 正数` 视为重新获得，于是预测分支把该实例移动到监听顺序末尾，实机从未移除这些 Power。现在伤害效果继续读取分支中的小刀/攻击历史，不动由格挡乘算 mirror 读取分支格挡历史；出牌入口不再改写 Power 生命周期。

失败基线 `6eba58023b214ead8829ed5effbad6ee` 在 `MIXED-POWER-ACQUISITION-ORDER` 得到 `P[1] expected Strength actual PhantomBlades`。最终 `8f5c8d96d9e342e8a2163e54e359c0d2` Passed：先制造一次攻击和格挡历史，再按报告顺序获得三种 Power，继续出牌并施加新 Power，完整比较生命、格挡、牌堆、有序 Power、ContinuationStamp、Fork 与父分支隔离。

直接证据范围：代表报告只直接核验了包内元数据与独立日志，没有整包恢复；最终通过的是从代表差异抽出的最小实机夹具。其余 19 份报告按相同首个 `P[index]` 差异、相同三种 Power 和相同临时移除/恢复路径静态归组，没有逐包重放。20 份报告 ID：

`4b188b2e088c4826ba1b14d0252bd3bf`、`baff3f1f64cb40058d79c0cd010fc1bc`、`c3cdb552a1d54ad8ba89d3000652a1b4`、`6f733c67442b4f618488924b03e73a5e`、`1f2e2fce2ec14cbeab33cf38b56df1d9`、`7e1175348df345f4ac0b9279308cd70e`、`d5890345771347ecb6c4e9a596bf6c43`、`f8ecd338197b4485bd4dd5dae88b706d`、`5ef653c8897349dc840cb1bc62f0a935`、`8e68629ec65c4a5b81faffad1e54f10d`、`681d989fbda84b889137f0d80b016eca`、`a8dc35c07c80464896048a13ccf2d23c`、`d9084f013d984a2eb7bbb67be41a9e55`、`1b1e109fdbea4db991f26c900b8fbf24`、`9419be5958224bda8f4b2e9ec0b1f1a7`、`81201f2162284bfcb5516d795c3728cf`、`c4beff8ab33b4dd89b9a9010495df2f7`、`b4c978ef34ec4c82975300e21ad9eccb`、`9037ccf3df9545149f09c4f57e23dd97`、`0a5d8947277343ec9bc251e616cc8725`。
