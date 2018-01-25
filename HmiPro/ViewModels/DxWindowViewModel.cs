﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using DevExpress.Mvvm;
using DevExpress.Mvvm.UI;
using HmiPro.Config;
using HmiPro.Config.Models;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Cores;
using HmiPro.Redux.Effects;
using HmiPro.Redux.Models;
using HmiPro.Redux.Patches;
using HmiPro.Redux.Reducers;
using HmiPro.ViewModels.Sys;
using HmiPro.Views.Sys;
using Newtonsoft.Json;
using YCsharp.Service;
using FluentScheduler;
using HmiPro.Mocks;
using HmiPro.Redux.Services;
using YCsharp.Util;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace HmiPro.ViewModels {
    /// <summary>
    /// 程序窗体模型
    /// <date>2017-12-17</date>
    /// <author>ychost</author>
    /// </summary>
    public class DxWindowViewModel : ViewModelBase {
        /// <summary>
        /// 日志
        /// </summary>
        public readonly LoggerService Logger;
        /// <summary>
        /// 程序的全局的核心数据和事件存储
        /// </summary>
        public readonly StorePro<AppState> Store;
        /// <summary>
        /// 事件派发器
        /// </summary>
        public readonly IDictionary<string, Action<AppState, IAction>> actionsExecDict = new Dictionary<string, Action<AppState, IAction>>();

        private string marqueeText;
        /// <summary>
        /// 跑马灯文字内容信息，如果文字内容为空，则隐藏显示
        /// </summary>
        public string MarqueeText {
            get => marqueeText;
            set {
                if (marqueeText != value) {
                    marqueeText = value;
                    RaisePropertyChanged(nameof(MarqueeText));
                    if (string.IsNullOrEmpty(value)) {
                        MarqueeHiehgit = 0;
                    } else {
                        MarqueeHiehgit = 30;
                    }
                    RaisePropertyChanged(nameof(MarqueeHiehgit));
                }
            }
        }
        /// <summary>
        /// 页面加载事件命令
        /// </summary>
        ICommand onViewLoadedCommand; public ICommand OnViewLoadedCommand {
            get {
                if (onViewLoadedCommand == null)
                    onViewLoadedCommand = new DelegateCommand(OnViewLoaded);
                return onViewLoadedCommand;
            }
        }
        /// <summary>
        /// FormView 等 Modal 加载服务
        /// </summary>
        public virtual IDialogService DialogService {
            get { return GetService<IDialogService>(); }

        }
        /// <summary>
        /// UI 线程调度器，可自动切换到 UI 线程
        /// </summary>
        public virtual IDispatcherService DispatcherService => GetService<IDispatcherService>();
        /// <summary>
        /// 可显示右上角的通知内容
        /// </summary>
        public virtual INotificationService NotifyNotificationService => GetService<INotificationService>();
        /// <summary>
        /// 导航服务，注册在 MainWindows.xaml中
        /// </summary>
        public INavigationService NavigationService { get { return GetService<INavigationService>(); } }
        /// <summary>
        /// 跑马灯内容信息
        /// </summary>
        public IDictionary<string, string> MarqueeMessagesDict;
        /// <summary>
        /// 跑马灯用的是 SortedDictionary 不支持并发，所以要手工对 Add，Remove 上锁
        /// </summary>
        public object MarqueeLock = new object();

        /// <summary>
        /// 设置跑马灯高度，这里用高度而不用 Visibility 是因为 Visibility 设置成 Collpased 会导致 跑马灯效果失效
        /// </summary>
        public double MarqueeHiehgit { get; set; }
        /// <summary>
        /// 初始化日志、Store、定时器等等
        /// </summary>
        public DxWindowViewModel() {
            Logger = LoggerHelper.CreateLogger(GetType().ToString());
            Store = UnityIocService.ResolveDepend<StorePro<AppState>>();
            MarqueeMessagesDict = Store.GetState().SysState.MarqueeMessagesDict;
            //初始化事件派发
            actionsExecDict[SysActions.SHOW_NOTIFICATION] = doShowNotification;
            actionsExecDict[SysActions.SHOW_SETTING_VIEW] = doShowSettingView;
            actionsExecDict[OeeActions.UPDATE_OEE_PARTIAL_VALUE] = whenOeeUpdated;
            actionsExecDict[SysActions.APP_INIT_COMPLETED] = whenAppInitCompleted;
            actionsExecDict[SysActions.SHOW_FORM_VIEW] = doShowFormView;
            actionsExecDict[SysActions.ADD_MARQUEE_MESSAGE] = doAddMarqueeMessage;
            actionsExecDict[SysActions.DEL_MARQUEE_MESSAGE] = doDelMarqueeMessage;
            Store.Subscribe(actionsExecDict);

            //每一分钟检查一次与服务器的连接
            Task.Run(() => {
                YUtil.SetInterval(60000, () => {
                    checkNetwork(HmiConfig.InfluxDbIp);
                });
            });

            YUtil.SetInterval(2000, t => {
                App.Store.Dispatch(new SysActions.AddMarqueeMessage(t.ToString(), "测试" + t));
                if (t > 5) {
                    App.Store.Dispatch(new SysActions.DelMarqueeMessage((t - 5).ToString()));
                }
            },10);
        }

        /// <summary>
        /// 通过内容 Id 删除跑马灯里面对应的文字
        /// </summary>
        /// <param name="state"></param>
        /// <param name="action"></param>
        void doDelMarqueeMessage(AppState state, IAction action) {
            var marquee = (SysActions.DelMarqueeMessage)action;
            lock (MarqueeLock) {
                if (MarqueeMessagesDict.Remove(marquee.Id)) {
                    updateMarqueeMessages();
                }
            }
        }

        /// <summary>
        /// 添加跑马灯的文字内容，每个内容有唯一 Id 标识
        /// </summary>
        /// <param name="state"></param>
        /// <param name="action"></param>
        void doAddMarqueeMessage(AppState state, IAction action) {
            var marquee = (SysActions.AddMarqueeMessage)action;
            lock (MarqueeLock) {
                MarqueeMessagesDict[marquee.Id] = marquee.Message;
                updateMarqueeMessages();
            }
        }

        /// <summary>
        /// 更新跑马灯文字显示内容
        /// </summary>
        void updateMarqueeMessages() {
            StringBuilder stringBuilder = new StringBuilder();
            int i = 1;
            foreach (var pair in MarqueeMessagesDict) {
                stringBuilder.Append($"{i++}. {pair.Value}  ");
            }
            MarqueeText = stringBuilder.ToString();
        }

        /// <summary>
        /// 程序初始化完成
        /// 包括配置文件初始化成功
        /// Mq消息监听成功
        /// Cpm服务启动成功
        /// Http服务启动成功
        /// </summary>
        /// <param name="state"></param>
        /// <param name="action"></param>
        void whenAppInitCompleted(AppState state, IAction action) {
            //模拟动作
            if (CmdOptions.GlobalOptions.MockVal) {
                foreach (var pair in MachineConfig.MachineDict) {
                    var machineCode = pair.Key;
                    //Mocks.MockDispatchers.DispatchMockMqEmpRfid(machineCode);
                    //YUtil.SetTimeout(3000, () => {
                    //    Mocks.MockDispatchers.DispatchMockAlarm(33);
                    //});

                    //YUtil.SetTimeout(6000, () => {
                    //    Mocks.MockDispatchers.DispatchMqMockScanMaterial(machineCode);
                    //});

                    //YUtil.SetTimeout(7000, () => {
                    //    Mocks.MockDispatchers.DispatchMockMqEmpRfid(machineCode, MqRfidType.EmpStartMachine);
                    //    YUtil.SetTimeout(15000, () => {
                    //        Mocks.MockDispatchers.DispatchMockMqEmpRfid(machineCode, MqRfidType.EmpEndMachine);
                    //    });
                    //});

                    //YUtil.SetTimeout(3000, () => {
                    //    for (int i = 0; i < 1; i++) {
                    //        MockDispatchers.DispatchMockSchTask(machineCode, i);
                    //    }
                    //});
                }
            }
            //启动完毕则检查更新
            if (!HmiConfig.IsDevUserEnv) {
                Task.Run(() => {
                    var sysService = UnityIocService.ResolveDepend<SysService>();
                    if (sysService.CheckUpdate()) {
                        sysService.StartUpdate();
                    }
                });
            }
        }

        /// <summary>
        /// 检查与某个ip的连接状况，并显示在window顶部
        /// </summary>
        /// <param name="ip"></param>
        void checkNetwork(string ip) {
            Ping pingSender = new Ping();
            PingReply reply = pingSender.Send(ip, 1000);
            if (reply.Status != IPStatus.Success) {
                var message = $"与服务器 {ip} 连接断开，请联系管理员";
                App.Store.Dispatch(new SysActions.AddMarqueeMessage(SysActions.MARQUEE_PING_IP_FAILED + ip, message));
            } else {
                App.Store.Dispatch(new SysActions.DelMarqueeMessage(SysActions.MARQUEE_PING_IP_FAILED + ip));
            }
        }

        /// <summary>
        /// 打印计算的Oee
        /// </summary>
        /// <param name="state"></param>
        /// <param name="action"></param>
        void whenOeeUpdated(AppState state, IAction action) {
            var oeeAction = (OeeActions.UpdateOeePartialValue)action;
            //Logger.Debug($@"Oee 时间效率 {oeeAction.TimeEff ?? 1}, 速度效率：{oeeAction.SpeedEff ?? 1}，质量效率：{oeeAction.QualityEff ?? 1}", ConsoleColor.Yellow);
        }

        /// <summary>
        /// 显示 FormView，一般是用作让用户输入一些数据，布局采用的 DataLayoutControl
        /// </summary>
        /// <param name="state"></param>
        /// <param name="action"></param>
        void doShowFormView(AppState state, IAction action) {
            var sysAction = (SysActions.ShowFormView)action;
            //弹出键盘
            YUtil.CallOskAsync();
            DispatcherService.BeginInvoke(() => {
                JumFormView(sysAction.Title, sysAction.FormCtrls);
            });
        }

        /// <summary>
        /// 显示设置界面
        /// </summary>
        /// <param name="state"></param>
        /// <param name="action"></param>
        void doShowSettingView(AppState state, IAction action) {
            //fix: 2018-1-24
            //如果不换线程会阻塞其它线程
            //因为如果用户不点击确定或者取消，线程一直处于等待状态
            DispatcherService.BeginInvoke(() => {
                JumpAppSettingView("程序设置");
            });
        }

        /// <summary>
        /// 显示通知消息
        /// </summary>
        /// <param name="state"></param>
        /// <param name="action"></param>
        void doShowNotification(AppState state, IAction action) {
            var msg = ((SysActions.ShowNotification)action).Message;
            //两次相同通知时间间隔秒数>=MinGapSec 才能显示
            //默认都显示
            var key = "Title: " + msg.Title + " Content: " + msg.Content;
            if (msg.MinGapSec.HasValue) {
                if (SysNotificationMsg.NotifyTimeDict.TryGetValue(key, out var lastTime)) {
                    if ((DateTime.Now - lastTime).TotalSeconds < msg.MinGapSec.Value) {
                        return;
                    }
                }
            }
            SysNotificationMsg.NotifyTimeDict[key] = DateTime.Now;
            //保存消息日志
            var logDetail = "Title: " + msg.Title + "\t Content: " + msg.Content;
            if (!string.IsNullOrEmpty(msg.LogDetail)) {
                logDetail = msg.LogDetail;
            }
            Logger.Notify(logDetail);
            DispatcherService.BeginInvoke(() => {
                INotification notification = NotifyNotificationService.CreatePredefinedNotification(msg.Title, msg.Content, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                //Hmi没有播放声音设备
                if (HmiConfig.IsDevUserEnv) {
                    SystemSounds.Exclamation.Play();
                }
                notification.ShowAsync();
            });
        }

        /// <summary>
        /// 导航函数
        /// </summary>
        /// <param name="target"></param>
        public void Navigate(string target) {
            NavigationService.Navigate(target, null, this, true);
        }

        /// <summary>
        /// 程序启动后跳转到主页
        /// </summary>
        public void OnViewLoaded() {
            Navigate("HomeView");
        }

        /// <summary>
        /// 派发了用户点击「确定」或者「取消」的事件
        /// </summary>
        /// <param name="title"></param>
        /// <param name="formCtrls"></param>
        public void JumFormView(string title, object formCtrls) {
            UICommand okCmd = new UICommand() {
                Caption = "确定",
                IsCancel = false,
                IsDefault = false
            };
            UICommand cancelCmd = new UICommand() {
                Caption = "取消",
                IsCancel = true,
                IsDefault = true
            };
            var formViewModel = FormViewModel.Create(title, formCtrls);
            var resultCmd = DialogService.ShowDialog(new List<UICommand>() { okCmd, cancelCmd }, title, nameof(FormView),
                formViewModel);
            //派发事件，可根据 FormCtrls 的 Type 来确定逻辑
            if (resultCmd == okCmd) {
                App.Store.Dispatch(new SysActions.FormViewPressedOk(title, formViewModel.FormCtrls));
            } else {
                App.Store.Dispatch(new SysActions.FormViewPressedCancel(title, formViewModel.FormCtrls));
            }
        }

        /// <summary>
        /// 跳转到程序设置界面
        /// 比如配置读取出错等等
        /// </summary>
        /// <param name="title"></param>
        public void JumpAppSettingView(string title) {
            Setting setting = null;
            using (var ctx = SqliteHelper.CreateSqliteService()) {
                setting = ctx.Settings.OrderBy(s => s.Id).ToList().LastOrDefault();
                if (setting == null) {
                    setting = new Setting();
                }
            }

            var settingViewModel = SettingViewModel.Create(setting);
            UICommand okCommand = new UICommand() {
                Caption = "确定",
                IsCancel = false,
                IsDefault = false
            };
            UICommand cancelCommand = new UICommand() {
                Caption = "取消",
                IsCancel = true,
                IsDefault = true,
            };

            var resultCommand = DialogService.ShowDialog(new List<UICommand>() { okCommand, cancelCommand },
                title, nameof(SettingView), settingViewModel);
            if (resultCommand == okCommand) {
                try {
                    using (var ctx = SqliteHelper.CreateSqliteService()) {
                        ctx.Settings.Add(settingViewModel.Setting);
                        ctx.SaveChanges();
                    }
                    MessageBox.Show("配置成功，请重新启动软件", "配置成功", MessageBoxButton.OK, MessageBoxImage.None);
                    //广播消息出去
                    Store.Dispatch(new SysActions.ShutdownApp());

                    Application.Current.Dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Send);
                } catch (Exception e) {
                    Logger.Error("动态配置有误", e);
                    MessageBox.Show(e.Message, "配置有误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            } else if (resultCommand == cancelCommand) {

            }
        }
    }
}
