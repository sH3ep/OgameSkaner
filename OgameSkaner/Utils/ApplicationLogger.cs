using System;
using System.IO;
using OgameSkaner.Model;

namespace OgameSkaner.Utils
{
    public class ApplicationLogger
    {
        private string _logFileName;
        private readonly LogFileType _logFiletype;

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
                    "<---------------------------->" + DateTime.Now + "<---------------------------->");

                sw.WriteLine(logText);
                sw.Close();
            }
        }
    }
}