﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms.VisualStyles;
using DevExpress.Xpf.WindowsUI;
using HmiPro.Config;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Models;
using HmiPro.Redux.Patches;
using HmiPro.Redux.Reducers;
using Reducto;
using YCsharp.Model.Procotol;
using YCsharp.Model.Procotol.SmParam;
using YCsharp.Service;
using DevExpress.XtraPrinting.Native;
using HmiPro.Config.Models;
using HmiPro.Redux.Effects;
using YCsharp.Util;

namespace HmiPro.Redux.Cores {
    /// <summary>
    /// 数据采集逻辑服务
    /// <date>2017-12-17</date>
    /// <author>ychost</author>
    /// </summary>
    public class CpmCore {
        /// <summary>
        /// 数据采集服务
        /// </summary>
        public YSmParamTcp SmParamTcp;
        /// <summary>
        /// 日志
        /// </summary>
        public LoggerService Logger;
        //编码：值（在线数据）
        //加float增加算法参数计算效率
        //计算相关参数的时候需要使用
        readonly IDictionary<string, IDictionary<int, float>> onlineFloatDict = new ConcurrentDictionary<string, IDictionary<int, float>>();
        /// <summary>
        /// 订阅指令：执行逻辑
        /// </summary>
        readonly IDictionary<string, Action<AppState, IAction>> actionExecutors = new ConcurrentDictionary<string, Action<AppState, IAction>>();
        /// <summary>
        /// 最后打开报警灯时间
        /// </summary>
        public IDictionary<string, AlarmLightsState> AlarmLightsStateDict;

        //编码：采集参数
        //外部可能会对此进行采样
        public IDictionary<string, IDictionary<int, Cpm>> OnlineCpmDict;

        public CpmCore() {
            Logger = LoggerHelper.CreateLogger(GetType().ToString());
        }

        /// <summary>
        /// 只有配置文件加载完成才能调用此初始化
        /// </summary>
        public void Init() {
            foreach (var pair in MachineConfig.MachineDict) {
                onlineFloatDict[pair.Key] = new ConcurrentDictionary<int, float>();
            }
            AlarmLightsStateDict = App.Store.GetState().CpmState.AlarmLightsStateDict;
            actionExecutors[AlarmActions.OPEN_ALARM_LIGHTS] = doOpenAlarmLights;
            actionExecutors[AlarmActions.CLOSE_ALARM_LIGHTS] = doCloseAlarmLights;
            actionExecutors[OeeActions.UPDATE_OEE_PARTIAL_VALUE] = whenOeeUpdated;
            App.Store.Subscribe(actionExecutors);
        }

        /// <summary>
        /// 更新参数界面上面的Oee显示
        /// </summary>
        /// <param name="state"></param>
        /// <param name="action"></param>
        void whenOeeUpdated(AppState state, IAction action) {
            var oeeAction = (OeeActions.UpdateOeePartialValue)action;
            var onlineDict = OnlineCpmDict[oeeAction.MachineCode];
            if (oeeAction.TimeEff.HasValue) {
                onlineDict[DefinedParamCode.OeeTime].Value = oeeAction.TimeEff.Value;
                onlineDict[DefinedParamCode.OeeTime].ValueType = SmParamType.Signal;
            }
            if (oeeAction.SpeedEff.HasValue) {
                onlineDict[DefinedParamCode.OeeSpeed].Value = oeeAction.SpeedEff.Value;
                onlineDict[DefinedParamCode.OeeSpeed].ValueType = SmParamType.Signal;
            }
            if (oeeAction.QualityEff.HasValue) {
                onlineDict[DefinedParamCode.OeeQuality].Value = oeeAction.QualityEff;
                onlineDict[DefinedParamCode.OeeQuality].ValueType = SmParamType.Signal;
            }
            if (oeeAction.Oee.HasValue) {
                onlineDict[DefinedParamCode.Oee].Value = oeeAction.Oee;
                onlineDict[DefinedParamCode.Oee].ValueType = SmParamType.Signal;
            }
        }

