﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HmiPro.Config;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Cores;
using HmiPro.Redux.Models;
using HmiPro.Redux.Patches;
using HmiPro.Redux.Reducers;
using Reducto;
using YCsharp.Service;
using YCsharp.Util;
using Timer = System.Timers.Timer;

namespace HmiPro.Redux.Effects {
    /// <summary>
    /// 加载GlobalConfig、MachineConfig、xxHelper等等
    /// 将配置系统迁移到此处是为了能方便管理以及能够支持系统启动进度条
    /// 一定要先执行 LoadGlobalConfig 然后再执行 LoadMachineConfig
    /// <author>ychost</author>
    /// <date>2018-1-26</date>
    /// </summary>
    public class LoadEffects {
        /// <summary>
        /// 加载机台配置
        /// </summary>
        public StorePro<AppState>.AsyncActionNeedsParam<LoadActions.LoadMachieConfig, bool> LoadMachineConfig;
        /// <summary>
        /// 加载全局配置，GlobalConfig、各种Helper 等等
        /// </summary>
        public StorePro<AppState>.AsyncActionNeedsParam<LoadActions.LoadGlobalConfig, bool> LoadGlobalConfig;
        /// <summary>
        /// 日志
        /// </summary>
        public LoggerService Logger;

        public LoadEffects() {
            Logger = LoggerHelper.CreateLogger(GetType().ToString());
            initLoadGlobalConfig();
            initLoadMachineConfig();
        }

        /// <summary>
        /// 更新系统启动进度内容
        /// </summary>
        /// <param name="message"></param>
        /// <param name="percent"></param>
        /// <param name="sleepms"></param>
        void updateLoadingMessage(string message, double percent, int sleepms = 400) {
            App.Store.Dispatch(new SysActions.SetLoadingMessage(message, percent));
            //if (!HmiConfig.IsDevUserEnv) {
            Thread.Sleep(sleepms);
            //}
        }

        /// <summary>
        /// 加载 GlobalConfig 和 初始化 xxHelper
        /// </summary>
        void initLoadGlobalConfig() {
            LoadGlobalConfig = App.Store.asyncAction<LoadActions.LoadGlobalConfig, bool>(
                async (dispatch, getState, instance) => {
                    dispatch(instance);
                    return await Task.Run(() => {
                        globalConfigLoad();
                        return true;
                    });
                });
        }

        /// <summary>
        /// 加载 MachineConfig，主要是初始化 CpmInfo
        /// </summary>
        void initLoadMachineConfig() {
            LoadMachineConfig = App.Store.asyncAction<LoadActions.LoadMachieConfig, bool>(
                async (dispatch, getState, instance) => {
                    dispatch(instance);
                    updateLoadingMessage("正在加载机台配置...", 0.4);
                    return await Task.Run(() => {
                        //为了调试方便机台配置文件在开发环境可以更改
                        if (HmiConfig.IsDevUserEnv) {
                            loadConfigBySetting();
                            //在生产环境机台配置文件地址和机台Ip是绑定在一起的
                        } else {
                            loadConfigByGlobal();
                        }
                        return true;
                    });
                });
        }

