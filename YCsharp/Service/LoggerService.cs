﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YCsharp.Util;
using Console = System.Console;


namespace YCsharp.Service {


    /// <summary>
    /// 日志服务，可输出到文件
    /// <todo>输出到数据库</todo>
    /// <date>2017-09-30</date>
    /// <author>ychost</author>
    /// </summary>
    public class LoggerService {

        /// <summary>
        /// 上次保存日志到文件的时间
        /// </summary>
        static readonly IDictionary<string, DateTime> lastSaveFileTimeDict = new ConcurrentDictionary<string, DateTime>();
        static readonly IDictionary<string, DateTime> lastConsoleTimeDict = new ConcurrentDictionary<string, DateTime>();

        static LoggerService() {
            //定时清除超时的缓存
            YUtil.SetTimeout(3600, () => {
                foreach (var pair in lastSaveFileTimeDict) {
                    if ((DateTime.Now - pair.Value).TotalHours > 10) {
                        lastSaveFileTimeDict.Remove(pair.Key);
                    }
                }
                foreach (var pair in lastConsoleTimeDict) {
                    if ((DateTime.Now - pair.Value).TotalHours > 10) {
                        lastConsoleTimeDict.Remove(pair.Key);
                    }
                }
            });
        }

        /// <summary>
        /// 日志文件夹
        /// </summary>
        readonly string logFolder;

        /// <summary>
        /// 输出到控制台？,总开关
        /// </summary>
        public bool OutConsole = true;
        /// <summary>
        /// 输出到文件？,总开关
        /// </summary>
        public bool OutFile = true;

        public string DefaultLocation = "";

        private bool logPathIsExist = false;

        /// <summary>
        /// 构造函数需要日志文件夹
        /// </summary>
        /// <param name="logFolder"></param>
        public LoggerService(string logFolder) {
            this.logFolder = logFolder + @"\";
        }

        /// <summary>
        /// 常用调试信息
        /// </summary>
        /// <param name="message">输出内容</param>
        /// <param name="outFile">是否输出到温度，默认是</param>
        /// <param name="consoleColor">Console输出的颜色，默认白色</param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        public void Info(string message, bool outFile = true, ConsoleColor consoleColor = ConsoleColor.White, int outMinGapSec = 0) {
            var lineNum = YUtil.GetCurCodeLineNum(2);
            Console.ForegroundColor = consoleColor;
            Info(DefaultLocation + $"[{lineNum}]行", message, outFile, outMinGapSec);
            Console.ForegroundColor = ConsoleColor.White;
        }