        /// <summary>
        /// 异步启动服务，将在线参数字典与AppState全局字典进行绑定
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public Task StartAsync(string ip, int port) {
            return Task.Run(() => {
                if (SmParamTcp == null) {
                    SmParamTcp = new YSmParamTcp(ip, port, LoggerHelper.CreateLogger("YSmParamTcp"));
                    SmParamTcp.OnDataReceivedAction += smModelsHandler;
                    OnlineCpmDict = App.Store.GetState().CpmState.OnlineCpmsDict;
                    //检查超时
                    YUtil.SetInterval(HmiConfig.CpmTimeout, () => {
                        checkCpmTimeout(HmiConfig.CpmTimeout);
                    });
                }
                SmParamTcp.Start();

            });
        }

        /// <summary>
        /// 停止掉Tcp监听服务
        /// </summary>
        public void Stop() {
            SmParamTcp?.StopSoft();
        }

        /// <summary>
        /// 打开报警灯，报警Ip必须配置
        /// </summary>
        void doOpenAlarmLights(AppState state, IAction action) {
            var alarmAction = (AlarmActions.OpenAlarmLights)action;
            //update: 2018-01-13
            //如果报警灯还处于工作状态，则忽略此次报警响灯操作
            if (AlarmLightsStateDict[alarmAction.MachineCode] == AlarmLightsState.On) {
                return;
            }
            AlarmLightsStateDict[alarmAction.MachineCode] = AlarmLightsState.On;
            if (MachineConfig.AlarmIpDict.TryGetValue(alarmAction.MachineCode, out var ip)) {
                SmParamTcp?.OpenAlarm(ip);
                //一定时间后关闭报警灯
                YUtil.SetTimeout(alarmAction.LightMs, () => {
                    //App.Store.Dispatch(new AlarmActions.CloseAlarmLights(alarmAction.MachineCode));
                    doCloseAlarmLights(state, new AlarmActions.CloseAlarmLights(alarmAction.MachineCode));
                });
            } else {
                Logger.Error($"机台 {alarmAction.MachineCode} 没有报警 ip");
            }
        }

        /// <summary>
        /// 关闭报警灯，报警Ip必须配置
        /// </summary>
        void doCloseAlarmLights(AppState state, IAction action) {
            var alarmAction = (AlarmActions.CloseAlarmLights)action;
            AlarmLightsStateDict[alarmAction.MachineCode] = AlarmLightsState.Off;
            if (MachineConfig.AlarmIpDict.TryGetValue(alarmAction.MachineCode, out var ip)) {
                SmParamTcp?.CloseAlarm(ip);
            } else {
                Logger.Error($"机台 {alarmAction.MachineCode} 没有报警 ip");
            }
        }

        /// <summary>
        /// 来自底层数据处理
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="smModels"></param>
        void smModelsHandler(string ip, List<SmModel> smModels) {
            //程序尚未初始化完成
            if (!AppState.IsCompleteInited) {
                return;
            }
            if (!MachineConfig.IpToMachineCodeDict.TryGetValue(ip, out var code)) {
                App.Store.Dispatch(new CpmActions.UnregIpActived(ip));
                Logger.Debug($"ip {ip} 未注册", ConsoleColor.Red);
                return;
            }
            App.Store.Dispatch(new CpmActions.CpmIpActivted(code, ip, DateTime.Now));
            smModels?.ForEach(sm => {
                //处理参数包
                if (sm.PackageType == SmPackageType.ParamPackage) {
                    updateMachieCpms(code, sm, ip);
                }
            });
        }