        /// <summary>
        /// LoggerHelper 和 Assets Helper 已经在 App.xaml.cs 中初始化了，所以这里不必要初始化了
        /// </summary>
        void globalConfigLoad() {
            updateLoadingMessage("正在准备系统资源文件", 0.15);
            Thread.Sleep(CmdOptions.GlobalOptions.WaitSec * 1000);

            updateLoadingMessage("正在检查系统启动环境...", 0.18);
            if (processIsStarted()) {
                var message = "系统重复启动异常";
                App.Store.Dispatch(new SysActions.SetLoadingMessage(message, 0.15));
                Thread.Sleep(1000);
                MessageBox.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                App.Store.Dispatch(new SysActions.ShutdownApp());
                return;
            }

            updateLoadingMessage("正在初始化异常配置...", 0.20);
            ExceptionHelper.Init();

            updateLoadingMessage("正在加载系统配置...", 0.23);
            GlobalConfig.Load(YUtil.GetAbsolutePath(".\\Profiles\\Global.xls"));

            updateLoadingMessage("正在初始化 Sqlite...", 0.25);
            SqliteHelper.Init(HmiConfig.SqlitePath);

            updateLoadingMessage("正在初始化 ActiveMq...", 0.28);
            ActiveMqHelper.Init(HmiConfig.MqConn, HmiConfig.MqUserName, HmiConfig.MqUserPwd);

            updateLoadingMessage("正在初始化 MongoDb...", 0.30);
            MongoHelper.Init(HmiConfig.MongoConn);

            updateLoadingMessage("正在初始化 InfluxDb...", 0.33);
            InfluxDbHelper.Init($"http://{HmiConfig.InfluxDbIp}:8086", HmiConfig.InfluxCpmDbName);

            updateLoadingMessage("正在同步时间...", 0.35);
            syncTime(!HmiConfig.IsDevUserEnv);

            Logger.Debug("当前操作系统：" + YUtil.GetOsVersion());
            Logger.Debug("当前版本：" + YUtil.GetAppVersion(Assembly.GetExecutingAssembly()));
            Logger.Debug("是否为开发环境：" + HmiConfig.IsDevUserEnv);
            Logger.Debug("浮点精度：" + HmiConfig.MathRound);
        }

