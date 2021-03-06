﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentScheduler;
using HmiPro.Config;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Models;
using Newtonsoft.Json;
using YCsharp.Service;
using YCsharp.Util;

namespace HmiPro.Redux.Services {
    /// <summary>
    /// <date>2017-12-19</date>
    /// <author>ychost</author>
    /// </summary>
    public class MqService {
        public readonly LoggerService Logger;
        public MqService() {
            UnityIocService.AssertIsFirstInject(GetType());
            Logger = LoggerHelper.CreateLogger(GetType().ToString());
        }


        /// <summary>
        /// 机台命令处理
        /// </summary>
        /// <param name="json"></param>
        public void CmdAccept(string json) {
            try {
                var mqCmds = JsonConvert.DeserializeObject<List<AppCmd>>(json);
                var mqCmd = mqCmds.FirstOrDefault(m => MachineConfig.MachineDict.Keys.Contains(m.machineCode.ToUpper()));
                if (mqCmd == null) {
                    return;
                }
                //机台过滤
                if (MachineConfig.MachineDict.Keys.Contains(mqCmd.machineCode.ToUpper())) {
                    mqCmd.machineCode = mqCmd.machineCode.ToUpper();
                    //指定执行时间
                    if (mqCmd.execTime.HasValue) {
                        var execTime = YUtil.UtcTimestampToLocalTime(mqCmd.execTime.Value);
                        Console.WriteLine($"任务将在 {execTime.ToString("G")} 执行");
                        JobManager.AddJob(() => {
                            App.Store.Dispatch(new MqActions.CmdAccept(mqCmd.machineCode, mqCmd));
                        }, (s) => s.ToRunOnceAt(execTime));
                    } else {
                        App.Store.Dispatch(new MqActions.CmdAccept(mqCmd.machineCode, mqCmd));
                    }
                }
            } catch (Exception e) {
                Logger.Error("处理机台命令异常，命令为：" + json, e);
            }
        }

        /// <summary>
        /// 处理排产任务
        /// </summary>
        /// <param name="json"></param>
        public void SchTaskAccept(string json) {
            try {
                MqSchTask schTask = JsonConvert.DeserializeObject<MqSchTask>(json);
                App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                    Title = "接受到新任务",
                    Content = "请注意及时更新进度"
                }));
                for (var i = 0; i < schTask.axisParam.Count; i++) {
                    schTask.axisParam[i].Index = i + 1;
                }
                Logger.Info("接受到任务数据：" + json);
                App.Store.Dispatch(new MqActions.SchTaskAccept(schTask));
            } catch (Exception e) {
                Logger.Error("排产任务反序列异常，json数据为：" + json, e);
                App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                    Title = "系统错误",
                    Content = "服务器推送的排产任务数据反序列化有误",
                    Level = NotifyLevel.Error
                }));
            }

        }



        /// <summary>
        /// 监听到扫描来料
        /// </summary>
        /// <param name="machineCode"></param>
        /// <param name="json"></param>
        public void ScanMaterialAccept(string machineCode, string json) {
            try {
                MqScanMaterial material = JsonConvert.DeserializeObject<MqScanMaterial>(json);
                App.Store.Dispatch(new MqActions.ScanMaterialAccpet(machineCode, material));
            } catch (Exception e) {
                Logger.Error($"扫描来料数据反序列化异常，json数据为 : {json}", e);
                App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                    Title = "系统错误",
                    Content = "扫描来料数据 数据反序列化有误",
                    Level = NotifyLevel.Error
                }));
            }
        }

        /// <summary>
        /// 接受到轴号卡
        /// </summary>
        /// <param name="json"></param>
        public void AxisRfidAccpet(string json) {
            try {
                MqAxisRfid mqRfid = JsonConvert.DeserializeObject<MqAxisRfid>(json);
                //机台校验
                if (!MachineConfig.HmiName.ToUpper().Contains(mqRfid.macCode.ToUpper())) {
                    return;
                }
                DMesActions.RfidType type = DMesActions.RfidType.Unknown;
                if (mqRfid.msgType == MqRfidType.AxisStart) {
                    type = DMesActions.RfidType.StartAxis;
                } else if (mqRfid.msgType == MqRfidType.AxisEnd) {
                    type = DMesActions.RfidType.EndAxis;
                }
                App.Store.Dispatch(new DMesActions.RfidAccpet(mqRfid.macCode, mqRfid.rfids,
                    DMesActions.RfidWhere.FromMq, type, mqRfid));

            } catch (Exception e) {
                Logger.Error($"线盘卡Rfid反序列化异常，json数据为 : {json}", e);
                App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                    Title = "系统错误",
                    Content = "线盘Rfid 数据反序列化有误",
                    Level = NotifyLevel.Error
                }));
            }
        }

        /// <summary>
        /// 接受到rfid数据
        /// </summary>
        /// <param name="json"></param>
        public void EmpRfidAccept(string json) {
            try {
                MqEmpRfid mqRfid = JsonConvert.DeserializeObject<MqEmpRfid>(json);
                //机台校验
                if (!MachineConfig.HmiName.ToUpper().Contains(mqRfid.macCode)) {
                    return;
                }
                //设置打卡时间
                mqRfid.PrintTime = DateTime.Now;
                DMesActions.RfidType type = DMesActions.RfidType.Unknown;
                if (mqRfid.type == MqRfidType.EmpStartMachine) {
                    type = DMesActions.RfidType.EmpStartMachine;
                } else if (mqRfid.type == MqRfidType.EmpEndMachine) {
                    type = DMesActions.RfidType.EmpEndMachine;
                } else if (mqRfid.type == MqRfidType.EmpStartWork) {
                    type = DMesActions.RfidType.EmpStartWork;
                } else if (mqRfid.type == MqRfidType.EmpEndWork) {
                    type = DMesActions.RfidType.EmpEndWork;
                }
                App.Store.Dispatch(new DMesActions.RfidAccpet(mqRfid.macCode, mqRfid.employeeCode,
                    DMesActions.RfidWhere.FromMq, type, mqRfid));
            } catch (Exception e) {
                Logger.Error($"人员Rfid反序列化异常，json数据为 : {json}", e);
                App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                    Title = "系统错误",
                    Content = "人员Rfid 数据反序列化有误",
                    Level = NotifyLevel.Error
                }));
            }
        }
    }
}
