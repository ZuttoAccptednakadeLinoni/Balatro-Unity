/****************************************************
    文件：UIMainMenuForm.cs
	作者：k0itoyuu
    日期：#CreateTime#
	功能：Nothing
*****************************************************/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Yuu
{
    public class UIMainMenuForm : UGuiFormEx
    {
        public Button beginButton;
        public Button settingButton;
        public Button quitButton;
        public Button collectionButton;
        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            beginButton.onClick.AddListener(OnLevelBeginButtonClick);
            settingButton.onClick.AddListener(OnSettingButtonClick);
            quitButton.onClick.AddListener(OnQuitButtonlick);
            collectionButton.onClick.AddListener(OnCollectButtonClick);
        }

        private void OnCollectButtonClick()
        {
            throw new System.NotImplementedException();
        }

        private void OnQuitButtonlick()
        {
            throw new System.NotImplementedException();
        }

        private void OnSettingButtonClick()
        {
            GameEntry.UI.OpenUIForm(EnumUIForm.UIOptionsForm);  
        }

        private void OnLevelBeginButtonClick()
        {
            throw new System.NotImplementedException();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
        }
        private void ShowTowerBuildButtons()
        {
            
        }
    }
}

