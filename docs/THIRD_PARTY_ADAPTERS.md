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

`Modify*` / `Should*` 这类只读 hook 中，允许原实现回落的入口会调用 Mod 自己的实现。
适配者仍须核对读取的数据属于当前预测分支；只读方法也可能读到 live 手牌、Power 或费用。
姿态伤害倍率、费用修改等效果只有在完整分支差分通过后，才能认定原实现回落适用。

`CardIsPlayableMirrors` 使用独立的镜像分派：继承基类的牌返回 `true`；有显式登记的重写执行
对应镜像；未登记的重写记录 `MethodNotMirrored`，并返回调用方提供的 `true`。这条覆盖提示
用于暴露缺失语义，适配者必须登记真实可打出条件，才能保证搜索按预测手牌判断合法性。

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

### 2.3 药水的玩家选择

```csharp
PotionChoiceMirrors.Register<TYourPotion>(spec, apply);
```

只有当你的药水会让玩家当场做选择时才需要。不登记的后果很硬：`PotionChoiceSupport.RequiresChoice`
对第三方类型恒为 `false`，于是求解器**根本不为它开搜索分支**——它会把这瓶药当成一个没有收益的
动作，随手插在路线里的某个位置。药水自己的 `PotionOnUseMirrors` 镜像补不了这个：等那个钩子触发
的时候，「要不要开分支」早就已经被否决了。

两个委托：

- `spec(simulator, potion)` 返回一个 `CardChoiceSpec`：候选、上下界、效果。候选**必须是玩家在
  原生页面上真正看到的那几张，顺序也要一致**，否则部署时按卡牌令牌在页面上定位会错位。
  下界给 0 表示「可以一张都不选」。
- `apply(simulator, potion, choice)` 按选中的结果在模拟里施加效果，返回是否已经结算完
  （还有嵌套选择挂起时返回 `false`，和原版同一口径）。

效果用 `PlanChoiceEffect.ModDefined`。部署侧按卡牌令牌在原生页面上定位，本来就与效果无关；
这个值只是明确表示「结算由登记方负责」，别的效果分支不会误接手。求解器自己从不产生这个值。

登记之后，你的药水和原版带选择的药水走同一条通道：搜索按你的 spec 展开分支、把选中的结果记进
计划、部署时照常应答原生页面，而效果由你的 `apply` 施加——求解器不需要认识任何第三方效果。

**一个真实例子。** 观者的形态药剂让玩家在平静和愤怒之间二选一。原版实现里比的是引用相等
（`val == calmChoice`），但两张选项牌是两个不同的类型、各只有一张，所以按类型判完全等价。

不登记的代价实测过：鬼祟珊瑚群那一场，求解器第 1 回合 `max_block=14 actual_block=3`、掉 11 血；
手打是「爆发+ 进愤怒 → 停顿 3+9=12 甲 → 如水 → 药水选平静退出愤怒」，如水在回合结束因为平静
再给 5 甲，17 甲挡掉 14 点，0 掉血。求解器不肯进愤怒的判断在它自己的世界观里是对的——进去了
退不出来就是挨双倍伤害；它只是不知道那瓶药能退出来。

### 2.4 从给定牌堆候选中弃牌

`ICombatPredictionChoiceSink.ResolvePileDiscardChoice(simulator, sourceId, player, sourcePile, options, maxBranches)`
供镜像处理器提交可选弃牌请求。`options` 是效果当时真正展示的有序候选，例如抽牌堆顶的几张牌。
允许空选，选中牌进入弃牌堆，数量范围为 `0..options.Count`。返回 `false` 表示选择挂起，调用方
应向上传播未完成状态；搜索补齐选择后会从稳定父节点重放，返回 `true` 才继续后续效果。

该入口沿用已有动作选择、计划记录和原生页面部署通道。手牌之外的弃牌排序使用源牌堆平均牌值
减去被弃牌牌值，并加上弃牌触发收益。`maxBranches` 对排序后的候选设置保留上限，省略时沿用
现有枚举策略；传 `1` 只保留排序第一项，会牺牲其他选择路线，适配者应使用目标场景验证取舍。

### 2.5 卡牌的玩家选择

此入口随 PR #56 合入，并于 `0.32.0` 发布。使用此入口的适配 Mod 应将 CombatSolver 最低依赖设为 `0.32.0`。

```csharp
CardChoiceMirrors.Register<TYourCard>(spec, apply);
```