        /// <summary>
        /// 通过 Sqlite 中的设置数据来加载配置
        /// </summary>
        private void loadConfigBySetting() {
            using (var ctx = SqliteHelper.CreateSqliteService()) {
                var setting = ctx.Settings.ToList().LastOrDefault();
                if (setting == null) {
                    App.Store.Dispatch(new SysActions.ShowSettingView());
                } else {
                    try {
                        MachineConfig.Load(setting.MachineXlsPath);
                        checkConfig();
                        afterConfigLoaded();
                    } catch (Exception e) {
                        App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                            Title = "配置出错",
                            Content = e.Message
                        }));
                        Logger.Error($"程序配置有误", e);
                        App.Store.Dispatch(new SysActions.ShowSettingView());
                    }
                }
            }
        }

        /// <summary>
        /// 通过 Global.xls中预定义的数据来加载配置
        /// </summary>
        void loadConfigByGlobal() {
            try {
                MachineConfig.LoadFromGlobal();
                checkConfig();
                afterConfigLoaded();
            } catch (Exception e) {
                Logger.Error("程序配置出错", e);
                App.Store.Dispatch(new SysActions.ShowNotification(new SysNotificationMsg() {
                    Title = "配置出错",
                    Content = e.Message
                }));
                var message = "机台配置文件出错，请检查网络连接";
                updateLoadingMessage(message, 0.5, 0);
                MessageBox.Show(message, "网络异常", MessageBoxButton.OK, MessageBoxImage.None);
                App.Store.Dispatch(new SysActions.ShutdownApp());
            }
        }


        /// <summary>
        /// 配置文件加载成功之后执行的一些初始化
        /// </summary>
        async void afterConfigLoaded() {
            //== 初始化部分State
            updateLoadingMessage("正在初始化系统核心...", 0.5);
            App.Store.Dispatch(new ViewStoreActions.Init());
            App.Store.Dispatch(new CpmActions.Init());
            App.Store.Dispatch(new AlarmActions.Init());
            App.Store.Dispatch(new OeeActions.Init());
            App.Store.Dispatch(new DMesActions.Init());
            App.Store.Dispatch(new DpmActions.Init());

            var sysEffects = UnityIocService.ResolveDepend<SysEffects>();
            var cpmEffects = UnityIocService.ResolveDepend<CpmEffects>();
            var mqEffects = UnityIocService.ResolveDepend<MqEffects>();

            //<fixme>触发条件执行 SysActions.Restart()，这里会卡死</fixme>
            //fix: 2018-1-26
            //用超时来提醒用户，该重启程序为关闭程序
            updateLoadingMessage("正在连接服务器...", 0.55);
            var task = Task.Run(() => {
                mqEffects.Start();
            });
            //更新连接服务器的进度
            double p = 0.55;
            bool isMqEffectsStarted = false;
            Timer updateTimer = null;
            updateTimer = YUtil.SetInterval(500, () => {
                p += 0.01;
                updateLoadingMessage("正在连接服务器...", p, 0);
                Logger.Debug("正在连接服务器..." + p.ToString("P1"));
                if (isMqEffectsStarted || p > 0.64) {
                    YUtil.ClearTimeout(updateTimer);
                }
            });
            isMqEffectsStarted = Task.WaitAll(new[] { task }, 10000);
            if (!isMqEffectsStarted) {
                updateLoadingMessage("连接服务器超时...", 0.6);
                closeAppAfterSec(10, 0.6, "连接服务器超时");
                return;
            }

            UnityIocService.ResolveDepend<DMesCore>().Init();
            UnityIocService.ResolveDepend<AlarmCore>().Init();
            UnityIocService.ResolveDepend<CpmCore>().Init();
            UnityIocService.ResolveDepend<OeeCore>().Init();
            UnityIocService.ResolveDepend<DpmCore>().Init();

            updateLoadingMessage("正在启动调度器...", 0.65);
            await UnityIocService.ResolveDepend<SchCore>().Init();

            //Http 命令解析
            updateLoadingMessage("正在启动系统核心...", 0.7);
            var starHttpSystem = App.Store.Dispatch(sysEffects.StartHttpSystem(new SysActions.StartHttpSystem($"http://+:{HmiConfig.CmdHttpPort}/")));

            //参数采集服务
            updateLoadingMessage("正在启动系统核心...", 0.75);
            var startCpmServer = App.Store.Dispatch(cpmEffects.StartServer(new CpmActions.StartServer(HmiConfig.CpmTcpIp, HmiConfig.CpmTcpPort)));

            //监听 Mq
            updateLoadingMessage("正在启动系统核心...", 0.8);
            Dictionary<string, Task<bool>> startListenMqDict = new Dictionary<string, Task<bool>>();
            foreach (var pair in MachineConfig.MachineDict) {
                //监听排产任务
                var stQueueName = @"QUEUE_" + pair.Key;
                var stTask = App.Store.Dispatch(mqEffects.StartListenSchTask(new MqActions.StartListenSchTask(pair.Key, stQueueName)));
                startListenMqDict[stQueueName] = stTask;
                //监听来料
                var smQueueName = $@"JUDGE_MATER_{pair.Key}";
                var smTask = App.Store.Dispatch(mqEffects.StartListenScanMaterial(new MqActions.StartListenScanMaterial(pair.Key, smQueueName)));
                startListenMqDict[smQueueName] = smTask;
            }

            //监听人员打卡
            updateLoadingMessage("正在启动系统核心...", 0.85);
            var empRfidTask = App.Store.Dispatch(mqEffects.StartListenEmpRfid(new MqActions.StartListenEmpRfid(HmiConfig.TopicEmpRfid)));
            startListenMqDict["rfidEmpTask"] = empRfidTask;

            //监听轴号卡
            updateLoadingMessage("正在启动系统核心...", 0.9);
            var axisRfidTask = App.Store.Dispatch(mqEffects.StartListenAxisRfid(new MqActions.StartListenAxisRfid(HmiConfig.TopicListenHandSet)));
            startListenMqDict["rfidAxisTask"] = axisRfidTask;

            updateLoadingMessage("正在启动系统核心...", 0.95);
            var tasks = new List<Task<bool>>() { starHttpSystem, startCpmServer };
            tasks.AddRange(startListenMqDict.Values);
            //检查各项任务启动情况
            await Task.Run(() => {
                //等等所有任务完成
                var isStartedOk = Task.WaitAll(tasks.ToArray(), 30000);
                if (!isStartedOk) {
                    var message = "系统核心启动超时，请检查网络连接";
                    updateLoadingMessage(message, 0.95);
                    closeAppAfterSec(10, 0.95, "系统核心启动超时");
                    return;
                }
                //是否启动完成Cpm服务
                var isCpmServer = startCpmServer.Result;
                if (!isCpmServer) {
                    var message = "参数采集核心启动失败";
                    updateLoadingMessage(message, 0.95, 0);
                    return;
                }
                //是否启动完成Http解析系统
                var isHttpSystem = starHttpSystem.Result;
                if (!isHttpSystem) {
                    var message = "Http 核心启动失败";
                    updateLoadingMessage(message, 0.95, 0);
                    return;
                }
                //是否完成监听Mq
                foreach (var pair in startListenMqDict) {
                    var isStartListenMq = pair.Value.Result;
                    var mqKey = pair.Key.ToUpper();
                    if (!isStartListenMq) {
                        string failedMessage = string.Empty;
                        if (mqKey.Contains("QUEUE")) {
                            failedMessage = $"监听Mq 排产队列 {pair.Key} 失败，请检查";
                        } else if (mqKey.Contains("JUDGE_MATER")) {
                            failedMessage = $"监听Mq 扫描来料队列 {pair.Key} 失败，请检查";
                        } else if (mqKey.Contains("RFIDEMP")) {
                            failedMessage = $"监听mq 人员打卡 数据失败，请检查";
                        } else if (mqKey.Contains("RFIDAXIS")) {
                            failedMessage = $"监听Mq 线盘卡失败，请检查";
                        }
                        if (!string.IsNullOrEmpty(failedMessage)) {
                            updateLoadingMessage(failedMessage, 0.95, 0);
                            return;
                        }
                    }
                }
                var percent = 0.95;
                YUtil.SetInterval(300, t => {
                    percent += 0.01;
                    updateLoadingMessage("系统核心启动完毕，正在渲染界面...", percent, 0);
                    if (t == 5 || percent >= 1) {
                        App.Store.Dispatch(new SysActions.AppInitCompleted());
                    }
                }, 5);

            });
        }
        /// <summary>
        /// 与服务器同步时间
        /// </summary>
        private void syncTime(bool canSync) {
            //非开发环境才同步时间
            if (canSync) {
                Task.Run(() => {
                    try {
                        //获取服务器时间
                        var ntpTime = YUtil.GetNtpTime(HmiConfig.NtpIp);
                        //时间差超过10秒才同步时间
                        if (Math.Abs((DateTime.Now - ntpTime).TotalSeconds) > 10) {
                            YUtil.SetLoadTimeByDateTime(ntpTime);
                        }
                        Logger.Info($"同步时间成功: {ntpTime}");
                    } catch (Exception e) {
                        Logger.Error("获取服务器时间失败", e);
                    }
                });
            }
        }

        /// <summary>
        /// 检测进程是否存在，存在则显示已有的进程否则则关闭程序
        /// <href>https://www.cnblogs.com/zhili/p/OnlyInstance.html</href>
        /// </summary>
        static bool processIsStarted() {
            Process currentproc = Process.GetCurrentProcess();
            Process[] processcollection = Process.GetProcessesByName(currentproc.ProcessName.Replace(".vshost", string.Empty));
            if (processcollection.Length > 1) {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 程序将在 totalSec 秒后自动关闭
        /// </summary>
        void closeAppAfterSec(int totalSec, double percent, string message = "程序启动超时") {
            var ms = totalSec * 1000;
            YUtil.SetInterval(1000, t => {
                var wait = totalSec - t;
                var waitMessage = $"{message}，将在 {wait} 秒后关闭";
                updateLoadingMessage(waitMessage, percent, 0);
                if (wait <= 0) {
                    App.Store.Dispatch(new SysActions.ShutdownApp());
                }
            }, totalSec);
        }

        /// <summary>
        /// 检查配置
        /// </summary>
        void checkConfig() {

        }
    }
}