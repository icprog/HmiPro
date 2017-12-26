﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Mvvm.DataAnnotations;
using DevExpress.Mvvm;
using HmiPro.Config;
using HmiPro.Helpers;
using HmiPro.Mocks;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Models;
using YCsharp.Util;

namespace HmiPro.ViewModels.Sys {
    [POCOViewModel]
    public class TestViewModel {
        public TestViewModel() {
        }

        private Func<int> rand = YUtil.GetRandomIntGen(0, 10);

        [Command(Name = "OpenAlarmCommand")]
        public void OpenAlarm(int ms) {
            var machineCode = MachineConfig.MachineDict.FirstOrDefault().Key;
            App.Store.Dispatch(new AlarmActions.OpenAlarmLights(machineCode, ms));

        }

        [Command(Name = "CloseAlarmCommand")]
        public void CloseAlarm() {
            var machineCode = MachineConfig.MachineDict.FirstOrDefault().Key;
            App.Store.Dispatch(new AlarmActions.CloseAlarmLights(machineCode));
        }

        [Command(Name = "CloseScreenCommand")]
        public void CloseScreen(object secObj) {
            if (secObj == null) {
                YUtil.CloseScreenByNirCmd(AssetsHelper.GetAssets().ExeNirCmd);
            } else {
                int sec = int.Parse(secObj.ToString());
                var ms = sec * 1000;
                Task.Run(() => {
                    YUtil.CloseScreenByNirCmd(AssetsHelper.GetAssets().ExeNirCmd);
                    YUtil.SetTimeout(ms, () => {
                        YUtil.OpenScreenByNirCmmd(AssetsHelper.GetAssets().ExeNirCmd);
                    });
                });
            }
        }

        [Command(Name = "OpenScreenCommand")]
        public void OpenScreen() {
            YUtil.OpenScreenByNirCmmd(AssetsHelper.GetAssets().ExeNirCmd);
        }

        [Command(Name = "StandbyScreenCommand")]
        public void StandbyScreen() {
            YUtil.SetMonitorInState(YUtil.MonitorState.MonitorStateStandBy);
        }

    }
}