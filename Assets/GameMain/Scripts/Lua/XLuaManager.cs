//------------------------------------------------------------
// XLuaManager - Lua 环境管理器
// 封装 XLua.LuaEnv 的生命周期，提供 DoString/Require 等便捷方法
//------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;
using XLua;

namespace Yuu
{
    /// <summary>
    /// XLua 环境管理器（静态类，由 GameEntry 驱动生命周期）。
    /// 使用方式：
    ///   初始化：GameEntry.Start() → XLuaManager.Init()
    ///   销毁：  GameEntry.OnDestroy() → XLuaManager.Dispose()
    /// </summary>
    public static class XLuaManager
    {
        /// <summary>
        /// 全局 LuaEnv 实例
        /// </summary>
        public static LuaEnv Env { get; private set; }

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// Lua 脚本加载根目录（Resources 下）
        /// </summary>
        private const string LUA_SCRIPT_ROOT = "LuaScripts";

        /// <summary>
        /// 初始化 Lua 环境。
        /// 建议在 GameEntry.Start() 中调用，确保在其他组件使用 Lua 之前完成初始化。
        /// </summary>
        /// <returns>是否初始化成功</returns>
        public static bool Init()
        {
            if (IsInitialized)
            {
                Debug.LogWarning("[XLuaManager] Already initialized. Skipping.");
                return true;
            }

            Debug.Log("[XLuaManager] Initializing LuaEnv...");

            try
            {
                Env = new LuaEnv();
            }
            catch (DllNotFoundException e)
            {
                Debug.LogError($"[XLuaManager] Native DLL 'xlua' not found.\n" +
                               "Make sure XLua plugin is correctly imported for this platform.\n" +
                               $"Error: {e.Message}");
                return false;
            }
            catch (InvalidProgramException e)
            {
                Debug.LogError($"[XLuaManager] XLua library version mismatch.\n" +
                               "The native DLL version doesn't match the C# wrapper.\n" +
                               "Try re-importing the XLua plugin.\n" +
                               $"Error: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[XLuaManager] Failed to create LuaEnv.\nError: {e}");
                return false;
            }

            // 注册自定义 Loader：优先检查热更目录，再回退到 Resources
            Env.AddLoader(CustomLoader);

            IsInitialized = true;
            Debug.Log("[XLuaManager] LuaEnv initialized successfully.");
            return true;
        }

        /// <summary>
        /// 执行 Lua 代码片段。
        /// </summary>
        /// <param name="chunk">Lua 代码字符串</param>
        /// <param name="chunkName">代码标识名（用于错误提示）</param>
        /// <returns>执行返回的值数组，失败返回 null</returns>
        public static object[] DoString(string chunk, string chunkName = "chunk")
        {
            if (!IsInitialized)
            {
                Debug.LogError("[XLuaManager] Not initialized. Call Init() first.");
                return null;
            }

            if (Env == null)
            {
                Debug.LogError("[XLuaManager] LuaEnv is null (already disposed?).");
                return null;
            }

            try
            {
                return Env.DoString(chunk, chunkName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[XLuaManager] Lua error in '{chunkName}':\n{e}");
                return null;
            }
        }

        /// <summary>
        /// 通过 require 加载 Lua 模块。
        /// 路径相对于 Resources/LuaScripts/，不需要带 .lua 后缀。
        /// 例如：Require("joker_registry") 会加载 LuaScripts/joker_registry.lua.txt
        /// </summary>
        /// <param name="modulePath">模块路径（用 . 分隔，如 "util.context_helper"）</param>
        /// <returns>模块返回值</returns>
        public static object[] Require(string modulePath)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[XLuaManager] Not initialized. Call Init() first.");
                return null;
            }

            if (Env == null)
            {
                Debug.LogError("[XLuaManager] LuaEnv is null.");
                return null;
            }

