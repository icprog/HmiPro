﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HmiPro.Config.Models;
using YCsharp.Service;

namespace HmiPro.Config {
    /// <summary>
    /// 机台配置
    /// <date>2017-12-18</date>
    /// <author>ychost</author>
    /// </summary>
    public static class MachineConfig {
        /// <summary>
        /// 所有机台字典，键为机台编码
        /// </summary>
        public static IDictionary<string, Machine> MachineDict;
        public static IDictionary<string, string> IpToMachineCodeDict;
        public static IDictionary<string, List<string>> MachineCodeToIpsDict;
        public static string AllMachineName;

        /// <summary>
        /// 每个机台的报警ip字典
        /// </summary>
        public static IDictionary<string, string> AlarmIpDict;

        public static void Load(string path) {
            MachineDict = new Dictionary<string, Machine>();
            IpToMachineCodeDict = new Dictionary<string, string>();
            AlarmIpDict = new Dictionary<string, string>();
            MachineCodeToIpsDict = new Dictionary<string, List<string>>();
            var codes = Path.GetFileNameWithoutExtension(path).Split('_');
            AllMachineName = Path.GetFileNameWithoutExtension(path);
            foreach (var code in codes) {
                var machine = new Machine();
                machine.Code = code;
                machine.InitCpmDict(path, $"{code}_采集参数");
                machine.InitCodeAndIp(path, $"{code}_机台属性");
                MachineDict[code] = machine;
                foreach (var ip in machine.CpmIps) {
                    IpToMachineCodeDict[ip] = code;
                    if (ip.EndsWith("100")) {
                        AlarmIpDict[code] = ip;
                    }
                }
                MachineCodeToIpsDict[code] = machine.CpmIps.ToList();
            }
        }
    }
}
