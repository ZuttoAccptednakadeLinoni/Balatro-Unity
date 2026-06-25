/****************************************************
    文件：UIGameSettingForm.cs
	作者：k0itoyuu
    日期：#CreateTime#
	功能：Nothing
*****************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Yuu
{
    public class UIGameSettingForm : UGuiFormEx
    {
        public Transform levelSelectButtonRoot;
        public Button button;
        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            button.onClick.AddListener(OnBackButtonClick);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            ShowLevelSelectionButtonItems();
            
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
        }

        private void OnBackButtonClick()
        {
            Debug.Log("点击");
            ShowItem<Card>(EnumItem.Card, (item) =>
            {
                item.transform.SetParent(levelSelectButtonRoot, false);
                item.transform.localScale = Vector3.one;
                item.transform.eulerAngles = Vector3.zero;
                item.transform.localPosition = Vector3.zero;
                item.GetComponent<Card>().OnOpen();
            });
        }
        private void ShowLevelSelectionButtonItems()
        {
            
        }
        
    }
}
