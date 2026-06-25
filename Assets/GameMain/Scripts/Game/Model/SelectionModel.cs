/****************************************************
    文件：SelectionModel.cs
    作者：k0itoyuu
    日期：2026/06/20
    功能：选中状态管理模型（Model 层）—— 维护当前选中卡牌集合，
          强制最大 5 张的限制，纯逻辑无 Unity 依赖。
*****************************************************/
using System;
using System.Collections.Generic;

namespace Yuu
{
    /// <summary>
    /// 选中状态模型。
    /// 维护当前选中的卡牌列表，强制最大 <see cref="MaxSelectionCount"/> 张的限制。
    /// 纯 C# 逻辑，不含 UnityEngine 依赖。
    /// </summary>
    public class SelectionModel
    {
        /// <summary>
        /// 最大可选中卡牌数量。
        /// </summary>
        public const int MaxSelectionCount = 5;

        private readonly List<CardData> _selectedCards = new List<CardData>();

        /// <summary>
        /// 当前已选中的卡牌数量。
        /// </summary>
        public int SelectedCount
        {
            get { return _selectedCards.Count; }
        }

        /// <summary>
        /// 是否已达到最大选中数量上限。
        /// </summary>
        public bool IsFull
        {
            get { return _selectedCards.Count >= MaxSelectionCount; }
        }

        /// <summary>
        /// 当前已选中卡牌列表（只读）。
        /// </summary>
        public IReadOnlyList<CardData> SelectedCards
        {
            get { return _selectedCards; }
        }

        /// <summary>
        /// 选中状态变更事件。
        /// 参数1: 发生变更的 <see cref="CardData"/>。
        /// 参数2: true = 被选中, false = 被取消选中。
        /// </summary>
        public event Action<CardData, bool> OnSelectionChanged;

        /// <summary>
        /// 尝试选中一张卡牌。
        /// 若已达上限则返回 false，不进行任何状态修改。
        /// 若该卡已在选中列表中则幂等返回 true。
        /// </summary>
        /// <param name="card">要选中的卡牌数据。不可为 null。</param>
        /// <returns>true = 选中成功；false = 已达上限，选中被拒绝。</returns>
        public bool TrySelect(CardData card)
        {
            if (card == null)
                return false;

            // 已在选中列表，幂等
            if (_selectedCards.Contains(card))
                return true;

            // 已达上限，拒绝
            if (IsFull)
                return false;

            // 执行选中
            _selectedCards.Add(card);
            card.IsSelected = true;

            OnSelectionChanged?.Invoke(card, true);
            return true;
        }

        /// <summary>
        /// 取消选中一张卡牌。
        /// 若该卡不在选中列表中则幂等返回 false。
        /// </summary>
        /// <param name="card">要取消选中的卡牌数据。不可为 null。</param>
        /// <returns>true = 成功取消选中；false = 该卡未被选中。</returns>
        public bool Deselect(CardData card)
        {
            if (card == null)
                return false;

            if (!_selectedCards.Remove(card))
                return false;

            card.IsSelected = false;

            OnSelectionChanged?.Invoke(card, false);
            return true;
        }

        /// <summary>
        /// 清除所有选中状态。
        /// 遍历当前所有已选卡牌并逐一取消选中，触发相应事件。
        /// </summary>
        public void ClearAll()
        {
            // 复制列表以避免在遍历中修改集合
            var snapshot = new List<CardData>(_selectedCards);
            foreach (var card in snapshot)
            {
                Deselect(card);
            }
        }

        /// <summary>
        /// 检查指定卡牌是否处于选中状态。
        /// </summary>
        /// <param name="card">要检查的卡牌数据。</param>
        /// <returns>true = 已选中；false = 未选中或 card 为 null。</returns>
        public bool IsSelected(CardData card)
        {
            if (card == null)
                return false;

            return _selectedCards.Contains(card);
        }
    }
}
