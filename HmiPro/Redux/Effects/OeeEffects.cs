﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DevExpress.XtraPrinting.Native;
using HmiPro.Config;
using HmiPro.Config.Models;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Cores;
using HmiPro.Redux.Models;
using HmiPro.Redux.Patches;
using HmiPro.Redux.Reducers;
using HmiPro.Redux.Services;
using YCsharp.Service;
using YCsharp.Util;

namespace HmiPro.Redux.Effects {
    /// <summary>
    /// <date>2017-12-20</date>
    /// <author>ychost</author>
    /// </summary>
    public class OeeEffects {

        public readonly LoggerService Logger;
        public StorePro<AppState>.AsyncActionNeedsParam<OeeActions.StartCalcOeeTimer> StartCalcOeeTimer;
        private readonly OeeCore oeeCore;
        public OeeEffects(OeeCore oeeCore) {
            UnityIocService.AssertIsFirstInject(GetType());
            Logger = LoggerHelper.CreateLogger(GetType().ToString());
            this.oeeCore = oeeCore;
            initStartCalcOeeTimer();
        }

        void initStartCalcOeeTimer() {
            StartCalcOeeTimer = App.Store.asyncActionVoid<OeeActions.StartCalcOeeTimer>(
              async (dispatch, getState, intance) => {
                  dispatch(intance);
                  YUtil.SetInterval(intance.Interval, () => {
                      foreach (var pair in getState().CpmState.MachineStateDict) {
                          var machineCode = pair.Key;
                          var timeEff = oeeCore.CalcOeeTimeEff(pair.Key, pair.Value);
                          var speedEff = oeeCore.CalcOeeSpeedEff(pair.Key, MachineConfig.MachineDict[machineCode].OeeSpeedType);
                          var qualityEff = oeeCore.CalcOeeQualityEff(pair.Key);
                          App.Store.Dispatch(new OeeActions.UpdateOeePartialValue(machineCode, timeEff, speedEff, qualityEff));
                      }
                  });
              });
        }
    }
}