和药水那条是同一堵墙的两面。`CardChoiceSupport.GetSpec` 是按原版卡牌类型写死的 `switch`，
默认分支返回 `null`，也就是「这张牌没有选择」。第三方卡牌落到那里就是这个答案，于是它的选牌
效果**永远不会被展开成搜索分支**：牌照样打得出去，效果在模拟里静默变成空操作。卡牌自己的
`CardOnPlayMirrors` 补不了这个——选择的展开发生在出牌路径上，不在效果镜像里。

两个委托：

- `spec(simulator, playedCard, card)` 返回一个 `CardChoiceSpec`。
- `apply(simulator, combat, playedCard, card, choice)` 施加效果，返回是否已经结算完。

比药水那条多两件要注意的事：

1. **升级等级必须对。** 部署时按 CardId 加升级等级在原生页面上定位选项。三选一这类牌通常会让
   三张选项跟着本牌一起升级，`spec` 里就要把选项牌也升级，否则部署定位不到。
2. **选项牌不在任何模拟牌堆里。** 所以 `apply` 拿到的是计划里的 `PlanCardToken`，不是
   `PredictedCard`；按 CardId 自己认，求解器不会替你解析。数值要读就从选项牌自己的
   `DynamicVars` 上读，不要写死。

效果同样用 `PlanChoiceEffect.ModDefined`。求解器自己从不产生这个值；如果它出现在卡牌选牌上
而没有登记方认领，结算会直接抛，不会静默空操作。

**一个真实例子。** 观者的许愿是 3 费，打出后在「力量 +3」「多层护甲 6」「金币 25」之间三选一
（升级后 4 / 8 / 30，三张选项牌各自的 `MagicNumber` 就是这三个数）。三个选项的单位完全不同，
但都不需要新的估值刻度：力量和多层护甲本来就是 Power，金币走求解器现成的长期资源刻度
（`GainPlayerGold` 加 `RecordLongTermResource`，`贪婪之手` 就是面值直记）。登记成三个真分支之后，
「值不值这 3 点能量」和「三个里挑哪个」都由搜索自己比出来，不需要写任何策略规则。

### 2.6 Power 的隐藏状态进指纹

```csharp
// 状态在普通私有字段里：只要这一条。
PowerHiddenStateMirrors.Register<TYourPower>(
    "TotalMantraGained",
    (simulator, power) => power.某个私有计数);

// 状态在 _internalData 里：还要这一条，否则模拟一开始读到的是初值。
PowerHiddenStateMirrors.RegisterRootCapture<TYourPower>(
    (simulator, clone, original) =>
        simulator.StateStore.GetReadOnly(clone, () => new MyState(original)));
PowerHiddenStateMirrors.Register<TYourPower>(
    "InstanceCount",
    (simulator, power) => simulator.StateStore.Peek(power, static p => new MyState(p)).Count);
```

状态指纹里 Power 的通用部分只收 `DynamicVars`。把语义状态放在 `_internalData` 或普通私有字段里
的 Power 走的是另一条路：`AddTurnStartStates` 按原版类型 `switch`，从 `StateStore` 里的预测状态
取一个计数塞进指纹（虚空形态、硬化外壳、自动机、束缚锁链……）。那个 `switch` 没有第三方入口。

**后果和别的缺口不一样，要分清：**

- **对续接无害。** 续接戳的 Power 段实机侧和模拟侧**共用同一个方法**，只读 `DynamicVars`，两边
  看到的东西一样，所以隐藏状态压根不进戳，也就不会对不上。
- **对搜索去重有害。** 只在这个状态上不同的两条分支指纹相同，会被当成同一个状态**去掉一条**。
  你的镜像算出来的数值是对的，但搜索可能把算得对的那条丢了。

所以这不是「记个 `Unmirrored` 就行」的事——红字只是显示，不会让被去重掉的分支回来。

#### 别往续接戳里塞

`PowerModel.DeepCloneFields` 会把 `_internalData` **重置**成 `InitInternalData()`。模拟克隆读到
的是初值，实机实例读到的是真值。往两侧共用的续接戳里塞这种值，只会让每一回合的续接都对不上
——正是本入口要避免的那种毛病。真要进戳得像遗物那样拆成实机版和预测版两个追加方法，本入口不做
这件事。

#### 靠 `_internalData` 的必须登记根捕获

