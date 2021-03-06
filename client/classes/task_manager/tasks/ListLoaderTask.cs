﻿#region Usage

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using com.jds.AWLauncher.classes.forms;
using com.jds.AWLauncher.classes.games.propertyes;
using com.jds.AWLauncher.classes.language.enums;
using com.jds.AWLauncher.classes.listloader;
using com.jds.AWLauncher.classes.listloader.enums;
using com.jds.AWLauncher.classes.zip;
using log4net;

#endregion

namespace com.jds.AWLauncher.classes.task_manager.tasks
{
    public class ListLoaderTask : AbstractTask
    {
        #region Constructor & Variables

        private static readonly ILog _log = LogManager.GetLogger(typeof (ListLoaderTask));

        private readonly Dictionary<ListFileType, LinkedList<ListFile>> _list =
            new Dictionary<ListFileType, LinkedList<ListFile>>();

        private readonly GameProperty _property;
        private readonly WebClient _webClient = new WebClient();
        private String _toString;
        private Thread _thread;

        public ListLoaderTask(GameProperty p)
        {
            _property = p;

            Status = Status.FREE;

            _list.Add(ListFileType.CRITICAL, new LinkedList<ListFile>());
            _list.Add(ListFileType.NORMAL, new LinkedList<ListFile>());
            _list.Add(ListFileType.DELETE, new LinkedList<ListFile>());

            _webClient.DownloadDataCompleted += client_DownloadDataCompleted;
            _webClient.DownloadProgressChanged += _webClient_DownloadProgressChanged;
        }

        #endregion

        #region Берет  с инета список

        public override void Run()
        {
            if (_property == null || _property.Panel == null)
            {
                throw new NullReferenceException("Panel or GProperty is null!!!!!!. WTF???");
            }
            _toString = "Get List Thread " + _property.GetType().Name + ":" + GetHashCode();
            _thread = new Thread(ListDownloadThread)
                          {
                              Name = _toString
                          };

            _thread.Start();
        }

        private void ListDownloadThread()
        {
            if (!_property.isEnable())
            {
                GoEnd(WordEnum.GAME_IS_DISABLED);
                return;
            }

            if (Status != Status.FREE || _webClient.IsBusy)
            {
                return;
            }

            MainForm.Instance.UpdateStatusLabel(WordEnum.STARTING_DOWNLOAD_LIST);
            MainForm.Instance.SetMainFormState(MainFormState.CHECKING);

            Status = Status.DOWNLOAD;

            _webClient.DownloadDataAsync(new Uri(_property.listURL() + "/list.zip"));
        }

        private void client_DownloadDataCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                GoEnd(WordEnum.CANCEL_BY_USER);
                return;
            }

            if (e.Error != null)
            {
                if (e.Error is WebException)
                {
                    GoEnd(e.Error.Message);

                    if(_log.IsDebugEnabled)
                    {
                        _log.Info("Exception: while downloading list: " + e.Error.Message, e.Error);
                    }
                    return;
                }
                else
                {
                    _log.Info("Exception: while downloading list: " + e.Error.Message, e.Error);

                    GoEnd(WordEnum.ERROR_DOWNLOAD_LIST);
                    return;
                }
            }

            if (e.Result == null)
            {
                GoEnd(WordEnum.PROBLEM_WITH_SERVER);
                return;
            }

            GoNextStep(e.Result);
        }


        void _webClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            MainForm.Instance.UpdateProgressBar(e.ProgressPercentage, false);
        }

        /// <summary>
        /// С текущего масива грузит список файлов
        /// </summary>
        /// <param name="zipArray"></param>
        private void GoNextStep(byte[] zipArray)
        {
            try
            {
                byte[] array = null;

                ZipInputStream zipStream = new ZipInputStream(new MemoryStream(zipArray))
                                               {
                                                   Password = "afsf325cf6y34g6a5frs4cf5"
                                               };

                if ((zipStream.GetNextEntry()) != null)
                {
                    array = new byte[zipStream.Length];
                    zipStream.Read(array, 0, array.Length);
                }

                zipStream.Close();
            
                if (array == null)
                {
                    if (_log.IsDebugEnabled)
                    {
                        _log.Info("Error: array is null");
                    } 
                    GoEnd(WordEnum.PROBLEM_WITH_SERVER);
                    return;
                }

                string encodingBytes = Encoding.UTF8.GetString(array);
                string[] lines = encodingBytes.Split('\n');

                foreach (string line in lines)
                {
                    if (line.Trim().Equals(""))
                        continue;

                    if (Status == Status.CANCEL)
                    {
                        GoEnd(WordEnum.CANCEL_BY_USER);
                        return;
                    }

                    if (line.StartsWith("#Revision:"))
                    {
                        Revision = int.Parse(line.Replace("#Revision:", "").Trim());
                        continue;
                    }

                    if (line.StartsWith("#"))
                    {
                        continue;
                    }

                    try
                    {
                        ListFile file = ListFile.parse(line);

                        _list[file.Type].AddLast(file);
                    }
                    catch (Exception e)
                    {
                        _log.Info("Exception for line " + line + " " + e, e);
                    }
                }

                GoEnd(WordEnum.ENDING_DOWNLOAD_LIST);
            }
            catch(Exception e)
            {
                GoEnd(WordEnum.PROBLEM_WITH_SERVER);
                
                _log.Info("Exception: " + e, e);
            }
        }

        #region Go End
        [MethodImpl(MethodImplOptions.Synchronized)]
        private void GoEnd(WordEnum word)
        {
            Status = Status.FREE;

            MainForm.Instance.UpdateStatusLabel(word);

            if(word  == WordEnum.ENDING_DOWNLOAD_LIST)
            {
                IsValid = true;    
            }

            MainForm.Instance.UpdateProgressBar(0, true);
            MainForm.Instance.UpdateProgressBar(0, false);

            if (!(TaskManager.Instance.NextTask is AnalyzerTask))
            {
                MainForm.Instance.SetMainFormState(MainFormState.NONE);
            }

            OnEnd();
        }

        private void GoEnd(String word)
        {
            Status = Status.FREE;

            MainForm.Instance.UpdateStatusLabel(word);

            MainForm.Instance.UpdateProgressBar(0, true);
            MainForm.Instance.UpdateProgressBar(0, false);

            if (!(TaskManager.Instance.NextTask is AnalyzerTask))
            {
                MainForm.Instance.SetMainFormState(MainFormState.NONE);
            }

            OnEnd();
        }
#endregion

        public override void Cancel()
        {
            Status = Status.CANCEL;

            if (_webClient.IsBusy)
            {
                _webClient.CancelAsync();
            }
        }

        #endregion

        #region Properties & Getters

        public Dictionary<ListFileType, LinkedList<ListFile>> Items
        {
            get { return _list; }
        }

        public bool IsValid { get; set; }

        public Status Status { get; set; }

        public int Revision { get; set; }

        public LinkedList<ListFile> Files(ListFileType t)
        {
            return _list[t];
        }

        #endregion

        public override string ToString()
        {
            return _toString;
        }
    }
}