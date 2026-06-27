//------------------------------------------------------------
// LuaJokerBridge - C# 侧 API 导出给 Lua 的桥接层
// Phase 1: 最小 stub，随后续阶段逐步扩展
// 所有 public static 方法均可从 Lua 通过 CS.Yuu.LuaJokerBridge.XXX() 调用
//------------------------------------------------------------

using UnityEngine;
using XLua;

namespace Yuu
{
    /// <summary>
    /// 导出给 Lua 的 C# API 桥接层。
    /// 对应原版 Balatro 中直接操作 G.E_MANAGER、G.playing_card 等全局状态的部分。
    /// 所有副作用操作（销毁卡牌、生成卡牌、修改数值等）必须走 Bridge，
    /// Lua 层只做纯逻辑计算。
    /// </summary>
    [LuaCallCSharp]
    public static class LuaJokerBridge
    {
        // ═══════════════════════════════════════════════════════════════
        // 小丑自身操作（Phase 1 最小集合）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>摧毁自身（Popcorn、Turtle Bean、Ramen 等自毁）</summary>
        public static void DestroySelf(int jokerInstanceId)
        {
            Debug.Log($"[LuaJokerBridge] DestroySelf({jokerInstanceId}) — TODO: implement");
        }

        /// <summary>摧毁其他小丑（Madness、Ceremonial Dagger）</summary>
        public static void DestroyJoker(int jokerInstanceId)
        {
            Debug.Log($"[LuaJokerBridge] DestroyJoker({jokerInstanceId}) — TODO: implement");
        }

        // ═══════════════════════════════════════════════════════════════
        // 查询（Phase 1 最小集合）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>获取当前剩余弃牌次数</summary>
        public static int GetRemainingDiscards()
        {
            // TODO: 从 GameController 获取实际值
            Debug.Log("[LuaJokerBridge] GetRemainingDiscards() — returning fallback 3");
            return 3;
        }

        /// <summary>获取已激活小丑数量</summary>
        public static int GetJokerCount()
        {
            return 0; // TODO: 从 JokerManager 获取
        }

        /// <summary>获取牌组大小</summary>
        public static int GetDeckSize()
        {
            return 52; // TODO: 从 GameController 获取
        }

        /// <summary>获取当前手牌数</summary>
        public static int GetHandSize()
        {
            return 8; // TODO: 从 GameController 获取
        }

        // ═══════════════════════════════════════════════════════════════
        // 事件队列（Phase 4 实现）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>推入一个副作用事件到队列</summary>
        public static void EnqueueEvent(System.Action action)
        {
            Debug.Log("[LuaJokerBridge] EnqueueEvent() — TODO: implement");
            // TODO: JokerEventManager.AddEvent(action)
        }
    }
}
