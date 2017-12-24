﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentScheduler;
using HmiPro.Config;
using HmiPro.Config.Models;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Effects;
using HmiPro.Redux.Models;
using Reducto;
using YCsharp.Service;
using YCsharp.Util;

namespace HmiPro.Redux.Cores {
    /// <summary>
    /// 调度器
    /// <href>https://github.com/fluentscheduler/FluentScheduler</href>
    /// <date>2017-12-20</date>
    /// <author>ychost</author>
    /// </summary>
    public class SchCore : Registry {
        private readonly SysEffects sysEffects;
        private readonly MqEffects mqEffects;
        private readonly OeeEffects oeeEffects;
        public readonly LoggerService Logger;
        public SchCore(SysEffects sysEffects, MqEffects mqEffects, OeeEffects oeeEffects) {
            UnityIocService.AssertIsFirstInject(GetType());
            this.sysEffects = sysEffects;
            this.mqEffects = mqEffects;
            this.oeeEffects = oeeEffects;
            Logger = LoggerHelper.CreateLogger(GetType().ToString());
        }

        /// <summary>
        /// 配置文件加载之后才能对其初始化
        /// 1. 每隔指定时间(15分钟)关闭显示器
        /// 2. 每天8:00打开显示器
        /// 3. 定时上传Cpm到Mq
        /// </summary>
        public async Task Init() {
            await App.Store.Dispatch(sysEffects.StartCloseScreenTimer(new SysActions.StartCloseScreenTimer(HmiConfig.CloseScreenInterval)));
            //启动定时上传Cpms到Mq定时器
            await App.Store.Dispatch(mqEffects.StartUploadCpmsInterval(new MqActiions.StartUploadCpmsInterval(HmiConfig.QueUpdateWebBoard, HmiConfig.UploadWebBoardInterval)));

            //每天8点打开显示器
            Schedule(() => {
                App.Store.Dispatch(new SysActions.OpenScreen());
            }).ToRunEvery(1).Days().At(8, 0);

            foreach (var pair in MachineConfig.MachineDict) {
                var machine = pair.Value;
                if (machine.OeeSpeedType == OeeActions.CalcOeeSpeedType.Unknown) {
                    App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                        Title = $"机台 {machine.Code} 无法计算Oee-速度效率",
                        Content = "请联系管理员配置 OeeSppedType"
                    }));
                }

            }

            var interval = 1 * 60 * 1000;
            await App.Store.Dispatch(oeeEffects.StartCalcOeeTimer(new OeeActions.StartCalcOeeTimer(interval)));
            JobManager.Initialize(this);

        }


        void FluentSchTest() {
            Schedule(() => {
                App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                    Title = "测试Fluent Schedule Task",
                    Content = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }));
                Logger.Debug("测试Flunt Schedule");
            }).ToRunEvery(1).Days().At(10, 40);

        }
    }
}