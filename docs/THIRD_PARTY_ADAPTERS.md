# 第三方 Mod 适配手册

写给想让战斗路线求解器看懂自家 Mod 的作者。

求解器不认识任何第三方内容。它靠一套**镜像**（mirror）在自己的模拟里重现游戏行为，而镜像是
按类型登记的。你的牌、Power、遗物、药水没有登记，求解器就只能退化处理，路线会算错。

这份文档讲：默认会发生什么、有哪些登记点、登记的纪律、怎么验证自己做对了。

## 0. 先判断你要不要读下去

| 你的 Mod | 要做什么 |
|---|---|
| 清单里 `affects_gameplay: false`（纯美术、UI、音效） | **什么都不用做**，自动放行 |
| 只改地图、进幕、事件、休息处、商店这类战斗外内容 | **什么都不用做**，求解器会判定它对战斗惰性 |
| 加了牌、Power、遗物、药水、敌人，或改了战斗数值 | 往下读 |

前两条是自动的。第三条不做适配的话，装上你的 Mod 之后求解器会直接停在
「检测到不兼容的第三方 Mod」，玩家用不了。

## 1. 求解器默认怎么对待未知内容

### 1.1 门禁：先让 Mod 进得来

求解器扫描所有 ModHelper 战斗 hook 订阅者。放行有三条路：

1. 清单 `affects_gameplay: false`；
2. `PredictionModHookSubscriberInertness.IsCombatInert` 判定为战斗惰性——只重写了战斗外的
   hook，或者只重写了战斗开始 / 战斗结束 hook（前者的效果已经落在被捕获的根状态里，后者在
   胜负判定之后才分发，求解器搜到战斗结束就停）；
3. 在 `PredictionModHookSubscriberCapture.KnownPreRootSubscriberTypeNames` 白名单里。

三条都不满足就抛 `IncompatibleGameplayModException`，整个求解器停摆。

> **当前限制。** 第 3 条那份白名单是私有静态集合，没有公开登记入口。目前只能靠 publicizer
> 写进去。这是明确要补的扩展点之一，见第 6 节。

### 1.2 镜像：进来之后每个类型的五种下场

每个被镜像的虚方法都有一张按**精确运行时类型**索引的注册表。查一个类型会得到五种结果之一
（`MirrorDispatchKind`）：

| 结果 | 含义 | 后果 |
|---|---|---|
| `NotOverridden` | 这个类型没有重写该方法 | 走基类行为，**正确**，不用管 |
| `Handled` | 有登记的镜像 | **正确**，这是你要达到的状态 |
| `Inferred` | 没登记，但结构上能推断出一个尽力而为的实现 | 可能对，求解器**记一条风险** |
| `Ignored` | 人工复核过，确认对预测无影响 | 正确，静默 |
| `Unsupported` | 有重写但没有安全的预测实现 | 求解器**记一条风险** |

**风险不是静默错误。** 求解器会在路线上打红字标明「这里有未镜像的效果」，玩家看得见，日志里
也有 `COVERAGE source=... method=... reason=...`。但红字**只是显示**——它不会把不可能的续接从
搜索里去掉。凡是会改变「接下来还能做什么」的效果（强制结束回合、额外回合、让某张牌打不出），
必须真的建模，只记风险不够。

### 1.3 只读 hook 会自动回落

`Modify*` / `Should*` 这类只读 hook，求解器在无法镜像时会回落到你自己的实现，本来就正确。
姿态的伤害倍率、费用修改这类就在其中，一般不需要登记。

## 2. 登记点总表

### 2.1 统一形状的镜像注册表（44 张）

绝大多数登记走同一个形状：

```csharp
XxxMirrors.Registry.Register<TYourType>(handler);
```

44 张注册表按域分布在 `src/Engine/InCombat/Mirrors/` 下：

| 目录 | 注册表数 | 覆盖什么 | 你多半要用的 |
|---|---|---|---|
| `Hooks/` | 37 | 战斗 hook：攻击、格挡、伤害、死亡、卡牌、球体、回合边界 | 按你重写了哪个 hook 挑，例如 `AfterDamageGivenMirrors` |
| `Cards/` | 4 | 出牌、可打出性、回合结束留手、结算落点 | `CardOnPlayMirrors`、`CardIsPlayableMirrors` |
| `Potions/` | 1 | 药水使用 | `PotionOnUseMirrors` |
| `Enchantments/`、`Afflictions/` | 各 1 | 附魔与病症的出牌效果 | 少见 |

**注意目录里的文件数比注册表多。** `Cards/` 下有十几个 `*Mirrors.cs`，但注册表只有 4 张——
`BespokeCardMirrors`、`CardGenerationCardMirrors` 这些是**处理器文件**，它们往
`CardOnPlayMirrors.Registry` 这张共享注册表里登记，自己不持有注册表。找登记入口时认
`static readonly Registry Registry` 这个字段，不要认文件名。