            try
            {
                // 使用 pcall 包裹 require，防止模块加载失败导致整个流程崩溃
                string luaCode = $@"
                    local ok, result = pcall(require, '{modulePath}')
                    if not ok then
                        CS.UnityEngine.Debug.LogError('[XLuaManager] require failed: {modulePath} — ' .. tostring(result))
                        return nil
                    end
                    return result
                ";
                var results = Env.DoString(luaCode, $"require:{modulePath}");
                return results;
            }
            catch (Exception e)
            {
                Debug.LogError($"[XLuaManager] Failed to require '{modulePath}':\n{e}");
                return null;
            }
        }

        /// <summary>
        /// 热重载指定模块（清除 package.loaded 缓存后重新 require）。
        /// </summary>
        /// <param name="modulePath">模块路径</param>
        public static void Reload(string modulePath)
        {
            if (!IsInitialized || Env == null) return;

            try
            {
                Env.DoString($"package.loaded['{modulePath}'] = nil");
                Require(modulePath);
                Debug.Log($"[XLuaManager] Reloaded: {modulePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[XLuaManager] Reload failed for '{modulePath}':\n{e}");
            }
        }

        /// <summary>
        /// 销毁 Lua 环境，释放原生资源。
        /// </summary>
        public static void Dispose()
        {
            if (!IsInitialized) return;

            Debug.Log("[XLuaManager] Disposing LuaEnv...");
            IsInitialized = false;

            if (Env != null)
            {
                try
                {
                    Env.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[XLuaManager] Error during disposal:\n{e}");
                }
                finally
                {
                    Env = null;
                }
            }

            Debug.Log("[XLuaManager] LuaEnv disposed.");
        }

        /// <summary>
        /// 自定义 Lua 文件加载器。
        /// 优先级：
        ///   1. 持久化热更目录（Application.persistentDataPath/Hotfix/LuaScripts/）
        ///   2. Resources/LuaScripts/（内置资源）
        /// </summary>
        private static byte[] CustomLoader(ref string filepath)
        {
            // filepath 格式如 "joker_registry" 或 "util.context_helper"
            // Lua require 用 . 分隔，需要转换为 / 路径
            string relativePath = filepath.Replace('.', '/');
            string luaContent = null;

            // 1. 尝试从持久化热更目录加载
            try
            {
                string hotfixDir = Path.Combine(Application.persistentDataPath, "Hotfix", LUA_SCRIPT_ROOT);
                string hotfixPathTxt = hotfixDir + "/" + relativePath + ".lua.txt";
                string hotfixPathLua = hotfixDir + "/" + relativePath + ".lua";

                if (File.Exists(hotfixPathTxt))
                {
                    luaContent = File.ReadAllText(hotfixPathTxt);
                    Debug.Log($"[XLuaManager] Hotfix loaded: {relativePath}.lua.txt");
                }
                else if (File.Exists(hotfixPathLua))
                {
                    luaContent = File.ReadAllText(hotfixPathLua);
                    Debug.Log($"[XLuaManager] Hotfix loaded: {relativePath}.lua");
                }
            }
            catch (Exception)
            {
                // persistentDataPath 访问失败时静默跳过（如 WebGL 等平台）
            }

            // 2. 回退到 Resources
            if (luaContent == null)
            {
                // 尝试多种 resource path 格式：
                // - "LuaScripts/joker_init.lua"    (xxx.lua.txt → Unity 识别为 xxx.lua)
                // - "LuaScripts/joker_init"         (fallback, 直接以文件名加载)
                // - "LuaScripts/jokers/j_banner.lua"
                // - "LuaScripts/jokers/j_banner"
                string[] candidates = {
                    LUA_SCRIPT_ROOT + "/" + relativePath + ".lua",
                    LUA_SCRIPT_ROOT + "/" + relativePath,
                };

                foreach (var candidate in candidates)
                {
                    var asset = Resources.Load<TextAsset>(candidate);
                    if (asset != null)
                    {
                        luaContent = asset.text;
                        break;
                    }
                }
            }

            if (luaContent != null)
            {
                return System.Text.Encoding.UTF8.GetBytes(luaContent);
            }

            Debug.LogWarning($"[XLuaManager] Lua file not found: {relativePath}");
            return null;
        }
    }
}
