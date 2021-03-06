﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using HmiPro.Annotations;
using HmiPro.Config;
using HmiPro.Config.Models;
using MongoDB.Bson;
using Newtonsoft.Json;
using YCsharp.Model.Procotol.SmParam;
using YCsharp.Util;

namespace HmiPro.Redux.Models {
    /// <summary>
    /// 预定义的参数地址
    /// </summary>
    public static class DefinedParamCode {
        //放线Rfid
        public static readonly int StartAxisRfid = 0x3e8;
        //收线Rfid
        public static readonly int EndAxisRfid = 0x3e9;
        //人员Rfid
        public static readonly int EmpRfid = 0x3ea;

        //Oee
        public static readonly int Oee = -100;
        public static readonly int OeeSpeed = -101;
        public static readonly int OeeTime = -102;
        public static readonly int OeeQuality = -103;
        //机台运行时间
        public static readonly int RunTime = -104;
        //机台停机时间
        public static readonly int StopTime = -105;
        //当班时间
        public static readonly int DutyTime = -106;
        //Od值
        public static readonly int Od = -107;
        //记米
        public static readonly int NoteMeter = -108;

        public static bool IsRfidParam(int paramCode) {
            return paramCode == StartAxisRfid || paramCode == EndAxisRfid || paramCode == EmpRfid || paramCode == 1002;
        }
    }

    /// <summary>
    /// 采集的参数模型
    /// <author>ychost</author>
    /// <date>2017-12-19</date>
    /// </summary>
    public class Cpm : INotifyPropertyChanged {
        //数据要保存在mongo中，所以必须有id才可以
        [JsonIgnore]
        public ObjectId Id { get; set; }
        public int Code;
        //参数值类型，可能是浮点，可能是rfid，可能是普通字符串 
        public SmParamType ValueType { get; set; } = SmParamType.Unknown;

        private object value;
        /// 兼容性好
        public object Value {
            get => value;
            set {
                if (this.value != value) {
                    this.value = value;
                    OnPropertyChanged(nameof(Value));
                }
            }
        }

        private string ip;

        /// <summary>
        /// 这个包的来源 ip
        /// </summary>
        public string Ip {
            get { return ip; }
            set { ip = value; }
        }


        public float FloatValue => GetFloatVal();
        /// <summary>
        /// 只保留指定字符个数的值
        /// </summary>
        public object NameRest => Name.Truncate(5);

        /// <summary>
        /// 获取浮点值，直接强转 (float)value会出问题
        /// </summary>
        /// <returns></returns>
        public float GetFloatVal() {
            if (ValueType != SmParamType.Signal) {
                throw new Exception($"参数 {Name} 值 {Value} 的类型是 {ValueType}，不能强转 float");
            }

            return float.Parse(value.ToString());
        }

        private DateTime pickTime = DateTime.Now;
        //采集时间
        public DateTime PickTime {
            get => pickTime;
            set {
                if (pickTime != value) {
                    pickTime = value;
                    OnPropertyChanged(nameof(PickTime));
                }
            }
        }

        //采集时间戳，毫秒级别
        public Int64 PickTimeStampMs => YUtil.GetUtcTimestampMs(PickTime);
        //参数名字
        public string Name { get; set; }
        /// <summary>
        /// 单位
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 是否异常
        /// </summary>
        public bool IsException { get; set; } = false;

        public void Update(object val, SmParamType valueType, DateTime pickTime) {
            this.Value = val;
            this.ValueType = valueType;
            this.PickTime = pickTime;
        }

        public static List<Cpm> ConvertBySmModel(string code, SmModel sm, string ip) {
            List<Cpm> cpms = new List<Cpm>();
            var machine = MachineConfig.MachineDict[code];
            if (machine == null) {
                return cpms;
            }
            foreach (var p in sm?.SmParams) {
                //过滤掉未配置的参数
                if (machine.CodeToAllCpmDict.ContainsKey(p.ParamCode)) {
                    Cpm cpm = new Cpm();
                    //编码
                    cpm.Code = p.ParamCode;
                    //从字典中获取名称
                    machine.CodeToAllCpmDict.TryGetValue(p.ParamCode, out var cpmmInfo);
                    if (cpmmInfo != null) {
                        cpm.Name = cpmmInfo.Name;
                    }
                    cpm.ValueType = p.ParamType;
                    //浮点参数
                    if (p.ParamType == SmParamType.Signal) {
                        try {
                            cpm.Value = p.GetSignalData();
                        } catch (Exception e) {
                            continue;
                        }
                        //转义
                        if (machine.CodeToAllCpmDict[cpm.Code].MethodName == CpmInfoMethodName.Escape) {
                            //如果在转义字典里面找不到，就显示传过来的值
                            var escapeStrs = machine.CodeToAllCpmDict[cpm.Code].MethodParamStrs;
                            for (int i = 0; i < escapeStrs.Count; i += 2) {
                                if (p.GetSignalData().ToString() == escapeStrs[i]) {
                                    cpm.Value = escapeStrs[i + 1];
                                    cpm.ValueType = SmParamType.String;
                                }
                            }
                        }
                    } else if (p.ParamType == SmParamType.String) {
                        //Rfid卡
                        if (DefinedParamCode.IsRfidParam(p.ParamCode)) {
                            cpm.ValueType = SmParamType.StrRfid;
                            cpm.Value = p.GetStrData();
                        }
                    } else if (p.ParamType == SmParamType.SingleComStatus) {
                        cpm.value = p.GetSingleComStatus();
                    }
                    //暂时对如下数据类型不做处理
                    if (cpm.ValueType != SmParamType.MultiComStatus && cpm.ValueType != SmParamType.Unknown) {
                        cpm.Ip = ip;
                        cpms.Add(cpm);
                    }
                }
            }

            return cpms;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
