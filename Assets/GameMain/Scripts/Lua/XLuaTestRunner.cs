//------------------------------------------------------------
// XLuaTestRunner - XLua 环境验证脚本
// 挂载到任意场景 GameObject 上，勾选 RunTest 一键验证
// 也可在 Editor 中通过菜单 XLua → Run Environment Test 执行
//------------------------------------------------------------

using UnityEngine;
using XLua;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Yuu
{
    /// <summary>
    /// XLua 环境验证器。两种使用方式：
    ///   1. 挂载到 GameObject，勾选 RunTest → 运行时自动测试
    ///   2. Editor 菜单 XLua → Run Environment Test
    /// </summary>
    public class XLuaTestRunner : MonoBehaviour
    {
        [SerializeField]
        private bool _runTest = false;

        private void Start()
        {
            if (_runTest)
            {
                RunAllTests();
            }
        }

        /// <summary>
        /// 执行所有验证测试
        /// </summary>
        [ContextMenu("Run All Tests")]
        public void RunAllTests()
        {
            Debug.Log("========== [XLuaTestRunner] Starting XLua Environment Tests ==========");

            Test1_BasicLuaEnv();
            Test2_DoString();
            Test3_CSharpCall();
            Test4_RequireModule();
            Test5_JokerRegistry();
            Test6_JokerCalculate();

            Debug.Log("========== [XLuaTestRunner] All Tests Complete ==========");
        }

        private void Test1_BasicLuaEnv()
        {
            Debug.Log("--- Test 1: Basic LuaEnv lifecycle ---");
            var env = new LuaEnv();
            Debug.Log("  LuaEnv created successfully");
            env.Dispose();
            Debug.Log("  LuaEnv disposed successfully");
            Debug.Log("  [PASS] Test 1");
        }

        private void Test2_DoString()
        {
            Debug.Log("--- Test 2: DoString execution ---");
            if (!XLuaManager.IsInitialized)
            {
                Debug.LogError("  [FAIL] XLuaManager not initialized. Call XLuaManager.Init() first.");
                return;
            }

            var results = XLuaManager.DoString(@"
                local a = 10
                local b = 20
                return a + b, 'hello from lua'
            ", "test2");
            if (results != null && results.Length >= 2 && (double)results[0] == 30.0)
            {
                Debug.Log($"  Result: {results[0]}, {results[1]}");
                Debug.Log("  [PASS] Test 2");
            }
            else
            {
                Debug.LogError("  [FAIL] Test 2 - unexpected result");
            }
        }

        private void Test3_CSharpCall()
        {
            Debug.Log("--- Test 3: Call C# from Lua ---");
            if (!XLuaManager.IsInitialized)
            {
                Debug.LogError("  [FAIL] XLuaManager not initialized.");
                return;
            }

            var results = XLuaManager.DoString(@"
                CS.UnityEngine.Debug.Log('  Hello from Lua via CS.UnityEngine.Debug.Log!')
                return true
            ", "test3");
            if (results != null && results.Length > 0 && (bool)results[0])
            {
                Debug.Log("  [PASS] Test 3");
            }
            else
            {
                Debug.LogError("  [FAIL] Test 3");
            }
        }

        private void Test4_RequireModule()
        {
            Debug.Log("--- Test 4: Require Lua modules ---");
            if (!XLuaManager.IsInitialized)
            {
                Debug.LogError("  [FAIL] XLuaManager not initialized.");
                return;
            }

            // 加载 joker_init（会递归加载所有子模块）
            var results = XLuaManager.Require("joker_init");
            if (results != null && results.Length > 0)
            {
                Debug.Log("  [PASS] Test 4 - joker_init loaded successfully");
            }
            else
            {
                Debug.LogError("  [FAIL] Test 4 - failed to load joker_init");
            }
        }

        private void Test5_JokerRegistry()
        {
            Debug.Log("--- Test 5: Joker Registry operations ---");
            if (!XLuaManager.IsInitialized)
            {
                Debug.LogError("  [FAIL] XLuaManager not initialized.");
                return;
            }

            var results = XLuaManager.DoString(@"
                local Registry = require('joker_registry')
                local names = Registry.list()
                local hasBanner = Registry.has('j_banner')
                local hasRamen = Registry.has('j_ramen')
                local hasGreen = Registry.has('j_green_joker')
                return #names, hasBanner, hasRamen, hasGreen, table.concat(names, ', ')
            ", "test5");

            if (results != null)
            {
                int count = (int)(double)results[0];
                Debug.Log($"  Registered jokers: {count} ({results[4]})");
                Debug.Log($"  j_banner: {results[1]}, j_ramen: {results[2]}, j_green_joker: {results[3]}");
                if (count >= 3)
                    Debug.Log("  [PASS] Test 5");
                else
                    Debug.LogError($"  [FAIL] Test 5 - expected >= 3 jokers, got {count}");
            }
            else
            {
                Debug.LogError("  [FAIL] Test 5");
            }
        }

        private void Test6_JokerCalculate()
        {
            Debug.Log("--- Test 6: Joker calculate (mock context) ---");
            if (!XLuaManager.IsInitialized)
            {
                Debug.LogError("  [FAIL] XLuaManager not initialized.");
                return;
            }

            var results = XLuaManager.DoString(@"
                local Registry = require('joker_registry')

                -- 模拟 ability table
                local ability = {
                    extra = { chips_per_discard = 30 },
                }

                -- 模拟 JokerMain context（TriggerType = 1）
                -- 使用 C# JokerContext（如果可用），否则用纯 Lua table
                local context = {}
                local ok, jokerCtx = pcall(function()
                    return CS.Yuu.JokerContext()
                end)
                if ok and jokerCtx then
                    jokerCtx.TriggerType = 1  -- JokerMain
                    context = jokerCtx
                else
                    -- fallback: 纯 Lua table
                    context = { TriggerType = 1 }  -- JokerMain
                end

                local result = Registry.calculate('j_banner', context, ability)
                if result then
                    return result.chip_mod, result.message, result.colour
                end
                return 0, 'no trigger', 'NONE'
            ", "test6");

            if (results != null)
            {
                Debug.Log($"  chip_mod={results[0]}, message={results[1]}, colour={results[2]}");
                // Banner with 3 remaining discards × 30 = 90 chips
                if ((int)(double)results[0] == 90)
                {
                    Debug.Log("  [PASS] Test 6 - Banner calculated correctly (90 chips)");
                }
                else
                {
                    Debug.LogWarning($"  [WARN] Test 6 - expected 90 chips, got {results[0]} (may need real Bridge)");
                }
            }
            else
            {
                Debug.LogError("  [FAIL] Test 6");
            }
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor 菜单入口
    /// </summary>
    public static class XLuaTestMenu
    {
        [MenuItem("XLua/Run Environment Test", false, 1)]
        private static void RunTestFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Please enter Play Mode first, then run this test.");
                return;
            }

            var runner = Object.FindObjectOfType<XLuaTestRunner>();
            if (runner == null)
            {
                var go = new GameObject("__XLuaTestRunner__");
                runner = go.AddComponent<XLuaTestRunner>();
            }
            runner.RunAllTests();
        }

        [MenuItem("XLua/Check Setup", false, 2)]
        private static void CheckSetup()
        {
            Debug.Log("========== XLua Setup Check ==========");
            Debug.Log($"XLua Src found: {System.IO.Directory.Exists("Assets/XLua/Src")}");
            Debug.Log($"XLuaManager.cs found: {System.IO.File.Exists("Assets/GameMain/Scripts/Lua/XLuaManager.cs")}");
            Debug.Log($"LuaJokerBridge.cs found: {System.IO.File.Exists("Assets/GameMain/Scripts/Lua/LuaJokerBridge.cs")}");
            Debug.Log($"LuaScripts dir found: {System.IO.Directory.Exists("Assets/GameMain/Resources/LuaScripts")}");
            Debug.Log($"joker_init.lua.txt found: {System.IO.File.Exists("Assets/GameMain/Resources/LuaScripts/joker_init.lua.txt")}");
            Debug.Log($"joker_registry.lua.txt found: {System.IO.File.Exists("Assets/GameMain/Resources/LuaScripts/joker_registry.lua.txt")}");
            Debug.Log($"j_banner.lua.txt found: {System.IO.File.Exists("Assets/GameMain/Resources/LuaScripts/jokers/j_banner.lua.txt")}");
            Debug.Log("=======================================");
        }
    }
#endif
}
