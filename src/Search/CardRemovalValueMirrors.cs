using MegaCrit.Sts2.Core.Models;

namespace CombatSolver;

/// <summary>第三方牌在移除类选择里按哪一类起手牌估值。</summary>
internal enum BasicCardRemovalKind
{
    /// <summary>起手打击这一类：数值按伤害算，权重与原版起手打击相同。</summary>
    Strike,

    /// <summary>起手防御这一类：数值按格挡算，权重与原版起手防御相同。</summary>
    Defend,
}

/// <summary>
/// 第三方起手牌的移除估值登记表。
/// </summary>
/// <remarks>
/// <para>
/// 消耗、转变这类**移除**选择按 <c>CardChoiceSupport.RemovalPriority</c> 从低到高排序，估值低的
/// 先被移除。通用估值把伤害记满、格挡打八折，于是一张 6 伤害的起手打击得 6.0，比一张 5 格挡的
/// 起手防御（4.0）还高——按通用估值排，先移除的会是防御。原版五个角色的实战优先级相反，所以
/// <c>BasicCardRemovalValue</c> 用一张按类型写死的表把这十张起手牌压回正确的相对位置。
/// </para>
/// <para>
/// 那张表只列原版十张，注释里写明了理由：其他来源的打击、防御「强弱取决于各自的机制，这里没有
/// 依据替它们排序」。这个判断对求解器成立——但对 Mod 作者不成立，<b>他知道自己那张牌是不是起手
/// 牌</b>。所以这里开一个登记点，让他自己声明，而不是让求解器去猜。
/// </para>
/// <para>
/// 登记的是<b>类别</b>，不是数值：权重仍然是求解器这一侧的 <c>BasicStrikeRemovalWeight</c> 与
/// <c>BasicDefendRemovalWeight</c>，第三方只说「这是我的起手打击」。这样升级差别照样保留
/// （6 伤害与 9 伤害排序不同），也不会有人往里塞一个凭空编出来的移除价值。
/// </para>
/// <para>
/// 不登记的后果是<b>静默的</b>：Mod 角色的起手打击按通用估值算成一张有伤害的好攻击牌，于是净化、
/// 洗炼这类牌永远不会先烧它。实测一场女王：玩家手打消耗掉三张观者打击、把全知与内心宁静留在
/// 牌库里，求解器反过来消耗了全知、内心宁静、痛击，把四张打击留着——两边同样有疾风连击 4，
/// 而只有前者的牌库能持续转起来。
/// </para>
/// <para>
/// 登记表为空时 <c>BasicCardRemovalValue</c> 一行都不多走，排序与开这个口子之前逐位相同。
/// 登记在初始化期间完成，任何搜索开始后保持登记表不变。
/// </para>
/// </remarks>
internal static class CardRemovalValueMirrors
{
    private static readonly Dictionary<Type, BasicCardRemovalKind> Registry = [];

    /// <summary>登记表是否为空。空表时下游可以整段跳过。</summary>
    public static bool IsEmpty => Registry.Count == 0;

    /// <summary>
    /// 声明一张第三方牌属于哪一类起手牌。
    /// </summary>
    /// <typeparam name="TCard">
    /// 你的起手牌类型。按<b>精确运行时类型</b>匹配，所以升级版与未升级版如果是同一个类型就一起
    /// 生效；升级差别由牌自己的 <c>Damage</c> / <c>Block</c> 基础值体现，不需要分别登记。
    /// </typeparam>
    /// <param name="kind">按打击还是按防御估值，见 <see cref="BasicCardRemovalKind"/>。</param>
    /// <remarks>
    /// 只登记<b>起手牌</b>。这个入口的语义是「这张牌和原版起手打击/防御在牌库里的地位相同」，
    /// 不是「给这张牌调一个移除价值」。给一张真正有用的牌登记，等于让求解器优先把它烧掉。
    /// </remarks>
    public static void Register<TCard>(BasicCardRemovalKind kind)
        where TCard : CardModel
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的起手牌类别。");
        // 和别的镜像登记表同一口径：重复登记是错误，不静默覆盖。
        Registry.Add(typeof(TCard), kind);
    }

    /// <summary>
    /// 撤销一次登记。<b>只给无人测试用</b>：测试要自己登记再清干净，否则同一个进程里后面的
    /// 用例都会被影响。真实 Mod 不要调用，更不要在搜索进行中调用。
    /// </summary>
    internal static void UnregisterForTesting<TCard>()
        where TCard : CardModel
        => Registry.Remove(typeof(TCard));

    /// <summary>取这张牌登记的类别；没登记过时返回 <c>null</c>。</summary>
    public static BasicCardRemovalKind? Kind(CardModel card)
        => Registry.Count == 0
            ? null
            : Registry.TryGetValue(card.GetType(), out BasicCardRemovalKind kind)
                ? kind
                : null;
}
