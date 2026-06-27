# XLua + Lua 实现小丑模块架构设计文档

> 基于原版 Balatro 小丑模块分析，针对 Yuu Unity 项目（Game Framework + MVC 架构）的 XLua 接入方案。

---

## 一、整体架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                        Unity C# 层                              │
│                                                                 │
│  GameController ──→ JokerManager ──→ LuaEnv (XLua)             │
│       │                  │                    │                 │
│       ▼                  ▼                    ▼                 │
│  PokerHandEvaluator  JokerModel[]     Lua 小丑脚本               │
│  (手牌判定)           (C#数据桥)       (效果逻辑)                  │
│                                            │                    │
│                               ┌────────────┼────────────┐      │
│                               ▼            ▼            ▼      │
│                          joker_1.lua  joker_2.lua  joker_N.lua  │
│                               │            │            │       │
│                               └────────────┼────────────┘       │
│                                            ▼                    │
│                                   joker_registry.lua            │
│                                   (小丑注册/路由表)               │
└─────────────────────────────────────────────────────────────────┘
```

---

## 二、核心设计决策

### 2.1 Lua 与 C# 的职责边界

| 层 | 职责 | 语言 |
|---|---|---|
| **数据定义** | 小丑的静态配置（名称、稀有度、价格、初始参数） | C# DataTable (`.txt`) |
| **运行时状态** | 小丑的累积状态（mult、Xmult、extra 数值） | Lua table（`ability`） |
| **效果逻辑** | `calculate_joker(context)` 的条件分支和返回值计算 | Lua |
| **副作用执行** | 摧毁卡牌、生成卡牌、修改牌组等 Unity 操作 | C# 提供 API，Lua 调用 |
| **事件队列** | 保证动画和逻辑时序的队列 | C# `JokerEventManager` |
| **UI** | 描述文字生成、浮动文字、动画 | C# `UIJokerForm` |

**核心原则：Lua 管"算"，C# 管"做"。**

- Lua 负责纯逻辑计算：给定 context，返回效果值
- C# 负责所有 Unity 相关操作：实例化、动画、UI、存储、对象池

### 2.2 为什么不用 Lua 做所有事

| 考量 | 说明 |
|---|---|
| **性能** | Unity 对象操作（GameObject、Transform、Sprite）在 Lua 侧性能差，每次跨语言调用都有 marshaling 开销 |
| **GC 压力** | XLua GC 与 Unity GC 双重压力，大量临时对象会导致卡顿 |
| **热更范围** | 实际需要热更新的只是**数值、规则和效果逻辑**，UI 和动画不需要热更 |
| **调试** | C# 侧有完整的 IDE 调试支持，Lua 侧调试相对困难 |

---

## 三、项目目录结构

```
Assets/
  Plugins/
    xlua.bundle/                    ← XLua 插件导入（XLua.dll、生成代码等）
  GameMain/
    Scripts/
      Lua/
        XLuaBootstrap.cs            ← LuaEnv 初始化/销毁/热更 Loader
        LuaJokerBridge.cs           ← 小丑相关 C# API 导出给 Lua（[LuaCallCSharp]）
        JokerManager.cs             ← 调度 C#↔Lua 的核心管理器
        JokerEventManager.cs        ← 副作用事件队列
        JokerContext.cs             ← 触发上下文结构体
        JokerEffectResult.cs        ← 效果返回值结构体
        JokerInstance.cs            ← 运行中小丑实例（持有 ability 的 LuaTable 引用）

      LuaScripts/                   ← .lua 脚本（Resources/LuaScripts/）
        joker_init.lua              ← Lua 侧初始化入口
        joker_registry.lua          ← 小丑注册表（路由 name → handler）
        jokers/
          j_banner.lua
          j_ramen.lua
          j_blueprint.lua
          j_brainstorm.lua
          j_photograph.lua
          j_hanging_chad.lua
          j_green_joker.lua
          j_ceremonial_dagger.lua
          ...                       ← 每个小丑一个文件（按需拆分或合并）
        util/
          context_helper.lua        ← context 判断辅助函数
```

---

## 四、C# 侧详细设计

### 4.1 小丑静态配置 — DataTable 扩展

沿用现有的 Game Framework DataTable 机制，新增 `JokerConfig` 数据表：

```
JokerConfig.txt:
# Id    Name                     Rarity  Cost  Config
10001   j_banner                 1       5     extra=30
10002   j_ramen                  2       6     Xmult=2|extra=0.01
10003   j_blueprint              3       10
10004   j_brainstorm             3       10
10005   j_hanging_chad           1       4     extra=2
10006   j_photograph             1       4     Xmult=2
10007   j_green_joker            1       4     extra_hand_add=1|extra_discard_sub=1
...
```

对应的 `DRJokerConfig` DataRow（由 DataTableGenerator 自动生成）。

### 4.2 触发上下文 — JokerContext

对应原版 Balatro 的 `context` table，用一个 C# 类承载：

```csharp
/// <summary>
/// 小丑触发上下文，对应原版 calculate_joker(context) 的 context 参数
/// </summary>
public class JokerContext
{
    /// <summary>触发阶段标识</summary>
    public JokerTriggerType TriggerType { get; set; }

    // ── 各阶段专用数据 ──

    /// <summary>individual 触发时，当前正在计分的牌</summary>
    public CardData IndividualCard { get; set; }

    /// <summary>individual 触发时，牌所在的区域（G.play 或 G.hand）</summary>
    public CardAreaType CardArea { get; set; }

    /// <summary>destroying_card 触发时，正在被摧毁的牌</summary>
    public CardData DestroyedCard { get; set; }

    /// <summary>other_joker 触发时，触发源小丑</summary>
    public JokerInstance OtherJoker { get; set; }

    /// <summary>repetition 触发时，剩余重复次数</summary>
    public int RepetitionRemaining { get; set; }

    /// <summary>end_of_round 触发时，是否游戏结束</summary>
    public bool IsGameOver { get; set; }

    /// <summary>selling_card 触发时，被出售的卡牌</summary>
    public CardData SoldCard { get; set; }

    /// <summary>using_consumeable 触发时，被使用的消耗牌类型</summary>
    public string ConsumableType { get; set; }

    // ── Blueprint/Brainstorm 递归防护 ──

    /// <summary>Blueprint 递归深度计数器（≥1 表示正在复制中）</summary>
    public int BlueprintDepth { get; set; }

    // ── 扩展字段 ──

    /// <summary>预留扩展：C# 侧可以随时追加自定义数据</summary>
    public Dictionary<string, object> ExtraData { get; set; }
}

/// <summary>
/// 小丑触发阶段枚举 — 按原版计分流程分类
/// </summary>
public enum JokerTriggerType
{
    // ── 主计分流程 ──
    Before,             // 出牌前
    JokerMain,          // 主计分阶段（默认分支）
    After,              // 出牌后

    // ── 逐张计分 ──
    Individual,         // 逐张计分（G.play / G.hand）
    Repetition,         // 重复计分

    // ── 小丑互相作用 ──
    OtherJoker,         // 其他小丑触发时

    // ── 回合生命周期 ──
    EndOfRound,         // 回合结束
    SettingBlind,       // 选择盲注
    FirstHandDrawn,     // 摸第一手牌

    // ── 商店相关 ──
    SellingSelf,        // 出售自身
    SellingCard,        // 出售其他卡牌
    RerollShop,         // 商店重骰
    EndingShop,         // 离开商店
    OpenBooster,        // 打开补充包
    SkippingBooster,    // 跳过补充包

    // ── 牌组操作 ──
    DestroyingCard,     // 卡牌被摧毁
    CardsDestroyed,     // 卡牌被摧毁后
    PlayingCardAdded,   // 添加打牌
    SkipBlind,          // 跳过盲注

    // ── 消耗品 ──
    UsingConsumable,    // 使用消耗牌

    // ── 弃牌 ──
    Discard,            // 弃牌
    PreDiscard,         // 弃牌前
    DebuffedHand,       // 被减益手牌
}

/// <summary>
/// 牌所在区域
/// </summary>
public enum CardAreaType
{
    Play,   // G.play（打出区）
    Hand,   // G.hand（手牌区）
}
```

### 4.3 效果返回值 — JokerEffectResult

统一的效果返回结构，对应原版的返回值 table：

```csharp
/// <summary>
/// 单次小丑效果返回值，对应原版 calculate_joker 的返回 table
/// </summary>
public class JokerEffectResult
{
    /// <summary>浮动文字</summary>
    public string Message { get; set; }

    /// <summary>加法倍率</summary>
    public int MultMod { get; set; }

    /// <summary>乘法倍率（默认 1.0，即不生效）</summary>
    public float XmultMod { get; set; } = 1f;

    /// <summary>筹码</summary>
    public int ChipMod { get; set; }

    /// <summary>金钱</summary>
    public int Dollars { get; set; }

    /// <summary>文字颜色</summary>
    public string Colour { get; set; }

    /// <summary>触发此效果的小丑实例 ID</summary>
    public int JokerInstanceId { get; set; }

    /// <summary>是否为空效果（无任何修改）</summary>
    public bool IsEmpty =>
        MultMod == 0 && Mathf.Approximately(XmultMod, 1f) &&
        ChipMod == 0 && Dollars == 0;
}
```

### 4.4 运行中小丑实例 — JokerInstance

```csharp
/// <summary>
/// 运行中的小丑实例，持有 Lua 侧的 ability table 引用
/// </summary>
public class JokerInstance
{
    /// <summary>实例唯一 ID（对应卡牌实体 ID）</summary>
    public int InstanceId { get; set; }

    /// <summary>静态配置 ID（查 DRJokerConfig）</summary>
    public int ConfigId { get; set; }

    /// <summary>小丑名称（如 "j_banner"），用于 Lua 路由</summary>
    public string Name { get; set; }

    /// <summary>是否被削弱（debuff）</summary>
    public bool IsDebuffed { get; set; }

    /// <summary>
    /// Lua 侧的 ability table（由 Lua 创建和管理）
    /// 包含：mult、Xmult、extra 等运行时累计值
    /// </summary>
    public LuaTable Ability { get; set; }

    /// <summary>出售价格</summary>
    public int SellCost { get; set; }

    /// <summary>Editions（镭射/多彩/负片）</summary>
    public string Edition { get; set; }
}
```

### 4.5 核心管理器 — JokerManager

```csharp
/// <summary>
/// 小丑系统核心管理器 — 调度 C#↔Lua 的核心
/// </summary>
public class JokerManager
{
    private LuaEnv _luaEnv;
    private LuaTable _jokerRegistry;
    private List<JokerInstance> _activeJokers = new List<JokerInstance>();

    /// <summary>
    /// 统一的计分入口 — 对应原版 evaluate_play 的多次遍历
    /// 调用时机：GameController.PlayHand() 中，手牌判定完成后
    /// </summary>
    public JokerScoreResult EvaluateAll(JokerContext baseContext)
    {
        var totalResult = new JokerScoreResult();

        // 1. before 阶段
        var beforeCtx = CloneContext(baseContext);
        beforeCtx.TriggerType = JokerTriggerType.Before;
        foreach (var joker in _activeJokers)
            totalResult.Accumulate(CalculateSingleJoker(joker, beforeCtx));

        // 2. joker_main 阶段
        var mainCtx = CloneContext(baseContext);
        mainCtx.TriggerType = JokerTriggerType.JokerMain;
        foreach (var joker in _activeJokers)
            totalResult.Accumulate(CalculateSingleJoker(joker, mainCtx));

        // 3. other_joker 互相作用
        for (int i = 0; i < _activeJokers.Count; i++)
        {
            var otherCtx = CloneContext(baseContext);
            otherCtx.TriggerType = JokerTriggerType.OtherJoker;
            for (int j = 0; j < _activeJokers.Count; j++)
            {
                if (i == j) continue;
                otherCtx.OtherJoker = _activeJokers[j];
                totalResult.Accumulate(CalculateSingleJoker(_activeJokers[i], otherCtx));
            }
        }

        return totalResult;
    }

    /// <summary>
    /// 对单张小丑调用 Lua 效果函数
    /// </summary>
    private JokerEffectResult CalculateSingleJoker(JokerInstance joker, JokerContext context)
    {
        if (joker.IsDebuffed) return null;

        // Blueprint/Brainstorm 的递归复制
        if (IsCopycatJoker(joker))
            return HandleCopycatJoker(joker, context);

        // 调用 Lua: joker_registry.calculate(name, context, ability)
        var luaResult = _jokerRegistry.Invoke<LuaTable, JokerContext, LuaTable, object>(
            "calculate", joker.Name, context, joker.Ability);

        return JokerEffectResult.FromLua(luaResult, joker.InstanceId);
    }

    /// <summary>
    /// 处理 Blueprint/Brainstorm 的递归复制
    /// </summary>
    private JokerEffectResult HandleCopycatJoker(JokerInstance joker, JokerContext context)
    {
        context.BlueprintDepth++;
        if (context.BlueprintDepth > _activeJokers.Count + 1)
            return null;  // 防止无限递归

        JokerInstance targetJoker = GetCopyTarget(joker);
        if (targetJoker == null || targetJoker == joker) return null;

        // 被复制的小丑会检查 context.blueprint 来决定是否跳过不可复制的副作用
        return CalculateSingleJoker(targetJoker, context);
    }

    /// <summary>
    /// 在特定阶段触发特定小丑（用于 individual/repetition/end_of_round 等非主流程触发）
    /// </summary>
    public JokerEffectResult TriggerSingleJoker(int instanceId, JokerContext context)
    {
        var joker = _activeJokers.Find(j => j.InstanceId == instanceId);
        if (joker == null) return null;
        return CalculateSingleJoker(joker, context);
    }

    /// <summary>
    /// 触发所有 active 小丑（用于 before/joker_main 之外的一次性场景触发）
    /// </summary>
    public List<JokerEffectResult> TriggerAllJokers(JokerContext context)
    {
        var results = new List<JokerEffectResult>();
        foreach (var joker in _activeJokers)
        {
            var result = CalculateSingleJoker(joker, context);
            if (result != null) results.Add(result);
        }
        return results;
    }

    // ── 小丑实例管理 ──

    public void AddJoker(JokerInstance joker)
    {
        _activeJokers.Add(joker);
    }

    public void RemoveJoker(int instanceId)
    {
        _activeJokers.RemoveAll(j => j.InstanceId == instanceId);
    }

    public JokerInstance GetJoker(int instanceId)
    {
        return _activeJokers.Find(j => j.InstanceId == instanceId);
    }

    // ── 辅助方法 ──

    private bool IsCopycatJoker(JokerInstance joker)
    {
        return joker.Name == "j_blueprint" || joker.Name == "j_brainstorm";
    }

    private JokerInstance GetCopyTarget(JokerInstance joker)
    {
        if (joker.Name == "j_blueprint")
        {
            // Blueprint: 复制右侧小丑
            int idx = _activeJokers.IndexOf(joker);
            return idx + 1 < _activeJokers.Count ? _activeJokers[idx + 1] : null;
        }
        // Brainstorm: 复制最左侧小丑
        return _activeJokers.Count > 0 ? _activeJokers[0] : null;
    }

    private JokerContext CloneContext(JokerContext src)
    {
        // 浅拷贝即可（CardData 等引用保持不变）
        return new JokerContext
        {
            TriggerType = src.TriggerType,
            IndividualCard = src.IndividualCard,
            CardArea = src.CardArea,
            DestroyedCard = src.DestroyedCard,
            OtherJoker = src.OtherJoker,
            RepetitionRemaining = src.RepetitionRemaining,
            IsGameOver = src.IsGameOver,
            SoldCard = src.SoldCard,
            BlueprintDepth = src.BlueprintDepth,
        };
    }
}

/// <summary>
/// 小丑计分汇总结果
/// </summary>
public class JokerScoreResult
{
    public int TotalChips { get; set; }
    public int TotalMult { get; set; }
    public float TotalXmult { get; set; } = 1f;
    public int TotalDollars { get; set; }
    public List<string> Messages { get; set; } = new List<string>();

    public void Accumulate(JokerEffectResult effect)
    {
        if (effect == null) return;
        TotalChips += effect.ChipMod;
        TotalMult += effect.MultMod;
        TotalXmult *= effect.XmultMod;
        TotalDollars += effect.Dollars;
        if (!string.IsNullOrEmpty(effect.Message))
            Messages.Add(effect.Message);
    }
}
```

### 4.6 事件队列 — JokerEventManager

```csharp
/// <summary>
/// 副作用事件队列，对应原版 G.E_MANAGER:add_event
/// 保证动画和逻辑的时序正确
/// </summary>
public class JokerEventManager
{
    private Queue<Action> _eventQueue = new Queue<Action>();
    private bool _isProcessing;

    /// <summary>
    /// 推入一个副作用事件（由 Lua 通过 Bridge 调用）
    /// </summary>
    public void AddEvent(Action func)
    {
        _eventQueue.Enqueue(func);
    }

    /// <summary>
    /// 在协程中逐帧执行事件队列
    /// 调用方式：yield return JokerEventManager.ProcessEvents()
    /// </summary>
    public IEnumerator ProcessEvents()
    {
        if (_isProcessing) yield break;
        _isProcessing = true;

        while (_eventQueue.Count > 0)
        {
            var evt = _eventQueue.Dequeue();
            evt?.Invoke();
            yield return null; // 等待一帧，保证动画时序
        }

        _isProcessing = false;
    }

    /// <summary>
    /// 在 Update 中同步执行（用于不需要动画间隔的场景）
    /// </summary>
    public void ProcessEventsImmediate()
    {
        while (_eventQueue.Count > 0)
        {
            var evt = _eventQueue.Dequeue();
            evt?.Invoke();
        }
    }
}
```

### 4.7 C#→Lua 桥接 — LuaJokerBridge

这是 Lua 能调用的所有 C# API 的集合，用 `[LuaCallCSharp]` 标注：

```csharp
/// <summary>
/// 导出给 Lua 的 C# API 桥接层
/// 对应原版 Lua 中直接操作 G.E_MANAGER、G.playing_card 等的部分
/// </summary>
[LuaCallCSharp]
public static class LuaJokerBridge
{
    // ═══════════════════════════════════════════════════════════════
    // 牌组操作
    // ═══════════════════════════════════════════════════════════════

    /// <summary>向牌组添加一张牌（Hologram 等）</summary>
    public static void AddCardToDeck(int cardId, string enhancement) { }

    /// <summary>从牌组移除一张牌（Erosion 等）</summary>
    public static void RemoveCardFromDeck(int cardId) { }

    /// <summary>摧毁一张牌并播放动画（Sixth Sense 等）</summary>
    public static void DestroyCard(int cardId) { }

    /// <summary>创建一张随机牌加入牌组（Certificate 等）</summary>
    public static int CreateRandomCard(string suit, int rank, string seal) { return 0; }

    /// <summary>将打出区的人头牌变为黄金牌（Midas Mask）</summary>
    public static void TurnFaceCardsGold() { }

    /// <summary>移除计分牌中的增强效果（Vampire）</summary>
    public static void RemoveEnhancementsFromScoringCards() { }

    /// <summary>向牌组添加石头牌（Marble Joker）</summary>
    public static void AddStoneCardToDeck() { }

    /// <summary>永久增加牌的筹码值（Hiker）</summary>
    public static void AddPermanentChips(int cardId, int chips) { }

    // ═══════════════════════════════════════════════════════════════
    // 金钱操作
    // ═══════════════════════════════════════════════════════════════

    public static void AddMoney(int amount) { }
    public static int GetMoney() { return 0; }

    // ═══════════════════════════════════════════════════════════════
    // 小丑自身操作
    // ═══════════════════════════════════════════════════════════════

    /// <summary>摧毁自身（Popcorn、Turtle Bean 等自毁）</summary>
    public static void DestroySelf(int jokerInstanceId) { }

    /// <summary>摧毁其他小丑（Madness、Ceremonial Dagger）</summary>
    public static void DestroyJoker(int jokerInstanceId) { }

    /// <summary>设置小丑的倍率（Lua 修改 ability.mult 后同步）</summary>
    public static void SetJokerMult(int jokerInstanceId, int newMult) { }

    /// <summary>增加小丑售价（Egg、Gift Card）</summary>
    public static void AddSellCost(int jokerInstanceId, int amount) { }

    // ═══════════════════════════════════════════════════════════════
    // 生成物品
    // ═══════════════════════════════════════════════════════════════

    public static int CreateTarotCard() { return 0; }
    public static int CreatePlanetCard(string pokerHandType) { return 0; }
    public static int CreateSpectralCard() { return 0; }
    public static int CreateRandomJoker(int rarityFilter) { return 0; }

    // ═══════════════════════════════════════════════════════════════
    // 手牌 / 弃牌
    // ═══════════════════════════════════════════════════════════════

    public static int GetHandSize() { return 0; }
    public static int GetDiscardCount() { return 0; }
    public static void SetDiscardCount(int count) { }
    public static void SetHandSize(int delta) { }

    // ═══════════════════════════════════════════════════════════════
    // Boss
    // ═══════════════════════════════════════════════════════════════

    public static void DisableBossEffect() { }

    // ═══════════════════════════════════════════════════════════════
    // UI（轻量调用，复杂 UI 由 C# 侧事件驱动）
    // ═══════════════════════════════════════════════════════════════

    public static void ShowFloatingText(int jokerInstanceId, string text, string colorHex) { }

    public static void ShowJokerAnimation(int jokerInstanceId, string animName) { }

    // ═══════════════════════════════════════════════════════════════
    // 事件队列
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 推入一个事件到队列（Lua 侧调用此方法来执行副作用）
    /// 对应原版 G.E_MANAGER:add_event(Event({func = function() ... end}))
    /// </summary>
    public static void EnqueueEvent(Action action) { }

    // ═══════════════════════════════════════════════════════════════
    // 查询
    // ═══════════════════════════════════════════════════════════════

    public static int GetJokerCount() { return 0; }
    public static int GetPlayingCardCount() { return 0; }
    public static int GetDeckSize() { return 0; }
    public static bool IsCardEnhanced(int cardId) { return false; }
    public static int GetCardRank(int cardId) { return 0; }
    public static string GetCardSuit(int cardId) { return ""; }
    public static int GetHandTypePlayCount(string handType) { return 0; }
    public static string GetCurrentPokerHandType() { return ""; }
    public static bool IsLastHand() { return false; }
    public static int GetRemainingDiscards() { return 0; }
    public static void UpgradePokerHand(string handType) { }
}
```

---

## 五、Lua 侧详细设计

### 5.1 初始化入口 — joker_init.lua

```lua
-- joker_init.lua
-- Lua 侧初始化入口，由 C# XLuaBootstrap 调用

-- 导入 C# 桥接
local Bridge = CS.Yuu.LuaJokerBridge

-- 加载注册表
local JokerRegistry = require("joker_registry")

-- 加载所有小丑效果（每个文件自注册到 JokerRegistry）
require("jokers.j_banner")
require("jokers.j_ramen")
require("jokers.j_blueprint")
require("jokers.j_brainstorm")
require("jokers.j_photograph")
require("jokers.j_hanging_chad")
require("jokers.j_green_joker")
require("jokers.j_ceremonial_dagger")
-- ... 更多小丑

-- 暴露给 C# 的入口函数
return {
    registry = JokerRegistry,
    Bridge = Bridge,
}
```

### 5.2 注册表 — joker_registry.lua

```lua
-- joker_registry.lua
-- 小丑效果注册表和路由

local JokerRegistry = {}

--- 注册一个小丑效果
--- @param name string 小丑名称，如 "j_banner"
--- @param handler function 效果函数: function(context, ability) -> table|nil
function JokerRegistry.register(name, handler)
    JokerRegistry[name] = handler
end

--- 调用指定小丑的效果
--- @param name string 小丑名称
--- @param context table JokerContext（C# 对象）
--- @param ability table ability table（Lua table）
--- @return table|nil 效果返回值
function JokerRegistry.calculate(name, context, ability)
    local handler = JokerRegistry[name]
    if handler then
        -- xpcall 保护，防止某个小丑的 Lua 错误导致整个计分崩溃
        local ok, result = xpcall(handler, debug.traceback, context, ability)
        if not ok then
            CS.UnityEngine.Debug.LogError("[JokerRegistry] Error in " .. name .. ": " .. tostring(result))
            return nil
        end
        return result
    end
    return nil
end

return JokerRegistry
```

### 5.3 辅助函数 — context_helper.lua

```lua
-- util/context_helper.lua
-- Context 判断辅助函数

local M = {}

-- 触发阶段常量（与 C# JokerTriggerType 枚举对应）
M.TRIGGER = {
    Before          = 0,
    JokerMain       = 1,
    After           = 2,
    Individual      = 3,
    Repetition      = 4,
    OtherJoker      = 5,
    EndOfRound      = 6,
    SettingBlind    = 7,
    FirstHandDrawn  = 8,
    SellingSelf     = 9,
    SellingCard     = 10,
    RerollShop      = 11,
    EndingShop      = 12,
    OpenBooster     = 13,
    SkippingBooster = 14,
    DestroyingCard  = 15,
    CardsDestroyed  = 16,
    PlayingCardAdded= 17,
    SkipBlind       = 18,
    UsingConsumable = 19,
    Discard         = 20,
    PreDiscard      = 21,
    DebuffedHand    = 22,
}

--- 快速创建一个效果返回值 table
--- @param opts table {message, mult_mod, Xmult_mod, chip_mod, dollars, colour}
function M.effect(opts)
    return {
        message    = opts.message or "",
        mult_mod   = opts.mult_mod or 0,
        Xmult_mod  = opts.Xmult_mod or 1,
        chip_mod   = opts.chip_mod or 0,
        dollars    = opts.dollars or 0,
        colour     = opts.colour or "MULT",
    }
end

--- 创建颜色常量
M.COLOUR = {
    RED   = "RED",
    MULT  = "MULT",
    CHIPS = "CHIPS",
    MONEY = "MONEY",
}

return M
```

### 5.4 小丑效果模板

每个小丑文件遵循统一模板：

```lua
-- jokers/j_xxx.lua
-- 小丑名称: XXX
-- 效果描述: ...

local Registry = require("joker_registry")
local Helper  = require("util.context_helper")
local Bridge  = CS.Yuu.LuaJokerBridge

local TRIGGER = Helper.TRIGGER
local COLOUR  = Helper.COLOUR

-- 效果处理函数
local function handler(context, ability)
    local trigger = context.TriggerType
    local result = nil

    -- 防御性检查（debuff 已在 C# 侧判断，这里再做一层保护）
    -- 注意：Blueprint 复制时需要跳过不可复制的副作用
    if context.BlueprintDepth and context.BlueprintDepth > 0 then
        -- 在 Blueprint 复制中，跳过某些副作用
    end

    -- ── before 阶段 ──
    if trigger == TRIGGER.Before then
        -- 示例：打出顺子时增加筹码（Runner）
        result = Helper.effect({
            message = "+Chips!",
            chip_mod = ability.extra.chips or 30,
            colour = COLOUR.CHIPS,
        })
    end

    -- ── joker_main 阶段 ──
    if trigger == TRIGGER.JokerMain then
        result = Helper.effect({
            message = "+" .. ability.mult .. " Mult",
            mult_mod = ability.mult,
            colour = COLOUR.MULT,
        })
    end

    -- ── individual 阶段 ──
    if trigger == TRIGGER.Individual then
        -- ...
    end

    -- ── end_of_round 阶段 ──
    if trigger == TRIGGER.EndOfRound then
        -- ...
    end

    return result
end

-- 自注册
Registry.register("j_xxx", handler)

return handler
```

### 5.5 具体小丑实现示例

#### j_banner.lua — 基础加法小丑

```lua
-- jokers/j_banner.lua
-- Banner: 每剩余弃牌次数给予 +30 筹码
-- 配置: extra=30

local Registry = require("joker_registry")
local Helper  = require("util.context_helper")
local Bridge  = CS.Yuu.LuaJokerBridge

local function handler(context, ability)
    local trigger = context.TriggerType

    if trigger == Helper.TRIGGER.JokerMain then
        local remaining = Bridge.GetRemainingDiscards()
        local chips = remaining * (ability.extra.chips_per_discard or 30)
        if chips > 0 then
            return Helper.effect({
                message = "+" .. chips .. " Chips",
                chip_mod = chips,
                colour = Helper.COLOUR.CHIPS,
            })
        end
    end

    return nil
end

Registry.register("j_banner", handler)
return handler
```

#### j_green_joker.lua — 有状态累积小丑

```lua
-- jokers/j_green_joker.lua
-- Green Joker: 每次出牌 +1 倍率，每次弃牌 -1 倍率
-- 配置: extra.hand_add=1, extra.discard_sub=1（初始 mult=0）

local Registry = require("joker_registry")
local Helper  = require("util.context_helper")

local function handler(context, ability)
    local trigger = context.TriggerType

    -- before 阶段：每次出牌增加倍率
    if trigger == Helper.TRIGGER.Before then
        ability.mult = (ability.mult or 0) + (ability.extra.hand_add or 1)
        return Helper.effect({
            message = "+" .. (ability.extra.hand_add or 1) .. " Mult",
            colour = Helper.COLOUR.MULT,
        })
    end

    -- discard 阶段：弃牌减少倍率
    if trigger == Helper.TRIGGER.Discard then
        ability.mult = math.max(0, (ability.mult or 0) - (ability.extra.discard_sub or 1))
        return Helper.effect({
            message = "-" .. (ability.extra.discard_sub or 1) .. " Mult",
            colour = Helper.COLOUR.RED,
        })
    end

    -- joker_main：返回当前累积倍率
    if trigger == Helper.TRIGGER.JokerMain then
        if ability.mult > 0 then
            return Helper.effect({
                message = "+" .. ability.mult .. " Mult",
                mult_mod = ability.mult,
                colour = Helper.COLOUR.MULT,
            })
        end
    end

    return nil
end

Registry.register("j_green_joker", handler)
return handler
```

#### j_ceremonial_dagger.lua — 有副作用的小丑

```lua
-- jokers/j_ceremonial_dagger.lua
-- Ceremonial Dagger: 选择盲注时摧毁右侧小丑，获得其售价×2 的倍率
-- 注意：Blueprint 复制时不执行摧毁逻辑

local Registry = require("joker_registry")
local Helper  = require("util.context_helper")
local Bridge  = CS.Yuu.LuaJokerBridge

local function handler(context, ability)
    local trigger = context.TriggerType

    -- setting_blind 阶段：摧毁右侧小丑
    if trigger == Helper.TRIGGER.SettingBlind then
        -- Blueprint 复制时不执行副作用
        if not (context.BlueprintDepth and context.BlueprintDepth > 0) then
            -- 事件队列化：保证动画时序
            Bridge.EnqueueEvent(function()
                local rightJoker = Bridge.GetRightJoker(ability.instance_id)
                if rightJoker then
                    local sellCost = Bridge.GetJokerSellCost(rightJoker)
                    ability.mult = (ability.mult or 0) + sellCost * 2
                    Bridge.DestroyJoker(rightJoker)
                end
            end)
        end
    end

    -- joker_main：返回累积倍率
    if trigger == Helper.TRIGGER.JokerMain then
        if (ability.mult or 0) > 0 then
            return Helper.effect({
                message = "+" .. ability.mult .. " Mult",
                mult_mod = ability.mult,
                colour = Helper.COLOUR.MULT,
            })
        end
    end

    return nil
end

Registry.register("j_ceremonial_dagger", handler)
return handler
```

---

## 六、XLua 热更新工作流

### 6.1 自定义 Loader

```csharp
// XLuaBootstrap.cs
public class XLuaBootstrap : MonoBehaviour
{
    public static LuaEnv LuaEnv { get; private set; }

    void Awake()
    {
        LuaEnv = new LuaEnv();

        // 自定义 Loader：优先加载热更目录
        LuaEnv.AddLoader((ref string filepath) =>
        {
            filepath = filepath.Replace(".", "/");

            // 1. 优先从持久化热更目录加载
            var hotfixPath = Path.Combine(Application.persistentDataPath, "Hotfix", filepath + ".lua");
            if (File.Exists(hotfixPath))
            {
                Debug.Log($"[XLua] Hotfix loaded: {filepath}");
                return File.ReadAllText(hotfixPath);
            }

            // 2. 回退到 Resources/LuaScripts/
            var asset = Resources.Load<TextAsset>("LuaScripts/" + filepath);
            if (asset != null)
                return asset.text;

            return null;  // 找不到文件
        });

        // 加载 Lua 初始化脚本
        LuaEnv.DoString(@"require('joker_init')");
    }

    void OnDestroy()
    {
        LuaEnv?.Dispose();
        LuaEnv = null;
    }
}
```

### 6.2 热更新流程

```
开发阶段:
  修改 .lua 脚本 → 直接运行（XLua 从 Resources 加载）

发布后热更:
  1. 服务器下发 .lua 文件 → Application.persistentDataPath/Hotfix/
  2. LuaEnv.AddLoader() 优先加载热更目录
  3. 触发 JokerManager.ReloadJokers() 重新 require
  4. Lua 侧的 ability 状态可以在重载前序列化保存，重载后恢复

重载流程:
  void ReloadJokers()
  {
      // 1. 保存所有小丑的运行时状态
      SerializeAllAbilities();
      // 2. 清理 Lua package.loaded 缓存
      _luaEnv.DoString(@"
          package.loaded['joker_registry'] = nil
          package.loaded['jokers.j_banner'] = nil
          -- ...
      ");
      // 3. 重新加载
      _luaEnv.DoString(@"require('joker_init')");
      // 4. 恢复运行时状态
      RestoreAllAbilities();
  }
```

---

## 七、整合到现有计分流程

### 7.1 修改 GameController.PlayHand() 流程

基于现有的计分流程（已在 [GameController.cs](Assets/GameMain/Scripts/Game/Controller/GameController.cs) 中实现手牌判定），新增小丑触发：

```csharp
// GameController.cs — PlayHand 协程
public IEnumerator PlayHandCoroutine(List<CardData> selectedCards)
{
    // ═══ 步骤 1: 手牌判定（已有） ═══
    var evalResult = PokerHandEvaluator.Evaluate(selectedCards);

    // ═══ 步骤 2: 小丑 before 阶段 ═══
    var jokerScore = _jokerManager.EvaluateAll(new JokerContext
    {
        TriggerType = JokerTriggerType.Before,
    });
    yield return _jokerEventManager.ProcessEvents();

    // ═══ 步骤 3: 逐张计分 ═══
    foreach (var card in evalResult.ScoringHand)
    {
        // C# 基础计分：chips += card.BaseChips

        // 小丑 individual 触发（打出区 G.play）
        var individualCtx = new JokerContext
        {
            TriggerType = JokerTriggerType.Individual,
            IndividualCard = card,
            CardArea = CardAreaType.Play,
        };
        jokerScore.AccumulateAll(_jokerManager.TriggerAllJokers(individualCtx));

        // 小丑 repetition 触发
        jokerScore.AccumulateAll(HandleRepetition(card));

        yield return null;
    }

    // 手牌区 individual 触发（G.hand）
    foreach (var card in _handCards)
    {
        var handCtx = new JokerContext
        {
            TriggerType = JokerTriggerType.Individual,
            IndividualCard = card,
            CardArea = CardAreaType.Hand,
        };
        jokerScore.AccumulateAll(_jokerManager.TriggerAllJokers(handCtx));
    }

    // ═══ 步骤 4: joker_main 阶段 ═══
    var mainResult = _jokerManager.EvaluateAll(new JokerContext
    {
        TriggerType = JokerTriggerType.JokerMain,
    });
    jokerScore.Accumulate(mainResult);
    yield return _jokerEventManager.ProcessEvents();

    // ═══ 步骤 5: 计算最终得分 ═══
    // 公式：总得分 = (手牌筹码 + 小丑筹码) × (手牌倍率 + 小丑倍率) × 小丑X倍率
    int totalChips = evalResult.Add + jokerScore.TotalChips;
    int totalMult = evalResult.Mul + jokerScore.TotalMult;
    float finalScore = totalChips * totalMult * jokerScore.TotalXmult;

    // ═══ 步骤 6: 更新 UI ═══
    _uiGameInfoForm.ShowResult(evalResult.HandType, totalChips, totalMult, finalScore);
    foreach (var msg in jokerScore.Messages)
        _uiJokerForm.ShowFloatingText(msg);
}
```

### 7.2 Repetition 处理封装

```csharp
/// <summary>
/// 处理重复计分（Hanging Chad、Sock and Buskin 等）
/// </summary>
private JokerScoreResult HandleRepetition(CardData card)
{
    var result = new JokerScoreResult();

    var repContext = new JokerContext
    {
        TriggerType = JokerTriggerType.Repetition,
        IndividualCard = card,
    };

    // 第一轮：计算所有 repetition 小丑的总重复次数
    foreach (var joker in _jokerManager.ActiveJokers)
    {
        var repResult = _jokerManager.TriggerSingleJoker(joker.InstanceId, repContext);
        if (repResult != null && repResult.RepetitionRemaining > 0)
        {
            // n 次额外计分
            for (int i = 0; i < repResult.RepetitionRemaining; i++)
            {
                var indCtx = new JokerContext
                {
                    TriggerType = JokerTriggerType.Individual,
                    IndividualCard = card,
                    CardArea = CardAreaType.Play,
                };
                result.AccumulateAll(_jokerManager.TriggerAllJokers(indCtx));
            }
        }
    }

    return result;
}
```

---

## 八、计分流程总览

```
evaluate_play()                           ← GameController.PlayHand()
  │
  ├─ [before] 遍历 G.jokers.cards
  │    └─ JokerManager.TriggerAllJokers({before=true})
  │
  ├─ [per scoring card] 遍历 scoring_hand
  │    ├─ Repetition 检查
  │    │    └─ TriggerSingleJoker(id, {repetition=true})
  │    │         → 重复 number 次 individual
  │    └─ Individual 效果
  │         └─ TriggerAllJokers({individual=true, cardarea=G.play})
  │
  ├─ [hand cards] 遍历 G.hand.cards
  │    └─ TriggerAllJokers({individual=true, cardarea=G.hand})
  │
  ├─ [joker_main] 遍历 G.jokers.cards
  │    └─ JokerManager.EvaluateAll({joker_main=true})
  │         ├─ 逐个 calculate_joker
  │         └─ 小丑互相作用 → {other_joker=_card}
  │
  └─ [destroying] 遍历 scoring_hand
       └─ TriggerAllJokers({destroying_card=card})
```

---

## 九、性能考量

| 考量 | 方案 |
|---|---|
| **跨语言调用开销** | 避免每次 `calculate_joker` 做大量 C#↔Lua 往返。一次调用入参（context + ability），一次返回（result table），中间逻辑全在 Lua 侧 |
| **Lua Table 序列化** | 不序列化 ability table，直接持有 `LuaTable` 引用。XLua 支持 C# 直接引用 Lua table |
| **对象池** | `JokerContext` 和 `JokerEffectResult` 使用 GF 的 ReferencePool 池化 |
| **分帧** | 事件队列逐帧执行（`yield return null`），避免单帧卡顿 |
| **错误隔离** | 每个小丑的 `calculate_joker` 用 `xpcall` 包裹，单个小丑报错不影响整体计分 |

---

## 十、实施路线图

| 阶段 | 内容 | 产出物 |
|------|------|--------|
| **Phase 1** | 集成 XLua 插件，创建 `XLuaBootstrap`，验证 Lua 环境可用 | `XLuaBootstrap.cs`、Lua 环境跑通 |
| **Phase 2** | 定义核心类型：`JokerContext`、`JokerEffectResult`、`JokerTriggerType`、`JokerInstance` | 4 个 C# 类型文件 |
| **Phase 3** | 实现 `LuaJokerBridge` 桥接层（最少 API 集，支撑 5 个示范小丑） | `LuaJokerBridge.cs` |
| **Phase 4** | 实现 `JokerManager` + `JokerEventManager` + 事件队列 | 核心管理器 |
| **Phase 5** | Lua 侧：`joker_registry.lua`、`context_helper.lua`、首批 5 个典型小丑 | Lua 脚本框架 + 5 个小丑 |
| **Phase 6** | 整合到 `GameController` 计分流程，对接 `UIJokerForm` 和浮动文字 | 完整可玩的计分流程 |
| **Phase 7** | 批量实现剩余小丑（150+），覆盖所有 TriggerType | 完整小丑库 |
| **Phase 8** | 热更新流程验证：服务器下发改动的小丑 Lua，重载后效果生效 | 热更链路跑通 |

---

> **文档版本**: v1.0 | **最后更新**: 2026-06-27 | **作者**: AI Assisted Design
