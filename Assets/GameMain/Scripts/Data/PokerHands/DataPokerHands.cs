/****************************************************
    文件：DataPokerHands.cs
    作者：k0itoyuu
    日期：2026/06/24
    功能：牌型数据管理 —— 加载 DRPokerHands 配置表，
          整合为 PokerHandData 字典缓存，通过 Id 返回整合数据。
          模式仿照 DataScene。
*****************************************************/
using System.Collections.Generic;
using GameFramework.Data;
using GameFramework.DataTable;
using UnityGameFramework.Runtime;

namespace Yuu.Data
{
    /// <summary>
    /// 牌型整合数据 —— 包装 DRPokerHands，统一对外暴露接口。
    /// 仿照 SceneData 模式，后续可扩展跨表整合。
    /// </summary>
    public sealed class PokerHandData
    {
        private DRPokerHands drPokerHands;

        /// <summary>牌型配置编号。</summary>
        public int Id
        {
            get { return drPokerHands.Id; }
        }

        /// <summary>加码值（Add）。</summary>
        public int Add
        {
            get { return drPokerHands.Add; }
        }

        /// <summary>乘倍值（Mul）。</summary>
        public int Mul
        {
            get { return drPokerHands.Mul; }
        }

        public PokerHandData(DRPokerHands drPokerHands)
        {
            this.drPokerHands = drPokerHands;
        }
    }

    /// <summary>
    /// 牌型数据管理器 —— 加载配置表，构建 Id → PokerHandData 字典缓存。
    /// </summary>
    public sealed class DataPokerHands : DataBase
    {
        private IDataTable<DRPokerHands> dtPokerHands;
        private Dictionary<int, PokerHandData> dicPokerHandData;

        protected override void OnInit()
        {
        }

        protected override void OnPreload()
        {
            LoadDataTable("PokerHands");
        }

        protected override void OnLoad()
        {
            dtPokerHands = GameEntry.DataTable.GetDataTable<DRPokerHands>();
            if (dtPokerHands == null)
                throw new System.Exception("Can not get data table PokerHands");

            // 构建 Id → PokerHandData 字典缓存
            dicPokerHandData = new Dictionary<int, PokerHandData>();

            DRPokerHands[] drPokerHandsArr = dtPokerHands.GetAllDataRows();
            foreach (var drPokerHands in drPokerHandsArr)
            {
                PokerHandData pokerHandData = new PokerHandData(drPokerHands);
                dicPokerHandData.Add(drPokerHands.Id, pokerHandData);
            }
        }

        /// <summary>
        /// 通过牌型 Id 获取整合数据。
        /// </summary>
        /// <param name="id">牌型配置编号。</param>
        /// <returns>牌型整合数据；Id 不存在时返回 null。</returns>
        public PokerHandData GetPokerHandData(int id)
        {
            if (dicPokerHandData.ContainsKey(id))
            {
                return dicPokerHandData[id];
            }

            return null;
        }

        /// <summary>
        /// 获取全部牌型整合数据。
        /// </summary>
        public PokerHandData[] GetAllPokerHandData()
        {
            int index = 0;
            PokerHandData[] results = new PokerHandData[dicPokerHandData.Count];
            foreach (var pokerHandData in dicPokerHandData.Values)
            {
                results[index++] = pokerHandData;
            }

            return results;
        }

        /// <summary>
        /// 检查牌型 Id 是否存在。
        /// </summary>
        public bool HasPokerHand(int id)
        {
            return dicPokerHandData.ContainsKey(id);
        }

        protected override void OnUnload()
        {
            GameEntry.DataTable.DestroyDataTable<DRPokerHands>();

            dtPokerHands = null;
            dicPokerHandData = null;
        }

        protected override void OnShutdown()
        {
        }
    }
}