        /// <summary>
        /// 处理参数包，将底层的包转成上层需要的Cpm
        /// </summary>
        /// <param name="machineCode"></param>
        /// <param name="sm"></param>
        void updateMachieCpms(string machineCode, SmModel sm, string ip) {
            var cpmsDirect = Cpm.ConvertBySmModel(machineCode, sm);
            var cpmsRelate = new List<Cpm>();
            var cpms = new List<Cpm>();
            IDictionary<int, Cpm> updatedCpmsDiffDict = new Dictionary<int, Cpm>();
            //简洁算法计算出来的参数
            cpmsDirect?.ForEach(cpm => {
                //普通浮点参数
                if (cpm.ValueType == SmParamType.Signal) {
                    var floatVal = cpm.GetFloatVal();
                    onlineFloatDict[machineCode][cpm.Code] = floatVal;
                    var relateCpms = calcRelateCpm(machineCode, cpm.Code);
                    cpmsRelate.AddRange(relateCpms);
                    //Rfid卡
                } else if (cpm.ValueType == SmParamType.StrRfid) {
                    var rfidAccept = createRfid(machineCode, cpm);
                    if (rfidAccept.RfidType != DMesActions.RfidType.Unknown) {
                        App.Store.Dispatch(rfidAccept);
                    }
                    //485通讯状态
                } else if (cpm.ValueType == SmParamType.SingleComStatus) {
                    App.Store.Dispatch(new CpmActions.Com485SingleStatusAccept(machineCode, ip, (SmSingleStatus)cpm.Value, cpm.Code));
                    if ((SmSingleStatus)cpm.Value == SmSingleStatus.Error) {
                        App.Store.Dispatch(new AlarmActions.Com485SingleError(machineCode, ip, cpm.Code, cpm.Name));
                    }
                }
            });
            if (cpmsDirect != null) {
                cpms.AddRange(cpmsDirect);
                cpms.AddRange(cpmsRelate);
            }
            //更新参数字典
            //fixed:2017-10-20
            //      一包参数的时间应该一致
            var pickTime = DateTime.Now;
            void updateDiff(Cpm cpm) {
                cpm.PickTime = pickTime;
                //直接更新，会更新界面的数据
                if (OnlineCpmDict[machineCode].TryGetValue(cpm.Code, out var storeCpm)) {
                    //Rfid 数据是在 DMesCore 更新
                    if (!DefinedParamCode.IsRfidParam(cpm.Code)) {
                        storeCpm.Update(cpm.Value, cpm.ValueType, pickTime);
                    }
                } else {
                    Logger.Error($"参数 {cpm.Code} 未注册", 3600);
                }
                updatedCpmsDiffDict[cpm.Code] = cpm;
            }

            //差异更新，使用linq 2017-11-13
            (from c in cpms
             where (
                 (c.Value != null && !string.IsNullOrEmpty(c.Value?.ToString()))
                 && (!OnlineCpmDict[machineCode].ContainsKey(c.Code)
                     || OnlineCpmDict[machineCode][c.Code].Value.ToString() != c.Value.ToString())
             )
             select c
            ).ForEach(updateDiff);
            //一定要先派遣所有更新，再派遣部分更新
            //这样就保证了reducer里面数据的唯一性
            if (cpms.Count > 0) {
                //派发所有接受到的参数
                App.Store.Dispatch(new CpmActions.CpmUpdatedAll(machineCode, cpms));
                dispatchLogicSetting(machineCode, cpms);
            }
            if (updatedCpmsDiffDict.Count > 0) {
                var diffCpms = updatedCpmsDiffDict.Values.ToList();
                //所有变化的参数
                App.Store.Dispatch(new CpmActions.CpmUpdateDiff(machineCode, updatedCpmsDiffDict));
                dispatchDiffLogicSetting(machineCode, diffCpms);
            }
            //检查Mq的Bom表数据报警
            dispatchCheckBomAlarm(machineCode, cpms);
            //检查Plc上下限报警
            dispatchCheckPlcAlarm(machineCode, cpms);
        }

        /// <summary>
        /// 为接受到的Rfid卡分类，有人员卡、收线卡、放线卡等等
        /// </summary>
        /// <param name="machineCode"></param>
        /// <param name="cpm"></param>
        private DMesActions.RfidAccpet createRfid(string machineCode, Cpm cpm) {
            DMesActions.RfidAccpet rfidAccept = new DMesActions.RfidAccpet(machineCode, cpm.Value.ToString(),
                DMesActions.RfidWhere.FromCpm, DMesActions.RfidType.Unknown);
            //放线卡
            if (DefinedParamCode.StartAxisRfid == cpm.Code) {
                rfidAccept.RfidType = DMesActions.RfidType.StartAxis;
                //收线卡
            } else if (DefinedParamCode.EndAxisRfid == cpm.Code) {
                rfidAccept.RfidType = DMesActions.RfidType.EndAxis;
                //人员卡
            } else if (DefinedParamCode.EmpRfid == cpm.Code) {
                rfidAccept.RfidType = DMesActions.RfidType.EmpStartMachine;
                rfidAccept.MqData = new MqEmpRfid() {
                    employeeCode = rfidAccept.Rfid,
                    macCode = machineCode,
                    name = rfidAccept.Rfid,
                    PrintTime = DateTime.Now,
                    type = MqRfidType.EmpStartMachine
                };
                //物料卡
            } else if (DefinedParamCode.EmpRfid == cpm.Code) {
            }
            return rfidAccept;
        }