同样因为克隆会重置，这类 Power 必须用 `RegisterRootCapture` 在根捕获时把实机实例的值搬进
`simulator.StateStore`，此后一律读预测状态，**不要再读克隆上的 `GetInternalData`**。这正是原版
`PowerPredictionStateSupport.CaptureRootState` 在做的事，照它的形状写即可。搜索途中新施加的实例
不走根捕获，它们的 `_internalData` 本来就是初值，预测状态首次取用时按初值起算就是对的。

状态放在普通私有字段里的 Power 不受影响（`MemberwiseClone` 会带过去），只登记读取函数就够了。

#### 三条约束

1. **只收整数。** 原版那个隐藏计数段里全部是整数或枚举；字符串只会出现在展示用的名字上，那类
   字段按 `SemanticStateFieldPolicy` 本来就不该进指纹。
2. **读取函数必须是纯读取。** 它在搜索热路径上被调用很多次，不得有副作用，也不要在里面分配。
3. **返回值只能取决于这个 Power 自己的状态**（含它在 `StateStore` 里的预测状态）。它参与状态
   等价判断，读别处会让等价判断不自洽。

登记多个状态就多调几次 `Register`，名字在同一类型内不得重复，下游按名字排序后依次进指纹。

**两个真实例子，都在观者。** 光辉的伤害等于牌面值加上本场战斗累计获得的真言，累计值在
`WatcherStatePower` 的一个普通私有 `int` 里，只需要读取函数；登记之后「先攒真言再打光辉」和
「直接打光辉」不再被当成同一个状态。天人形态的那个 Power 用 `_internalData` 存一个实例表，每回合
给「总和」点能量再把每个实例加一——总和就是 `Amount`，本来就在指纹里，缺的只是**实例个数**，
也就是下一回合总和的增量；它要根捕获加读取函数两条，登记一个 `InstanceCount` 就够了，不需要把
整张表塞进去。

### 2.7 还没有登记入口的地方

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
| `CorePowerSupport.TriggerPlayerSideTurnEndEffects`、`FlushPlayerHandAtTurnEnd`、`TurnStartPowerSupport.TriggerAfterPlayerTurnStart`、`SimulatedCombatState.TriggerRelicsAfterPlayerTurnStart` | 回合边界的效果没有注册表 | 待做 |
| `SimulatedCombatState.TryPrepareExtraPlayerTurn` / `TryPrepareLiveExtraPlayerTurn` / `ConsumeExtraTurnSources` | 额外回合的来源硬编码，只认龙涎香和帕尔之眼 | 待做 |
| `CombatPredictionSimulator.OnPlayWrapper` | 出牌后补抽没有挂载点 | 待做 |
| `ContinuationStamp.AppendCard` 的 `private=` 段与 `CombatBeamSolver.CaptureCardStateFingerprintForTesting` 的 `switch (preview)` | **卡牌**的隐藏字段按原版类型写死（利爪、基因算法、巨锤、狂暴、镰刀、疯狂科学），第三方卡牌的私有计数进不了指纹。Power 那一侧已有 `PowerHiddenStateMirrors`，见 §2.6 | 待做 |
| `SimulatedCombatState.AddTurnStartStates` 的 `switch (power)` | 原版 Power 隐藏计数按类型写死。第三方走 §2.6 的登记表进同一份指纹，本行只是记下原版那个 `switch` 本身仍然封闭 | 第三方已有入口 |
| `GrowthSource` / `GrowthValues.HasTarget` 与 `SolverGrowthStrategyPanel.SourceCard` | 成长额度仅支持内置八类来源；第三方战略估值登记不会自动获得独立成长配置 | 0.32.0 已发布，尚无公开登记入口 |

**这些开关新增或改动时，必须在同一个提交里更新这张表和本文档对应章节。** 见
[AGENTS.md](../AGENTS.md) 第 9 节。

## 7. 相关文档

- [架构与职责地图](ARCHITECTURE.md)：源码入口和所有权，`§4.2 Mirror` 是镜像层的位置。
- [战斗钩子覆盖目录](COMBAT_HOOK_COVERAGE.md)：求解器分发哪些 hook。
- [第三方 Power 的战略估值登记](third-party-strategic-effects.md)。
- [无头测试](HEADLESS_TESTING.md)：夹具怎么跑。
- [检查点回放](CHECKPOINT_REPLAY.md)：问题包怎么导入。
