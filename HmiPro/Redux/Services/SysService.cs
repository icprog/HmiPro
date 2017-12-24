﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FSLib.App.SimpleUpdater;
using HmiPro.Config;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Models;
using HmiPro.Redux.Reducers;
using Newtonsoft.Json;
using YCsharp.Service;

namespace HmiPro.Redux.Services {
    public class SysService {
        public Updater AppUpdater;
        public readonly LoggerService Logger;
        public HttpListener HttpListener;

        public IDictionary<string, Action<HttpListenerResponse>> HttpSystemCmdDict =
            new ConcurrentDictionary<string, Action<HttpListenerResponse>>();

        public SysService() {
            UnityIocService.AssertIsFirstInject(GetType());
            Logger = LoggerHelper.CreateLogger(GetType().ToString());
            initCmdExecers();
        }

        public Task<bool> StartHttpSystem(SysActions.StartHttpSystem startHttpSystem) {
            return Task.Run(() => {
                HttpListener = new HttpListener();
                //Logger.Debug("http 监听地址 " + startHttpSystem.Url);
                HttpListener.Prefixes.Add(startHttpSystem.Url);
                return Task.Run(() => {
                    try {
                        HttpListener.Start();
                        HttpListener.BeginGetContext(processHttpContext, null);
                        return true;
                    } catch (Exception e) {
                        return false;
                    }
                });
            });

        }

        /// <summary>
        /// 指定命令的执行者
        /// </summary>
        private void initCmdExecers() {
            HttpSystemCmdDict["update-app"] = execUpdateApp;
            HttpSystemCmdDict["get-state"] = execGetState;
        }


        /// <summary>
        /// http相关处理
        /// </summary>
        /// <param name="ar"></param>
        private void processHttpContext(IAsyncResult ar) {
            var context = HttpListener.EndGetContext(ar);
            HttpListener.BeginGetContext(processHttpContext, null);
            var response = context.Response;
            response.AddHeader("Server", "Http System For HmiPro");
            var request = context.Request;
            var path = request.Url.LocalPath;
            if (path.StartsWith("/") || path.StartsWith("\\"))
                path = path.Substring(1);
            var visit = path.Split(new char[] { '/', '\\' }, 2);
            var cmd = "";
            if (visit.Length > 0) {
                cmd = visit[0].ToLower();
            }
            response.ContentType = "application/x-www-form-urlencoded;charset=utf-8";
            Logger.Info($"Http接受到命令：{cmd}", false);
            if (HttpSystemCmdDict.TryGetValue(cmd, out var exec)) {
                exec(response);
            } else {
                outResponse(response, new HttpSystemRest() { DebugMessage = $"未知命令：{cmd}" });
            }
        }


        private void execGetState(HttpListenerResponse responnse) {
            var rest = new HttpSystemRest();
            rest.Data = AppState.ExectedActions;
            rest.Machine = MachineConfig.AllMachineName;
            rest.Message = "获取程序状态成功";
            outResponse(responnse, rest);
        }


        /// <summary>
        /// 程序检查自动更新
        /// </summary>
        /// <param name="response"></param>
        private void execUpdateApp(HttpListenerResponse response) {
            bool hasUpdate = CheckUpdate();
            var rest = new HttpSystemRest();
            if (hasUpdate) {
                rest.Message = "检查到更新";
                rest.Code = 0;
            } else {
                rest.Message = "未能检查到更新";
                rest.Code = 0;
            }
            if (hasUpdate) {
                StartUpdate();
            }
            outResponse(response, rest);
        }

        /// <summary>
        /// 向http客户端返回数据
        /// </summary>
        /// <param name="response"></param>
        /// <param name="rest"></param>
        private void outResponse(HttpListenerResponse response, HttpSystemRest rest) {
            using (var stream = response.OutputStream) {
                var result = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(rest));
                stream.Write(result, 0, result.Length);
            }
        }
        /// <summary>
        /// 检查是否存在更新
        /// </summary>
        public bool CheckUpdate() {
            if (AppUpdater == null) {
                AppUpdater = Updater.CreateUpdaterInstance(HmiConfig.UpdateUrl, "update_c.xml");
            }
            var result = AppUpdater.CheckUpdateSync();
            return result.HasUpdate;
        }

        /// <summary>
        /// 执行更新
        /// </summary>
        public void StartUpdate() {
            AppUpdater.StartExternalUpdater();
        }
    }
}