        /// <summary>
        /// 带自定义位置的调试信息
        /// </summary>
        /// <param name="location">位置标识</param>
        /// <param name="message">输出内容</param>
        /// <param name="outFile">是否输出到文件</param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        public void Info(string location, string message, bool outFile, int outMinGapSec = 0) {
            var content = createLogContent(location, message, "info");
            consoleOut(content, outMinGapSec);
            if (outFile) {
                fileOut(content, "info", outMinGapSec);
            }
        }

        /// <summary>
        /// 系统通知消息，会存档
        /// </summary>
        /// <param name="message"></param>
        /// <param name="outFile"></param>
        /// <param name="consoleColor"></param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        public void Notify(string message, bool outFile = true, ConsoleColor consoleColor = ConsoleColor.White, int outMinGapSec = 0) {
            var lineNum = YUtil.GetCurCodeLineNum(2);
            Console.ForegroundColor = consoleColor;
            Notify(DefaultLocation + $"[{lineNum}]行", message, outFile, outMinGapSec);
            Console.ForegroundColor = ConsoleColor.White;
        }

        /// <summary>
        /// 系统通知消息
        /// </summary>
        /// <param name="location"></param>
        /// <param name="message"></param>
        /// <param name="outFile"></param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        public void Notify(string location, string message, bool outFile, int outMinGapSec = 0) {
            var content = createLogContent(location, message, "notify");
            consoleOut(content, outMinGapSec);
            if (outFile) {
                fileOut(content, "notify", outMinGapSec);
            }
        }

        /// <summary>
        /// 输出错误信息
        /// </summary>
        /// <param name="location"></param>
        /// <param name="message"></param>
        /// <param name="outMinGapSec">日志输出到文件最小时间间隔</param>
        public void Error(string location, string message, int outMinGapSec = 0) {
            var content = createLogContent(location, message, "error");
            Console.ForegroundColor = ConsoleColor.Red;
            consoleOut(content, outMinGapSec);
            Console.ForegroundColor = ConsoleColor.White;
            fileOut(content, "error", outMinGapSec);
        }

        public void Error(string location, string message, Exception e, int outMinGapSec = 0) {
            Error(location, $"{message} 原因：{e.Message}", outMinGapSec);
        }

        /// <summary>
        /// 常用api
        /// </summary>
        /// <param name="message"></param>
        /// <param name="e"></param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        public void Error(string message, Exception e, int outMinGapSec = 0) {
            var lineNum = YUtil.GetCurCodeLineNum(2);
            Error(DefaultLocation + $"[{lineNum}]行", $"{message} 原因：{e}", outMinGapSec);
        }
        /// <summary>
        /// 常用api
        /// </summary>
        /// <param name="message"></param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        public void Error(string message, int outMinGapSec = 0) {
            var lineNum = YUtil.GetCurCodeLineNum(2);
            Error(DefaultLocation + $"[{lineNum}]行", message, outMinGapSec);
        }

        /// <summary>
        /// 输出调试信息，只能在控制台输出
        /// </summary>
        /// <param name="message"></param>
        /// <param name="color"></param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        public void Debug(string message, ConsoleColor color = ConsoleColor.Green, int outMinGapSec = 0) {
            var content = createLogContent(DefaultLocation, message, "debug");
            Console.ForegroundColor = color;
            consoleOut(content, outMinGapSec);
            Console.ForegroundColor = ConsoleColor.White;
        }

        /// <summary>
        /// 输出警告信息
        /// </summary>
        /// <param name="location"></param>
        /// <param name="message"></param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        public void Warn(string location, string message, int outMinGapSec = 0) {
            Warn(location, message, true, outMinGapSec);
        }

        public void Warn(string message, bool outFile, int outMinGapSec = 0) {
            Warn(DefaultLocation, message, outFile, outMinGapSec);
        }

        public void Warn(string location, string message, bool outFile, int outMinGapSec = 0) {
            var content = createLogContent(location, message, "warn");
            Console.ForegroundColor = ConsoleColor.Yellow;
            consoleOut(content, outMinGapSec);
            Console.ForegroundColor = ConsoleColor.White;
            if (outFile) {
                fileOut(content, "warn");
            }
        }

        /// <summary>
        /// 常用api
        /// </summary>
        /// <param name="message"></param>
        public void Warn(string message) {
            var lineNum = YUtil.GetCurCodeLineNum(2);
            Warn(DefaultLocation + $"[{lineNum}]行", message);
        }
        /// <summary>
        /// 控制台输出
        /// </summary>
        /// <param name="content"></param>
        /// <param name="outMinGapSec">日志打印最小时间间隔秒数</param>
        void consoleOut(string content, int outMinGapSec) {
            var key = content.Split(new string[] { "信息：" }, StringSplitOptions.None)[1];
            if (!lastConsoleTimeDict.ContainsKey(key)) {
                lastConsoleTimeDict[key] = DateTime.MinValue;
            }
            if ((DateTime.Now - lastConsoleTimeDict[key]).TotalSeconds < outMinGapSec && outMinGapSec > 0) {
                return;
            }
            lastConsoleTimeDict[key] = DateTime.Now;
            if (OutConsole) {
                Console.Write(content);
            }
            logoutAction?.Invoke(content);
        }

        private static Action<string> logoutAction;

        /// <summary>
        /// 订阅日志输出动作，每个日志输出都会受到消息通知
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public static Action Subscribe(Action<string> action) {
            logoutAction += action;
            return () => {
                logoutAction -= action;
            };
        }

        public static readonly object FileOutLock = new object();

        /// <summary>
        /// 文件输出
        /// </summary> 
        /// <param name="content"></param>
        /// <param name="mark"></param>
        /// <param name="outMinGapSec">上次记录与此次记录时间差大于该间隙才能输出到文件</param>
        void fileOut(string content, string mark, int outMinGapSec = 0) {
            //var key = content + mark;
            var key = content.Split(new string[] { "信息：" }, StringSplitOptions.None)[1];
            if (!lastSaveFileTimeDict.ContainsKey(key)) {
                lastSaveFileTimeDict[key] = DateTime.MinValue;
            }
            if ((DateTime.Now - lastSaveFileTimeDict[key]).TotalSeconds < outMinGapSec && outMinGapSec > 0) {
                return;
            }
            lastSaveFileTimeDict[key] = DateTime.Now;
            lock (FileOutLock) {
                //总开关
                if (OutFile) {
                    var path = buildLogFilePath(mark);
                    //日志文件夹不存在则创建
                    if (!logPathIsExist) {
                        var file = new FileInfo(path);
                        var di = file.Directory;
                        if (!di.Exists) {
                            di.Create();
                        }
                        logPathIsExist = true;
                    }
                    //往文件输出日志
                    using (FileStream logFile = new FileStream(path, FileMode.OpenOrCreate,
                        FileAccess.Write, FileShare.Write)) {
                        logFile.Seek(0, SeekOrigin.End);
                        var bytes = Encoding.Default.GetBytes(content);
                        logFile.Write(bytes, 0, bytes.Length);
                    }
                }
            }
        }


        /// <summary>
        /// 获取日志文件路径
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns></returns>
        private string buildLogFilePath(string prefix) {
            var time = DateTime.Now.ToString("yyyyMM");
            return logFolder + $"{prefix}-{time}.log.txt";
        }

        /// <summary>
        /// 生成日志内容
        /// </summary>
        /// <param name="location"></param>
        /// <param name="message"></param>
        /// <param name="mark"></param>
        /// <returns></returns>
        string createLogContent(string location, string message, string mark) {
            return $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} [{mark}]: 线程：[{Thread.CurrentThread.ManagedThreadId.ToString("00")}]  位置：{location} \r\n 信息：{message}\r\n\r\n";
        }

    }
}
