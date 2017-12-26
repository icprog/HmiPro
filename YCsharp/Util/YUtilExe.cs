﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace YCsharp.Util {
    /// <summary>
    /// 对电脑的操作部分，关机，注销，关闭显示器等等
    /// 都是调用其它exe文件
    /// </summary>
    public static partial class YUtil {
        [DllImport("user32")]
        public static extern bool ExitWindowsEx(uint uFlags, uint dwReason);
        [DllImport("user32")]
        public static extern void LockWorkStation();
        [DllImport("user32")]
        public static extern int SendMessage(int hWnd, int hMsg, int wParam, int lParam);
        public enum MonitorState {
            MonitorStateOn = -1,
            MonitorStateOff = 2,
            MonitorStateStandBy = 1
        }
        /// <summary>
        /// 关机
        /// </summary>
        public static void ShutDown() {
            try {
                System.Diagnostics.ProcessStartInfo startinfo = new System.Diagnostics.ProcessStartInfo("shutdown.exe", "-s -t 00");
                System.Diagnostics.Process.Start(startinfo);
            } catch { }
        }
        /// <summary>
        /// 重启
        /// </summary>
        public static void Restart() {
            try {
                System.Diagnostics.ProcessStartInfo startinfo = new System.Diagnostics.ProcessStartInfo("shutdown.exe", "-r -t 00");
                System.Diagnostics.Process.Start(startinfo);
            } catch { }
        }
        /// <summary>
        /// 注销
        /// </summary>
        public static void LogOff() {
            try {
                ExitWindowsEx(0, 0);
            } catch { }
        }
        /// <summary>
        /// 锁屏
        /// </summary>
        public static void LockPC() {
            try {
                LockWorkStation();
            } catch { }
        }

        /// <summary>
        /// 打开显示器
        /// </summary>
        /// <param name="state"></param>
        public static void SetMonitorInState(MonitorState state) {
            SendMessage(0xFFFF, 0x112, 0xF170, (int)state);
        }

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="exePath">接受命令的可执行文件</param>
        /// <param name="cmd">命令</param>
        public static void ExecCmd(string exePath, string cmd) {
            try {
                System.Diagnostics.ProcessStartInfo startinfo = new System.Diagnostics.ProcessStartInfo(exePath, cmd);
                System.Diagnostics.Process.Start(startinfo);
            } catch {
                Console.WriteLine("执行命令 " + exePath + " " + cmd + " 异常");
            }
        }
        /// <summary>
        /// 通过 NirCmd调用的方式关闭显示器
        /// </summary>
        /// <param name="nirCmdPath"></param>
        public static void CloseScreenByNirCmd(string nirCmdPath) {
            ExecCmd(nirCmdPath, "monitor off");
        }

        /// <summary>
        /// 通过NirCmd调用的方式打开显示器
        /// </summary>
        /// <param name="nirCmdPath"></param>
        public static void OpenScreenByNirCmmd(string nirCmdPath) {
            ExecCmd(nirCmdPath, "monitor on");
        }
    }
}