        /// <summary>
        /// 检查参数超时，即参数之前有值，但是后来由于硬件掉线了没有值，这个时候应该更新参数的显示
        /// 如果不更新则会一直显示掉线之前的值
        /// </summary>
        /// <param name="timeoutMs"></param>
        private void checkCpmTimeout(int timeoutMs) {
            foreach (var cpmsDict in OnlineCpmDict) {
                foreach (var pair in cpmsDict.Value) {
                    if (pair.Value.ValueType != SmParamType.Signal && pair.Value.ValueType != SmParamType.String) {
                        continue;
                    }
                    //时间差 
                    var msDiff = (DateTime.Now - pair.Value.PickTime).TotalMilliseconds;
                    if (msDiff > timeoutMs) {
                        pair.Value.Update("暂无", SmParamType.Timeout, DateTime.Now);
                    }
                }
            }
        }

        /// <summary>
        /// 排产逻辑参数差异值
        /// </summary>
        /// <param name="machineCode"></param>
        /// <param name="diffCpms"></param>
        private void dispatchDiffLogicSetting(string machineCode, List<Cpm> diffCpms) {
            var setting = GlobalConfig.MachineSettingDict[machineCode];
            foreach (var cpm in diffCpms) {
                if (cpm.ValueType != SmParamType.Signal) {
                    continue;
                }
                if (cpm.Name == setting.NoteMeter) {
                    App.Store.Dispatch(new CpmActions.NoteMeterDiffAccept(machineCode, cpm.GetFloatVal()));
                }
                if (cpm.Name == setting.Spark) {
                    App.Store.Dispatch(new CpmActions.SparkDiffAccept(machineCode, cpm.GetFloatVal()));
                }
                if (cpm.Name == setting.StateSpeed) {
                    App.Store.Dispatch(new CpmActions.StateSpeedDiffAccpet(machineCode, cpm.GetFloatVal()));
                    //速度变化为0的事件
                    if (cpm.GetFloatVal() == 0) {
                        App.Store.Dispatch(new CpmActions.StateSpeedDiffZeroAccept(machineCode));
                    }
                }
            }
        }

        /// <summary>
        /// 派遣设定的逻辑参数值
        /// </summary>
        /// <param name="machineCode"></param>
        /// <param name="cpms"></param>
        private void dispatchLogicSetting(string machineCode, List<Cpm> cpms) {
            var setting = GlobalConfig.MachineSettingDict[machineCode];
            foreach (var cpm in cpms) {
                if (cpm.ValueType != SmParamType.Signal) {
                    continue;
                }
                if (setting.StateSpeed == cpm.Name) {
                    App.Store.Dispatch(new CpmActions.StateSpeedAccept(machineCode, cpm.GetFloatVal()));
                    if (cpm.GetFloatVal() == 0) {
                        App.Store.Dispatch(new CpmActions.StateSpeedZeroAccept(machineCode));
                    }
                }
                //会计算Oee
                if (setting.OeeSpeed == cpm.Name) {
                    App.Store.Dispatch(new CpmActions.OeeSpeedAccept(machineCode, cpm.GetFloatVal()));
                }
                if (setting.NoteMeter == cpm.Name) {
                    App.Store.Dispatch(new CpmActions.NoteMeterAccept(machineCode, cpm.GetFloatVal()));
                }

                if (setting.Od == cpm.Name) {
                    App.Store.Dispatch(new CpmActions.OdAccept(machineCode, cpm.GetFloatVal()));
                }
            }
        }


