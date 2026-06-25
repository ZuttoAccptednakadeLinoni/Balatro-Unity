/****************************************************
    文件：ProcedureGame.cs
    作者：k0itoyuu
    日期：#CreateTime#
    功能：游戏主流程 —— 初始化 GameController，打开 UI，发牌。
*****************************************************/
using System.Collections;
using System.Collections.Generic;
using GameFramework.Event;
using GameFramework.Procedure;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace Yuu
{
    public class ProcedureGame : ProcedureBase
    {
        private ProcedureOwner procedureOwner;
        private bool changeScene = false;
        private GameController gameController;

        protected override void OnInit(ProcedureOwner procedureOwner)
        {
            base.OnInit(procedureOwner);
        }

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            this.procedureOwner = procedureOwner;
            this.changeScene = false;

            GameEntry.Event.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenUIFormSuccess);
            GameEntry.Event.Subscribe(ChangeSceneEventArgs.EventId, OnChangeScene);

            // 创建游戏控制器（先不初始化，等 UICardForm 就绪）
            gameController = GameController.Create();

            // 打开 UI
            GameEntry.UI.OpenUIForm(EnumUIForm.UICardForm);
            GameEntry.UI.OpenUIForm(EnumUIForm.UIGameInfoForm);
            GameEntry.UI.OpenUIForm(EnumUIForm.UIJokerForm);
            GameEntry.UI.OpenUIForm(EnumUIForm.UITarotForm);
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            base.OnLeave(procedureOwner, isShutdown);
        }

        protected override void OnDestroy(ProcedureOwner procedureOwner)
        {
            base.OnDestroy(procedureOwner);
        }

        private void OnOpenUIFormSuccess(object sender, GameEventArgs e)
        {
            OpenUIFormSuccessEventArgs ne = (OpenUIFormSuccessEventArgs)e;

            // 检测 UICardForm 打开成功 → 初始化 Controller 并发牌
            if (ne.UIForm.Logic is UICardForm cardForm)
            {
                // 初始化（绑定 UICardForm → GameController）
                gameController.Initialize(cardForm);
                cardForm.SetGameController(gameController);

                // 发放初始手牌（HandSize 张，默认 8）
                gameController.DealInitialHand();
            }
        }

        private void OnChangeScene(object sender, GameEventArgs e)
        {
            ChangeSceneEventArgs ne = (ChangeSceneEventArgs)e;
            if (ne == null)
                return;

            changeScene = true;
            procedureOwner.SetData<VarInt32>(Constant.ProcedureData.NextSceneId, ne.SceneId);
        }
    }
}
