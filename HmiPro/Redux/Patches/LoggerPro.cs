﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HmiPro.Config;
using HmiPro.Helpers;
using HmiPro.Redux.Actions;
using HmiPro.Redux.Effects;
using HmiPro.Redux.Models;
using MongoDB.Bson;
using YCsharp.Service;
using YCsharp.Util;

namespace HmiPro.Redux.Patches {
    /// <summary>
    /// 扩展日志类，支持写入mongo中去
    /// <author>ychost</author>
    /// <date>2017-12-29</date>
    /// </summary>
    public static class LoggerPro {
        /// <summary>
        /// 将日志内容写入Mongo当中去
        /// </summary>
        public static void WriteToMogo(this LoggerService logger, LogDoc logDoc, string dbName, string collection) {
            var mongoClient = MongoHelper.GetMongoService();
            mongoClient.GetDatabase(dbName).GetCollection<MongoDoc>(collection).InsertOneAsync(logDoc);
        }

        /// <summary>
        /// 将错误日志写入数据库中
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="message"></param>
        /// <param name="dbName"></param>
        /// <param name="collection"></param>
        public static void ErrorWithDb(this LoggerService logger, string message, string dbName, string collection) {
            logger.ErrorWithDb(message, null, dbName, collection);
        }

        /// <summary>
        /// 将错误日志写入数据库中
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="message"></param>
        /// <param name="e"></param>
        /// <param name="dbName"></param>
        /// <param name="collection"></param>
        public static void ErrorWithDb(this LoggerService logger, string message, Exception e, string dbName, string collection) {
            logger.Error(message, e);
            var doc = new LogDoc() {
                Location = logger.DefaultLocation,
                Message = message,
                Exception = e,
                Level = "Error",
            };
            logger.WriteToMogo(doc, dbName, collection);
        }
    }

    /// <summary>
    /// 日志在mongo中的模型
    /// <author>ychost</author>
    /// <date>2017-12-29 </date>
    /// </summary>
    public class LogDoc : MongoDoc {
        public string Location { get; set; }
        public string Message { get; set; }
        public DateTime Time { get; set; }
        public string Level { get; set; }
        public Exception Exception { get; set; }
        public string Hmi { get; set; }

        public LogDoc() {
            Time = YUtil.DateTimeToUtcTime(DateTime.Now);
            Hmi = MachineConfig.HmiName;

        }
    }
}