        /// <summary>
        /// 检查Plc报警
        /// </summary>
        /// <param name="machineCode"></param>
        /// <param name="cpms"></param>
        void dispatchCheckPlcAlarm(string machineCode, List<Cpm> cpms) {
            foreach (var cpm in cpms) {
                if (cpm.ValueType != SmParamType.Signal) {
                    continue;
                }
                if (MachineConfig.MachineDict[machineCode].CodeToPlcAlarmDict.TryGetValue(cpm.Code, out var plcAlarm)) {
                    if (plcAlarm.MaxCode.HasValue) {
                        var maxCpm = App.Store.GetState().CpmState.OnlineCpmsDict[machineCode][plcAlarm.MaxCode.Value];
                        if (maxCpm.ValueType == SmParamType.Signal) {
                            if (cpm.GetFloatVal() > maxCpm.GetFloatVal()) {
                                var message = $"{cpm.Name} 超过最大值";
                                App.Store.Dispatch(new AlarmActions.CpmPlcAlarmOccur(machineCode, message, cpm.Code, cpm.Name));
                            }
                        }
                    }
                    if (plcAlarm.MinCode.HasValue) {
                        var minCpm = App.Store.GetState().CpmState.OnlineCpmsDict[machineCode][plcAlarm.MinCode.Value];
                        if (minCpm.ValueType == SmParamType.Signal) {
                            if (cpm.GetFloatVal() < minCpm.GetFloatVal()) {
                                var message = $"{cpm.Name} 小于最小值";
                                App.Store.Dispatch(new AlarmActions.CpmPlcAlarmOccur(machineCode, message, cpm.Code, cpm.Name));
                            }
                        }
                    }
                }
            }

        }

        /// <summary>
        /// 将需要报警检查的Cpm发送出去
        /// </summary>
        /// <param name="machineCode"></param>
        /// <param name="cpms"></param>
        void dispatchCheckBomAlarm(string machineCode, List<Cpm> cpms) {
            cpms?.ForEach(cpm => {
                if (MachineConfig.MachineDict[machineCode].CodeToMqBomAlarmCpmDict.TryGetValue(cpm.Code, out var alarmCpm)) {
                    if (cpm.ValueType != SmParamType.Signal) {
                        Logger.Error($"机台 {machineCode} 参数 {cpm.Name} 的值不是浮点类型，不能报警");
                        return;
                    }
                    var bomKeys = alarmCpm.MqAlarmBomKeys;
                    if (bomKeys?.Length != 2) {
                        Logger.Error($"机台 {machineCode} 参数 {cpm.Name} 的报警配置有误，长度不为 2 ");
                        return;
                    }
                    string max = null;
                    string min = null;
                    string std = null;
                    foreach (var key in bomKeys) {
                        if (key.ToLower().Contains("max")) {
                            max = key;
                        } else if (key.ToLower().Contains("min")) {
                            min = key;
                        } else {
                            std = key;
                        }
                    }
                    var alarmCheck = new AlarmBomCheck() { Cpm = cpm, MaxBomKey = max, MinBomKey = min, StdBomKey = std };
                    App.Store.Dispatch(new AlarmActions.CheckCpmBomAlarm(machineCode, alarmCheck));
                }
            });
        }

        /// <summary>
        /// 更新需要算法计算的参数，并返回转换后的集合
        /// </summary>
        /// <returns></returns>
        List<Cpm> calcRelateCpm(string code, int updatedCode) {
            List<Cpm> cpms = new List<Cpm>();
            foreach (var infoPair in MachineConfig.MachineDict[code].CodeToRelateCpmDict) {
                var val = infoPair.Value.ExecCalc(onlineFloatDict[code], updatedCode);
                if (val.HasValue) {
                    //设置精度
                    val = (float)Math.Round(val.Value, HmiConfig.MathRound);
                    //实时更新在线数据表
                    onlineFloatDict[code][infoPair.Key] = val.Value;
                    cpms.Add(new Cpm() {
                        Code = infoPair.Key,
                        Value = val,
                        ValueType = SmParamType.Signal,
                        Name = infoPair.Value.Name
                    });
                }
            }
            return cpms;
        }
    }
}
