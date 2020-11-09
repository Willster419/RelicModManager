﻿using System;
using System.Linq;
using System.Windows;
using System.Net;
using System.IO;
using System.Windows.Threading;
using RelhaxModpack.Utilities;
using RelhaxModpack.Database;
using RelhaxModpack.Utilities.Enums;
using RelhaxModpack.Utilities.ClassEventArgs;

namespace RelhaxModpack.Windows
{
    /// <summary>
    /// The delegate for invocation of when the FTP upload or download finishes
    /// </summary>
    /// <param name="sender">The sending object</param>
    /// <param name="e">The Upload or download event arguments</param>
    public delegate void EditorUploadDownloadClosed(object sender, EditorUploadDownloadEventArgs e);

    /// <summary>
    /// Interaction logic for DatabaseEditorDownload.xaml
    /// </summary>
    public partial class DatabaseEditorDownload : RelhaxWindow
    {
        //public
        /// <summary>
        /// The path to the zip file on the disk
        /// </summary>
        public string ZipFilePathDisk;

        /// <summary>
        /// The FTP path to the zip file
        /// </summary>
        public string ZipFilePathOnline;

        /// <summary>
        /// The complete name of the Zip file
        /// </summary>
        public string ZipFileName;

        /// <summary>
        /// The FTP credentials
        /// </summary>
        public NetworkCredential Credential;

        /// <summary>
        /// Enumeration flag to indicate uploading or downloading
        /// </summary>
        public EditorTransferMode TransferMode = EditorTransferMode.DownloadZip;

        /// <summary>
        /// The package being updated. A null package with Upload=true indicates the item being uploaded is a media
        /// </summary>
        public DatabasePackage PackageToUpdate;

        /// <summary>
        /// The event callback used for the editor when an upload or download is finished
        /// </summary>
        public event EditorUploadDownloadClosed OnEditorUploadDownloadClosed;

        /// <summary>
        /// The timeout, in seconds, until the window will automatically close
        /// </summary>
        public uint Countdown = 0;

        //private
        private WebClient client = null;
        private string CompleteFTPPath = string.Empty;
        private long FTPDownloadFilesize = -1;
        private DispatcherTimer timer = null;

        /// <summary>
        /// Create an instance of the DatabaseEditorDownlaod class
        /// </summary>
        public DatabaseEditorDownload()
        {
            InitializeComponent();
        }

        private async void RelhaxWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //init timer
            timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, Timer_Elapsed, this.Dispatcher) { IsEnabled = false };

            //set the open folder and file button
            //if uploading, the buttons are invalid, don't show then
            //if downloading, the buttons are valid to show, but not enabled until the download is complete
            switch(TransferMode)
            {
                case EditorTransferMode.UploadZip:
                    OpenFodlerButton.Visibility = Visibility.Hidden;
                    OpenFileButton.Visibility = Visibility.Hidden;
                    ProgressBody.Text = string.Format("Uploading {0} to FTP folder {1}", Path.GetFileName(ZipFilePathDisk), Settings.WoTModpackOnlineFolderVersion);
                    break;
                case EditorTransferMode.UploadMedia:
                    OpenFodlerButton.Visibility = Visibility.Hidden;
                    OpenFileButton.Visibility = Visibility.Hidden;
                    ProgressBody.Text = string.Format("Uploading {0} to FTP folder Medias/...", ZipFileName);
                    break;
                case EditorTransferMode.DownloadZip:
                    OpenFodlerButton.Visibility = Visibility.Visible;
                    OpenFileButton.Visibility = Visibility.Visible;
                    OpenFodlerButton.IsEnabled = false;
                    OpenFileButton.IsEnabled = false;
                    ProgressBody.Text = string.Format("Downloading {0} from FTP folder {1}", Path.GetFileName(ZipFilePathDisk), Settings.WoTModpackOnlineFolderVersion);
                    break;
            }

            //set initial text(s)
            ProgressHeader.Text = string.Format("{0} 0 kb of 0 kb", TransferMode.ToString());
            CompleteFTPPath = string.Format("{0}{1}", ZipFilePathOnline, ZipFileName);

