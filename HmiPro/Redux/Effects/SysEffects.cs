﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using FSLib.App.SimpleUpdater;
using HmiPro.Config;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Models;
using HmiPro.Redux.Patches;
using HmiPro.Redux.Reducers;
using HmiPro.Redux.Services;
using Newtonsoft.Json;
using Reducto;
using YCsharp.Service;
using YCsharp.Util;

namespace HmiPro.Redux.Effects {
    /// <summary>
    /// 开启 Http 服务，打开/关闭 显示器（定时操作）
    /// <date>2017-12-19</date>
    /// <author>ychost</author>
    /// </summary>
    public class SysEffects {
        /// <summary>
        /// 启动 Http 服务（命令解析）
        /// </summary>
        public readonly StorePro<AppState>.AsyncActionNeedsParam<SysActions.StartHttpSystem, bool> StartHttpSystem;
        /// <summary>
        /// 定时关闭显示器（启动）
        /// </summary>
        public readonly StorePro<AppState>.AsyncActionNeedsParam<SysActions.StartCloseScreenTimer> StartCloseScreenTimer;
        /// <summary>
        /// 定时关闭显示器（停止）
        /// </summary>
        public readonly StorePro<AppState>.AsyncActionNeedsParam<SysActions.StopCloseScreenTimer> StopCloseScrenTimer;
        /// <summary>
        /// 日志
        /// </summary>
        public readonly LoggerService Logger;
        /// <summary>
        /// 定时关闭显示器的定时器
        /// </summary>
        public Timer CloseScrrenTimer;

        /// <summary>
        /// 初始化上面的 Effect
        /// </summary>
        /// <param name="sysService"></param>
        public SysEffects(SysService sysService) {
            UnityIocService.AssertIsFirstInject(GetType());
            Logger = LoggerHelper.CreateLogger(GetType().ToString());
            //启动http解析服务
            StartHttpSystem = App.Store.asyncAction<SysActions.StartHttpSystem, bool>(
                async (dispatch, getState, instance) => {
                    dispatch(instance);
                    var isStarted = await sysService.StartHttpSystem(instance);
                    if (isStarted) {
                        App.Store.Dispatch(new SysActions.StartHttpSystemSuccess());
                    } else {
                        App.Store.Dispatch(new SysActions.StartHttpSystemFailed());
                    }
                    return isStarted;
                });
            //启动关闭显示器定时器
            StartCloseScreenTimer = App.Store.asyncActionVoid<SysActions.StartCloseScreenTimer>(
                async (dispatch, getState, instance) => {
                    dispatch(instance);
                    await Task.Run(() => {
                        if (CloseScrrenTimer != null) {
                            YUtil.RecoveryTimeout(CloseScrrenTimer);
                        } else {
                            CloseScrrenTimer = YUtil.SetInterval(instance.Interval, () => {
                                App.Store.Dispatch(new SysActions.CloseScreen());
                            });
                        }
                    });
                });

            //停止关闭显示器定时器
            StopCloseScrenTimer = App.Store.asyncActionVoid<SysActions.StopCloseScreenTimer>(
                async (dispatch, getState, instance) => {
                    dispatch(instance);
                    await Task.Run(() => {
                        if (CloseScrrenTimer != null) {
                            YUtil.ClearTimeout(CloseScrrenTimer);
                        }
                    });
                });
        }

    }
}
