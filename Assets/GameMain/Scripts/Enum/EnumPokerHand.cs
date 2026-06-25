//------------------------------------------------------------
// 此文件参照 PokerHands.txt 数据表生成
// 生成时间：2026-06-24
//------------------------------------------------------------

namespace Yuu
{
    /// <summary>
    /// 牌型枚举，值与 PokerHands 配置表 Id 一一对应。
    /// </summary>
    public enum EnumPokerHand : int
    {
        /// <summary>
        /// 无
        /// </summary>
        None = 0,

        /// <summary>
        /// 高牌
        /// </summary>
        HighCard = 1001,

        /// <summary>
        /// 一对
        /// </summary>
        OnePair = 1002,

        /// <summary>
        /// 两对
        /// </summary>
        TwoPair = 1003,

        /// <summary>
        /// 三条
        /// </summary>
        ThreeOfAKind = 1004,

        /// <summary>
        /// 顺子
        /// </summary>
        Straight = 1005,

        /// <summary>
        /// 同花
        /// </summary>
        Flush = 1006,

        /// <summary>
        /// 葫芦
        /// </summary>
        FullHouse = 1007,

        /// <summary>
        /// 四条
        /// </summary>
        FourOfAKind = 1008,

        /// <summary>
        /// 同花顺
        /// </summary>
        StraightFlush = 1009,

        /// <summary>
        /// 五条
        /// </summary>
        FiveOfAKind = 1010,

        /// <summary>
        /// 同花葫芦
        /// </summary>
        FlushFullHouse = 1011,

        /// <summary>
        /// 同花五条
        /// </summary>
        FlushFiveOfAKind = 1012,
    }
}