**怎么知道自己要登记哪几个。** 把你的每个类型对基类虚方法的重写列出来，和这 44 张表逐一对照。
只重写了求解器不分发的方法，不用登记；重写了它分发的方法，就要登记。这一步不要靠印象，
要交叉核对——漏一个的表现是「效果看起来正常但其实没发生」。

同一张表里 `Register` 用的是 `Dictionary.Add`，**重复登记会抛异常**，不会静默覆盖。

### 2.2 战略估值：会改变出牌顺序的 Power

```csharp
StrategicEffectMirrors.Register<TYourPower>(requirements, evaluate, host);
```

只有当你的 Power **收益取决于它和别的动作的先后关系**时才需要。详见
[第三方 Power 的战略估值登记](third-party-strategic-effects.md)。

不登记的后果：求解器按叠加层数记一点 `ScalingPotential` 兜底。对大多数 Power 够用；对
「自己不给甲、但让后续攻击给甲」这类会被排到错误位置。

### 2.3 还没有登记入口的地方

见第 6 节。目前只能 Harmony 打补丁，或者等对应的扩展点合并。

## 3. 登记的纪律

这几条不是风格建议，是踩过的坑。

### 3.1 加载时一次性登记完

注册表**按精确运行时类型缓存查询结果，而且 `Register` 不会让缓存失效**。一旦某个类型被查过
一次（拿到 `Inferred` 或 `Unsupported`），之后再登记也不会生效，而且不报错。

所以：在 Mod 初始化时把所有登记做完，绝不在战斗中途登记。

### 3.2 失败要关死，不要装一半

自检不通过时**一个镜像都不要登记**。装一半比不装更糟：求解器会拿着一部分正确的镜像给出看起来
可信的路线，缺掉的那部分静默变成空操作。全都不装的话，求解器会明确停在门禁上并显示原因，
玩家至少知道出了事。

同理，解析不到 Harmony 目标方法就抛异常让整层注册失败，不要跳过继续。

### 3.3 按反编译出来的实现写，不要照卡面文字猜

卡面文字和实现经常不一致：触发时机、目标选择、数值来源、结算顺序。逐条对照反编译源码写，
一张牌一个方法、一行一效果、按原版的调用顺序排列，这样可以逐行复核。

典型的坑：变量键名。`PowerVar<T>` 单参数构造生成的键是 `typeof(T).Name`（例如
`VulnerablePower`），不是卡面上显示的那个词。写错会让整次搜索失败。

### 3.4 钉死你依赖的版本，并在运行期自检

求解器的内部接口会变。适配层应当：

- 构建期引用确定版本；
- 运行期核对自己用到的那几个方法签名和字段还在不在，不在就干净地拒绝加载。

同理，如果你在适配**别人的** Mod，按文件哈希钉死比按版本号更稳——作者不一定每次改动都升版本
号，而一个没升版本号的签名改动会让某张牌变成「没有效果但看起来正常」。

### 3.5 时机比数值更容易错

抽牌发生在触发它的那张牌离开出牌堆之前还是之后、Power 在这张牌自己结算之前还是之后到位、
「上一张牌」是本回合的还是整场的——这些一错，数值全对但结果不对。写注释说明你选的时机和依据。

## 4. 怎么验证自己做对了

### 4.1 两条验收标准

**不要用胜率或手感做验收。** 镜像低估自己的伤害会让求解器打得保守，于是活得久——这种路线能
通过手感检验，通不过严格 diff。

标准是：

1. **严格 diff 零差异**：模拟的终局状态和真实终局状态逐字段相等。
2. **`PredictionGaps` 里非补偿项为空**：求解器自己不报告任何未镜像效果。

胜率是在这两条都干净**之后**才有意义的指标，用来抓 diff 抓不到的东西，比如某个 Power 在估值
函数里定价错了。反过来先看胜率，会让你在错误的地方停下来。

### 4.2 夹具要能自己验算，而且要有反向对照

好夹具的判据落在能用算术自己验的量上——能量够不够打第二张牌、格挡数值、正好击杀的回合数——
而不是「跑起来不报错」。

**每条夹具都要做一次反向对照**：把你要验的那行登记注释掉重新构建，夹具必须不过；加回来必须
过。没做过反向对照的夹具证明不了任何事。

无头夹具的跑法见 [HEADLESS_TESTING.md](HEADLESS_TESTING.md)。

### 4.3 用玩家的问题包，不要只看描述

求解器自带问题包导出，里面有完整路线、逐检查点状态、日志和一份自动分类（例如
`BetterWorldline 预计战损 11 → 0` 就是「玩家手打比求解器的路线好，好 11 点血」）。
带问题包基本都能定位；只有文字描述通常不够。

