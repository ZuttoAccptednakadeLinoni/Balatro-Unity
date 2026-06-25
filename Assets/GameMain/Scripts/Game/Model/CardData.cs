/****************************************************
    文件：CardData.cs
    作者：k0itoyuu
    日期：2026/06/20
    功能：卡牌游戏数据模型（Model 层）—— 纯数据类，
          不含任何 UnityEngine 依赖，作为卡牌状态的单点真相源。
*****************************************************/

namespace Yuu
{
    /// <summary>
    /// 卡牌游戏数据模型。
    /// 存储卡牌的游戏逻辑数据（花色、点数、筹码值）和选中状态。
    /// 纯 C# 类，无 UnityEngine 依赖，Model 层不直接操作 UI。
    /// </summary>
    public class CardData
    {
        /// <summary>
        /// 卡牌唯一标识。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 花色：Hearts（红心）、Spades（黑桃）、Diamonds（方块）、Clubs（梅花）。
        /// </summary>
        public string Suit { get; set; }

        /// <summary>
        /// 点数：2-14，其中 11=J, 12=Q, 13=K, 14=A。
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// 基础筹码值（Balatro 中每张牌有对应的筹码贡献）。
        /// </summary>
        public int BaseChips { get; set; }

        /// <summary>
        /// 是否处于选中状态。
        /// 由 SelectionModel 统一管理，外部不应直接修改。
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// 创建一张卡牌数据。
        /// </summary>
        /// <param name="id">唯一标识。</param>
        /// <param name="suit">花色。</param>
        /// <param name="rank">点数。</param>
        /// <param name="baseChips">基础筹码值。</param>
        public CardData(int id, string suit, int rank, int baseChips)
        {
            Id = id;
            Suit = suit;
            Rank = rank;
            BaseChips = baseChips;
            IsSelected = false;
        }

        /// <summary>
        /// 返回卡牌的可读描述。
        /// </summary>
        public override string ToString()
        {
            string rankStr = Rank switch
            {
                11 => "J",
                12 => "Q",
                13 => "K",
                14 => "A",
                _ => Rank.ToString()
            };
            return string.Format("[{0}] {1} {2} (Chips: {3})", Id, Suit, rankStr, BaseChips);
        }
    }
}
