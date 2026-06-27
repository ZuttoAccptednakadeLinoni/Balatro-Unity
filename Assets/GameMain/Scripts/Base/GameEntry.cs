using UnityEngine;

namespace Yuu
{
    /// <summary>
    /// 游戏入口。
    /// </summary>
    public partial class GameEntry : MonoBehaviour
    {
        private void Start()
        {
            InitBuiltinComponents();
            InitCustomComponents();
            InitLuaEnvironment();
        }

        private void OnDestroy()
        {
            ShutdownLuaEnvironment();
        }

        private void InitLuaEnvironment()
        {
            // Step 1: 初始化 LuaEnv（加载 native DLL）
            if (!XLuaManager.Init())
            {
                Debug.LogError("[GameEntry] XLuaManager.Init() failed. Lua features disabled.");
                return;
            }

            // Step 2: 基础烟雾测试（不依赖任何外部 Lua 文件）
            var smokeTest = XLuaManager.DoString(
                "return 'XLua OK', 42",
                "smoke_test"
            );
            if (smokeTest != null && smokeTest.Length >= 2)
            {
                Debug.Log($"[GameEntry] XLua smoke test passed: {smokeTest[0]}, {smokeTest[1]}");
            }
            else
            {
                Debug.LogError("[GameEntry] XLua smoke test FAILED.");
                return;
            }

            // Step 3: 加载小丑模块（依赖 Resources/LuaScripts/ 下的 .lua.txt 文件）
            var initResult = XLuaManager.Require("joker_init");
            if (initResult != null)
            {
                Debug.Log("[GameEntry] joker_init loaded successfully. Lua environment ready!");
            }
            else
            {
                Debug.LogWarning("[GameEntry] joker_init load failed. " +
                                 "Check that Resources/LuaScripts/joker_init.lua.txt exists and is imported as TextAsset.");
            }
        }

        private void ShutdownLuaEnvironment()
        {
            XLuaManager.Dispose();
        }
    }
}
