using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GameFramework.Data;
using GameFramework.DataTable;
using UnityGameFramework.Runtime;
using Yuu;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Yuu.Data
{
    public sealed class TextureData
    {
        private DRTextures dRTextures;
        private DRAssetsPath dRAssetsPath;

        public int Id
        {
            get
            {
                return dRTextures.Id;
            }
        }

        public string Name
        {
            get
            {
                return dRTextures.Name;
            }
        }

        public int AssetId
        {
            get
            {
                return dRTextures.AssetId;
            }
        }

        public string AssetPath
        {
            get
            {
                return dRAssetsPath.AssetPath;
            }
        }

        public TextureData(DRTextures dRTextures, DRAssetsPath dRAssetsPath)
        {
            this.dRTextures = dRTextures;
            this.dRAssetsPath = dRAssetsPath;
        }
    }

    public sealed class DataTextures : DataBase
    {
        private IDataTable<DRTextures> dtTextures;
        private Dictionary<int, TextureData> dicTextureData;
        private Dictionary<int, Sprite[]> dicSpriteCache;

        protected override void OnInit()
        {

        }

        protected override void OnPreload()
        {
            LoadDataTable("Textures");
        }

        protected override void OnLoad()
        {
            dtTextures = GameEntry.DataTable.GetDataTable<DRTextures>();
            if (dtTextures == null)
                throw new System.Exception("Can not get data table textures");

            dicTextureData = new Dictionary<int, TextureData>();
            dicSpriteCache = new Dictionary<int, Sprite[]>();

            DRTextures[] drTextures = dtTextures.GetAllDataRows();
            foreach (var dRTextures in drTextures)
            {
                DRAssetsPath dRAssetsPath = GameEntry.Data.GetData<DataAssetsPath>().GetDRAssetsPathByAssetsId(dRTextures.AssetId);
                TextureData textureData = new TextureData(dRTextures, dRAssetsPath);
                dicTextureData.Add(dRTextures.Id, textureData);
            }
        }

        public TextureData GetTextureData(int id)
        {
            if (dicTextureData.ContainsKey(id))
            {
                return dicTextureData[id];
            }

            return null;
        }

        public TextureData[] GetAllTextureData()
        {
            int index = 0;
            TextureData[] results = new TextureData[dicTextureData.Count];
            foreach (var textureData in dicTextureData.Values)
            {
                results[index++] = textureData;
            }

            return results;
        }

        /// <summary>
        /// 根据图集 Id 和子精灵序号获取 Sprite。
        /// 首次访问时加载并缓存图集内的所有 Sprite。
        /// </summary>
        /// <param name="textureId">贴图配置表中的 Id（界面编号）。</param>
        /// <param name="spriteIndex">子精灵序号（从 0 开始）。</param>
        /// <returns>指定序号的 Sprite，若越界返回 null。</returns>
        public Sprite GetSprite(int textureId, int spriteIndex)
        {
            Sprite[] sprites = GetOrLoadSprites(textureId);
            if (sprites == null || spriteIndex < 0 || spriteIndex >= sprites.Length)
            {
                Log.Warning("Sprite index {0} out of range for texture id {1} (total sprites: {2}).",
                    spriteIndex, textureId, sprites != null ? sprites.Length : 0);
                return null;
            }

            return sprites[spriteIndex];
        }

        /// <summary>
        /// 获取图集中所有 Sprite。
        /// 首次访问时加载并缓存。
        /// </summary>
        /// <param name="textureId">贴图配置表中的 Id。</param>
        /// <returns>Sprite 数组。</returns>
        public Sprite[] GetAllSprites(int textureId)
        {
            return GetOrLoadSprites(textureId);
        }

        private Sprite[] GetOrLoadSprites(int textureId)
        {
            if (dicSpriteCache.TryGetValue(textureId, out Sprite[] cached))
                return cached;

            TextureData texData = GetTextureData(textureId);
            if (texData == null)
            {
                Log.Error("Texture id {0} not found.", textureId);
                return null;
            }

            string assetPath = texData.AssetPath;
            Sprite[] sprites = LoadAllSpritesFromPath(assetPath);
            dicSpriteCache[textureId] = sprites;
            return sprites;
        }

        /// <summary>
        /// 从精灵名称中提取数字索引（如 "8BitDeck_0"→0, "8BitDeck_51"→51）。
        /// 无法解析时返回 int.MaxValue 使其排到末尾。
        /// </summary>
        private static int ExtractSpriteIndex(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
                return int.MaxValue;

            int lastUnderscore = spriteName.LastIndexOf('_');
            if (lastUnderscore >= 0 && lastUnderscore < spriteName.Length - 1)
            {
                string numStr = spriteName.Substring(lastUnderscore + 1);
                if (int.TryParse(numStr, out int result))
                    return result;
            }
            return int.MaxValue;
        }

        private static Sprite[] LoadAllSpritesFromPath(string assetPath)
        {
#if UNITY_EDITOR
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null || assets.Length == 0)
                return null;

            List<Sprite> spriteList = new List<Sprite>(assets.Length);
            for (int i = 0; i < assets.Length; i++)
            {
                Sprite sprite = assets[i] as Sprite;
                if (sprite != null)
                    spriteList.Add(sprite);
            }

            // 按精灵名称中的数字后缀排序，确保顺序与图集 .meta 中 spriteSheet 定义一致，
            // 避免 AssetDatabase.LoadAllAssetsAtPath 返回非确定性顺序导致牌面错位。
            spriteList.Sort((a, b) =>
            {
                int indexA = ExtractSpriteIndex(a.name);
                int indexB = ExtractSpriteIndex(b.name);
                return indexA.CompareTo(indexB);
            });

            return spriteList.Count > 0 ? spriteList.ToArray() : null;
#else
            // Runtime: 通过 GameEntry.Resource 加载单个 Sprite
            // 子精灵的加载路径为 "assetPath/spriteName"
            Log.Warning("LoadAllSpritesFromPath is not fully supported in runtime mode.");
            return null;
#endif
        }

        protected override void OnUnload()
        {
            GameEntry.DataTable.DestroyDataTable<DRTextures>();

            dtTextures = null;
            dicTextureData = null;
            dicSpriteCache = null;
        }

        protected override void OnShutdown()
        {
        }
    }

}
