﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OgameSkaner.Model
{
    public class ApplicationLogger
    {
        private string _logFileName;
        private LogFileType _logFiletype;
        public ApplicationLogger(string logFileName)
        {
            _logFileName = logFileName;
        }

        public ApplicationLogger(LogFileType logFileType)
        {
            _logFiletype = logFileType;
            GenerateDefaultFileName();
        }

        private void GenerateDefaultFileName()
        {
            switch (_logFiletype)
            {
                case LogFileType.errorLog:
                    _logFileName = "ErrorLog.txt";
                    return;
                case LogFileType.requestLog:
                    _logFileName = "requestLog.txt";
                    return;
                case LogFileType.responseLog:
                    _logFileName = "responseLog.txt";
                    return;
                case LogFileType.timeLog:
                    _logFileName = "timeLog.txt";
                    return;

            }

        }

        public void AddLog(string logText)
        {
            using (var sw = File.AppendText(_logFileName))
            {
                sw.WriteLine("");
                sw.WriteLine(
                    "<---------------------------->" + DateTime.Now.ToString() + "<---------------------------->");

                sw.WriteLine(logText);
                sw.Close();
            }
        }
    }
}