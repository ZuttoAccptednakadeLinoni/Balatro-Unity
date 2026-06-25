/****************************************************
    文件：UIGameInfoForm.cs
    作者：k0itoyuu
    日期：2026/06/24
    功能：游戏信息界面 —— 显示出牌后的牌型名称、加码(Add)、乘倍(Mul)。
          由 UICardForm 在出牌后调用 UpdateHandResult 刷新显示。
*****************************************************/
using TMPro;
using UnityEngine;

namespace Yuu
{
    public class UIGameInfoForm : UGuiFormEx
    {
        [Header("牌型名称")]
        [SerializeField]
        private TextMeshProUGUI _handNameText;

        [Header("加码 / 筹码")]
        [SerializeField]
        private TextMeshProUGUI _addText;

        [Header("乘倍 / 倍率")]
        [SerializeField]
        private TextMeshProUGUI _mulText;

        /// <summary>
        /// 当前牌型判定结果。
        /// </summary>
        public PokerHandResult CurrentResult { get; private set; }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            // 初始化为默认值
            if (_addText != null)
                _addText.text = "0";
            if (_mulText != null)
                _mulText.text = "0";
            if (_handNameText != null)
                _handNameText.text = "";
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
        }

        /// <summary>
        /// 更新牌型判定结果显示。
        /// 由 UICardForm 在出牌完成后调用。
        /// </summary>
        /// <param name="result">牌型判定结果。</param>
        public void UpdateHandResult(PokerHandResult result)
        {
            CurrentResult = result;

            if (result == null)
                return;

            if (_addText != null)
                _addText.text = result.Add.ToString();

            if (_mulText != null)
                _mulText.text = result.Mul.ToString();

            if (_handNameText != null)
                _handNameText.text = GetHandTypeDisplayNameStatic(result.HandType);
        }

        /// <summary>
        /// 获取牌型的中文显示名称。
        /// </summary>
        public static string GetHandTypeDisplayNameStatic(EnumPokerHand handType)
        {
            return handType switch
            {
                EnumPokerHand.None => "",
                EnumPokerHand.HighCard => "高牌",
                EnumPokerHand.OnePair => "一对",
                EnumPokerHand.TwoPair => "两对",
                EnumPokerHand.ThreeOfAKind => "三条",
                EnumPokerHand.Straight => "顺子",
                EnumPokerHand.Flush => "同花",
                EnumPokerHand.FullHouse => "葫芦",
                EnumPokerHand.FourOfAKind => "四条",
                EnumPokerHand.StraightFlush => "同花顺",
                EnumPokerHand.FiveOfAKind => "五条",
                EnumPokerHand.FlushFullHouse => "同花葫芦",
                EnumPokerHand.FlushFiveOfAKind => "同花五条",
                _ => handType.ToString(),
            };
        }
    }
}