## 5. 一个完整例子

观者 Mod 的「以手拒之」：打出后给目标挂一层反弹格挡，之后玩家每打中这个敌人一段就起
`Amount` 点甲。

**症状。** 手里以手拒之 + 两张打击，敌人这回合打 4 点。求解器给的顺序是
「打击 打击 以手拒之」，第 1 回合 `max_block=0`，白挨 4 点。第 2、3、4 回合都是
`max_block=4 actual_block=4`——层数一旦挂上去后面每回合都算得对，唯独挂上去的那一回合被浪费。

**排查。** 先确认镜像本身对不对：反弹格挡的钩子分发和逐条判定（目标判定、施加者判定、
`IsPoweredAttack`、`TotalDamage > 0`、受益者三级回退、`Unpowered` 不吃敏捷、不自减）都和反编译
出来的实现核对过，两条夹具锁住了这一半。**镜像是对的，坏的是排序。**

**根因。** `ClassifyActionOptionFamilies` 判一个动作算不算 `ImmediateDefense`，看四样：这次动作
的格挡增量、`ProjectedPlayerHp`、`PlayerHp`、`StrategicEffects.PreventionPotential`。以手拒之
打出的瞬间这四样一样都不动——它自己不给甲，而反弹格挡这层 Power 挂在**敌人身上**，设置估值那圈
原本只统计玩家自己身上的增益。于是它被归成一张纯 `ImmediateOffense`，和打击同族但伤害更低，
在族内代表里被打击压掉。

**修法。** 用 `StrategicEffectMirrors.Register<BlockReturnPower>(..., StrategicEffectHost.Enemy)`
登记估值。登记之后打出它会让 `PreventionPotential` 从 0 变正，于是它同时进 `ImmediateDefense`
族，不再被压掉。

**验证。** 夹具 `WATCHER-TALK-TO-THE-HAND-ORDERING`：以手拒之加两张打击、3 能量，判
`max_block >= 4`（以手拒之给 2 层，两张打击各 1 段，排最前面 = 4 甲，排中间 = 2，排最后 = 0）。
做过反向对照：注释掉那行登记，夹具不过。

**这个例子的一般教训**：现象是「AI 不会用这张牌」，根因既不在这张牌的镜像里，也不在搜索深度或
估值权重上，而在动作分类那一层。排查顺序应当是：先确认镜像对不对，再看它有没有被搜索看见，
最后才怀疑估值。

## 6. 已知的封闭开关

下面这些位置目前是按原版类型写死的开关，第三方登记不进去。要用只能 Harmony 打补丁，或者等
对应扩展点合并。列在这里是为了让你知道撞上了什么，而不是以为自己写错了。

| 位置 | 症状 | 状态 |
|---|---|---|
| `PredictionModHookSubscriberCapture.KnownPreRootSubscriberTypeNames` | 私有静态白名单，没有公开登记入口 | 待做 |
| `PotionChoiceSupport.RequiresChoice` / `GetSpec` / `Apply` | 第三方药水的玩家选择永远不会被展开成搜索分支；`RequiresChoice` 对第三方类型恒为 `false` | 待做 |
| `CardChoiceSupport` 的选牌规格 | 第三方卡牌的选牌效果同上 | 待做 |
| `CorePowerSupport.TriggerPlayerSideTurnEndEffects`、`FlushPlayerHandAtTurnEnd`、`TurnStartPowerSupport.TriggerAfterPlayerTurnStart`、`SimulatedCombatState.TriggerRelicsAfterPlayerTurnStart` | 回合边界的效果没有注册表 | 待做 |
| `SimulatedCombatState.TryPrepareExtraPlayerTurn` / `TryPrepareLiveExtraPlayerTurn` / `ConsumeExtraTurnSources` | 额外回合的来源硬编码，只认龙涎香和帕尔之眼 | 待做 |
| `CombatPredictionSimulator.OnPlayWrapper` | 出牌后补抽没有挂载点 | 待做 |

**这些开关新增或改动时，必须在同一个提交里更新这张表和本文档对应章节。** 见
[AGENTS.md](../AGENTS.md) 第 9 节。

## 7. 相关文档

- [架构与职责地图](ARCHITECTURE.md)：源码入口和所有权，`§4.2 Mirror` 是镜像层的位置。
- [战斗钩子覆盖目录](COMBAT_HOOK_COVERAGE.md)：求解器分发哪些 hook。
- [第三方 Power 的战略估值登记](third-party-strategic-effects.md)。
- [无头测试](HEADLESS_TESTING.md)：夹具怎么跑。
- [检查点回放](CHECKPOINT_REPLAY.md)：问题包怎么导入。