            using (client = new WebClient() { Credentials=Credential })
            {
                switch(TransferMode)
                {
                    case EditorTransferMode.UploadZip:
                    case EditorTransferMode.UploadMedia:
                        //before uploading, make sure it doesn't exist first (zipfile or media)
                        ProgressHeader.Text = "Checking if file exists on server...";
                        Logging.Editor("Checking if {0} already exists on the server in folder {1}", LogLevel.Info, ZipFileName, Settings.WoTModpackOnlineFolderVersion);
                        string[] listOfFilesOnServer = await FtpUtils.FtpListFilesFoldersAsync(ZipFilePathOnline, Credential);
                        if (listOfFilesOnServer.Contains(ZipFileName) && MessageBox.Show("File already exists, overwrite?", "", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        {
                            Logging.Editor("DOES exist and user said don't overwrite, aborting");
                            ProgressHeader.Text = "Canceled";
                            return;
                        }

                        //attach upload event handlers
                        client.UploadProgressChanged += Client_UploadProgressChanged;
                        client.UploadFileCompleted += Client_DownloadUploadFileCompleted;

                        //run the FTP upload
                        Logging.Editor("Starting FTP upload of {0} from folder {1}", LogLevel.Info, ZipFileName, Settings.WoTModpackOnlineFolderVersion);
                        try
                        {
                            await client.UploadFileTaskAsync(CompleteFTPPath, ZipFilePathDisk);
                            Logging.Editor("FTP upload complete of {0}", LogLevel.Info, ZipFileName);

                            //run upload event handler
                            OnEditorUploadDownloadClosed?.Invoke(this, new EditorUploadDownloadEventArgs()
                            {
                                Package = PackageToUpdate,
                                UploadedFilename = ZipFileName,
                                UploadedFilepathOnline = ZipFilePathOnline,
                                TransferMode = this.TransferMode
                            });
                        }
                        catch (Exception ex)
                        {
                            Logging.Editor("FTP UPLOAD Failed");
                            Logging.Editor(ex.ToString());
                        }
                        finally
                        {
                            CancelButton.IsEnabled = false;
                        }
                        break;
                    case EditorTransferMode.DownloadZip:
                        //attach download event handlers
                        client.DownloadProgressChanged += Client_DownloadProgressChanged;
                        client.DownloadFileCompleted += Client_DownloadUploadFileCompleted;

                        //run the FTP download
                        Logging.Editor("Starting FTP download of {0} from folder {1}", LogLevel.Info, ZipFileName, Settings.WoTModpackOnlineFolderVersion);
                        try
                        {
                            FTPDownloadFilesize = await FtpUtils.FtpGetFilesizeAsync(CompleteFTPPath, Credential);
                            await client.DownloadFileTaskAsync(CompleteFTPPath, ZipFilePathDisk);
                            Logging.Editor("FTP download complete of {0}", LogLevel.Info, ZipFileName);
                        }
                        catch (Exception ex)
                        {
                            Logging.Editor("FTP download failed of {0}", LogLevel.Info, ZipFileName);
                            Logging.Editor(ex.ToString());
                        }
                        finally
                        {
                            CancelButton.IsEnabled = false;
                            OpenFileButton.IsEnabled = true;
                            OpenFodlerButton.IsEnabled = true;
                        }
                        break;
                }
            }
            StartTimerForClose();
        }

        private void StartTimerForClose()
        {
            if(Countdown == 0)
            {
                Logging.Editor("Countdown is 0, do not close");
            }
            else
            {
                Logging.Editor("Countdown is > 0, starting");
                TimeoutClose.Visibility = Visibility.Visible;
                timer.Start();
                TimeoutClose.Text = Countdown.ToString();
            }
        }

        private void Timer_Elapsed(object sender, EventArgs e)
        {
            TimeoutClose.Text = (--Countdown).ToString();
            if (Countdown == 0)
            {
                Logging.Editor("Countdown complete, closing the window");
                timer.Stop();
                Close();
            }
        }

        private async void Client_DownloadUploadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            if(e.Cancelled)
            {
                Logging.Editor("FTP upload or download cancel detected from UI thread, handling");
                switch (TransferMode)
                {
                    case EditorTransferMode.UploadZip:
                    case EditorTransferMode.UploadMedia:
                        Logging.Editor("Deleting file on server");
                        await FtpUtils.FtpDeleteFileAsync(CompleteFTPPath, Credential);
                        break;
                    case EditorTransferMode.DownloadZip:
                        Logging.Editor("Deleting file on disk");
                        File.Delete(ZipFilePathDisk);
                        break;
                }
            }
        }

        private void Client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            //https://stackoverflow.com/questions/4591059/download-file-from-ftp-with-progress-totalbytestoreceive-is-always-1
            if (ProgressProgressBar.Maximum != FTPDownloadFilesize)
                ProgressProgressBar.Maximum = FTPDownloadFilesize;
            ProgressProgressBar.Value = e.BytesReceived;
            ProgressHeader.Text = string.Format("{0} {1} of {2}", "Downloaded",
                FileUtils.SizeSuffix((ulong)e.BytesReceived, 1, true, false), FileUtils.SizeSuffix((ulong)FTPDownloadFilesize, 1, true, false));
        }

        private void Client_UploadProgressChanged(object sender, UploadProgressChangedEventArgs e)
        {
            if (ProgressProgressBar.Maximum != e.TotalBytesToSend)
                ProgressProgressBar.Maximum = e.TotalBytesToSend;
            ProgressProgressBar.Value = e.BytesSent;
            ProgressHeader.Text = string.Format("{0} {1} of {2}", "Uploaded",
                FileUtils.SizeSuffix((ulong)e.BytesSent, 1, true, false), FileUtils.SizeSuffix((ulong)e.TotalBytesToSend, 1, true, false));
        }

        private void OpenFodlerButton_Click(object sender, RoutedEventArgs e)
        {
            if(!Directory.Exists(Path.GetDirectoryName(ZipFilePathDisk)))
            {
                Logging.Editor("OpenFolder button pressed but path {0} does not exist!", LogLevel.Info, Path.GetDirectoryName(ZipFilePathDisk));
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(Path.GetDirectoryName(ZipFilePathDisk));
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(ZipFilePathDisk))
            {
                Logging.Editor("OpenFile button pressed but file {0} does not exist!", LogLevel.Info, ZipFilePathDisk);
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(ZipFilePathDisk);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Logging.Editor("Cancel pressed, TransferMode={0}", LogLevel.Info, TransferMode.ToString());
            ProgressHeader.Text = "Canceled";
            Logging.Editor("Canceling upload or download operation");
            client.CancelAsync();
        }
    }
}
