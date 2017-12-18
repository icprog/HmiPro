﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YCsharp.Service;

namespace HmiPro.Config.Models {

    /// <summary>
    /// 单个机台的配置
    /// </summary>
    public class Machine {
        public string Code { get; set; }
        public string[] CpmIps { get; set; }
        //编码：采集参数（所有）
        public IDictionary<int, CpmInfo> CodeToAllCpmDict = new Dictionary<int, CpmInfo>();
        //编码：采集参数（需要计算，如最大值）
        public IDictionary<int, CpmInfo> CodeToRelateCpmDict = new Dictionary<int, CpmInfo>();
        //编码：采集参数（直接获取）
        public IDictionary<int, CpmInfo> CodeToDirectCpmDict = new Dictionary<int, CpmInfo>();
        //逻辑：采集参数（直接获取记米，速度等需要逻辑运算的采集参数）
        public IDictionary<CpmInfoLogic, CpmInfo> LogicToCpmDict = new Dictionary<CpmInfoLogic, CpmInfo>();
        //编码：逻辑
        public IDictionary<int, CpmInfoLogic> CodeToLogicDict = new Dictionary<int, CpmInfoLogic>();
        //编码：[算法参数编码]
        public IDictionary<int, List<int>> CodeMethodDict = new Dictionary<int, List<int>>();
        //需要输送到 InfluxDb 中德参数
        public IDictionary<int, CpmInfo> ToInfluxDict = new Dictionary<int, CpmInfo>();

        /// <summary>
        /// 初始化机台属性，如机台编码，底层ip等等
        /// </summary>
        /// <param name="path"></param>
        /// <param name="sheetName"></param>
        public void InitCodeAndIp(string path, string sheetName) {
            var propDict = xlsToMachine(path, sheetName);
            Code = propDict[nameof(Code)].ToString();
            CpmIps = propDict[nameof(CpmIps)].ToString().Split('|');
        }

        IDictionary<string, object> xlsToMachine(string xlsPath, string sheetName) {
            IDictionary<string, object> dict = new Dictionary<string, object>();
            using (var xlsOp = new XlsService(xlsPath)) {
                DataTable dt = xlsOp.ExcelToDataTable(sheetName, true);
                foreach (DataRow row in dt.Rows) {
                    var prop = row["属性"].ToString();
                    var value = row["值"];
                    dict[prop] = value;
                }
            }
            return dict;
        }


        /// <summary>
        /// 初始化采集参数字典
        /// </summary>
        public void InitCpmDict(string path, string sheetName) {
            CpmLoader cpmLoader = new CpmLoader(path, sheetName);
            List<CpmInfo> cpms = cpmLoader.Load();
            cpms.ForEach(cpm => {
                //所有参数
                if (CodeToAllCpmDict.ContainsKey(cpm.Code)) {
                    throw new Exception($"参数编码 [{cpm.Code}] 重复了");
                }
                CodeToAllCpmDict[cpm.Code] = cpm;

                //计算参数
                if (cpmLoader.IsRelateMethod(cpm.MethodName)) {
                    CodeToRelateCpmDict[cpm.Code] = cpm;
                } else {
                    CodeToDirectCpmDict[cpm.Code] = cpm;
                }

                //逻辑参数
                if (cpm.Logic != null) {
                    if (LogicToCpmDict.ContainsKey(cpm.Logic.Value)) {
                        throw new Exception($"逻辑 [{cpm.Logic}] 重复了");
                    }
                    LogicToCpmDict[cpm.Logic.Value] = cpm;
                    CodeToLogicDict[cpm.Code] = cpm.Logic.Value;
                }

                //算法参数
                if (cpm.MethodParamInts?.Count > 0) {
                    var codeList = new List<int>();
                    CodeMethodDict[cpm.Code] = codeList;
                    cpm.MethodParamInts.ForEach(code => {
                        codeList.Add(code);
                    });
                }
                //所有数据都保存
                ToInfluxDict[cpm.Code] = cpm;
            });
            validCodeMethodDict();
            buildLogicDict();
        }


        /// <summary>
        /// 校验算法参数的编码是有效的
        /// </summary>
        void validCodeMethodDict() {
            foreach (var methodPair in CodeMethodDict) {
                methodPair.Value.ForEach(method => {
                    if (!CodeToAllCpmDict.ContainsKey(method)) {
                        throw new Exception($"算法参数编码 [{method}] 不存在");
                    }
                });
            }
        }

        void buildLogicDict() {
            if (LogicToCpmDict.ContainsKey(CpmInfoLogic.Speed)) {
                var cpm = LogicToCpmDict[CpmInfoLogic.Speed];
                //添加速度标准差
                if (!LogicToCpmDict.ContainsKey(CpmInfoLogic.SpeedStdDev)) {
                    var stdDev = new CpmInfo() {
                        Name = $"{cpm.Name}标准差",
                        MethodName = CpmInfoMethodName.StdDev,
                        MethodParamInts = new List<int>() { cpm.Code }
                    };
                    LogicToCpmDict[CpmInfoLogic.SpeedStdDev] = stdDev;
                }

                //添加速度导数
                if (!LogicToCpmDict.ContainsKey(CpmInfoLogic.SpeedDerivative)) {
                    var der = new CpmInfo() {
                        Name = $"{cpm.Name}导数",
                        MethodName = CpmInfoMethodName.Derivative,
                        MethodParamInts = new List<int>(cpm.Code)
                    };
                    LogicToCpmDict[CpmInfoLogic.SpeedDerivative] = der;
                }

            }
        }

    }
}
