﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WPFWindow;

namespace WBAssistantF.Module.USB
{
    internal class DesktopArrange
    {
        private readonly Logger _logger;

        private UsbInfo currentInfo;
        private int insertedCount;
        private readonly FileSystemWatcher watcher;

        public DesktopArrange(Copier copier, Logger logger)
        {
            _logger = logger;
            copier.USBChange += Copier_USBChange;
            watcher = new FileSystemWatcher(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            watcher.Created += Watcher_Created;
        }

        public void Stop()
        {
            watcher.EnableRaisingEvents = false;
        }

        public void Start()
        {
            watcher.EnableRaisingEvents = true;
        }

        private ImageSource toSource(Icon ico)
        {
            return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }

        private void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(e.FullPath) && !Directory.Exists(e.FullPath)) return;
            var isFile = File.Exists(e.FullPath);

            var retry = 1200;
            while (IsFileOrDirLocked(isFile, e.FullPath))
            {
                Thread.Sleep(500);
                if (--retry == 0) return;
            }

            var destFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            if (insertedCount > 0)
                destFolder += "\\" + currentInfo.Remark;
            else
                destFolder += "\\其他";

            var thread = new Thread(() =>
            {
                var oriSource = File.Exists(e.FullPath)
                    ? Icon.ExtractAssociatedIcon(e.FullPath)
                    : DefaultIcons.FolderLarge;
                var msgBox = new MovingMsgBox(
                    e.FullPath,
                    destFolder,
                    toSource(oriSource),
                    toSource(DefaultIcons.FolderLarge)
                )
                {
                    WindowStartupLocation = WindowStartupLocation.Manual
                };
                msgBox.Left = Cursor.Position.X - msgBox.Width / 2;
                msgBox.Top = Cursor.Position.Y - msgBox.Height * 1.5;
                if (msgBox.Left < 0) msgBox.Left = 0;
                if (msgBox.Top < 0) msgBox.Top = 0;
                msgBox.Show();
                msgBox.ShowMovingAnim();
                try
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            if (destFolder == e.FullPath) return;
                            if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                            if (isFile)
                            {
                                if (File.Exists(e.FullPath)) File.Delete(destFolder + "\\" + e.Name);
                                File.Move(e.FullPath, destFolder + "\\" + e.Name);
                            }
                            else
                                Directory.Move(e.FullPath, destFolder + "\\" + e.Name);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogE("移动文件时出现了错误：\n" + ex.Message);
                        }

                        OpenFile(destFolder);
                        Thread.Sleep(6000);
                        msgBox.Dispatcher.InvokeShutdown();
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();

                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    _logger.LogE("整理文件时出现了错误：\n" + ex.Message);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        /// <summary>
        ///     whether there is inserted usb recorded.
        /// </summary>
        //private bool hasInserted = false;
        private void Copier_USBChange(bool IsInsert, UsbInfo? info)
        {
            if (IsInsert)
            {
                currentInfo = (UsbInfo) info;
                ++insertedCount;
            }
            else
                --insertedCount;
        }

        private bool IsFileOrDirLocked(bool isFile, string path)
        {
            if (isFile)
            {
                try
                {
                    using var stream = new FileInfo(path).Open(FileMode.Open, FileAccess.Read, FileShare.None);
                    stream.Close();
                }
                catch (IOException)
                {
                    //the file is unavailable because it is:
                    //still being written to
                    //or being processed by another thread
                    //or does not exist (has already been processed)
                    return true;
                }

                //file is not locked
                return false;
            }

            foreach (var file in Directory.GetFiles(path))
                if (IsFileOrDirLocked(true, file))
                    return true;
            foreach (var dir in Directory.GetDirectories(path))
                if (IsFileOrDirLocked(false, dir))
                    return true;
            return false;
        }

        private static void OpenFile(string filename)
        {
            var p = new Process
            {
                StartInfo =
                {
                    FileName = "explorer.exe", Arguments = $"\"{filename}\""
                }
            };
            p.Start();
        }
    }

    // get it from https://stackoverflow.com/a/59129804
    public static class DefaultIcons
    {
        private const uint SHSIID_FOLDER = 0x3;
        private const uint SHGSI_ICON = 0x100;
        private const uint SHGSI_LARGEICON = 0x0;
        private const uint SHGSI_SMALLICON = 0x1;


        private static Icon folderIcon;

        public static Icon FolderLarge => folderIcon ?? (folderIcon = GetStockIcon(SHSIID_FOLDER, SHGSI_LARGEICON));

        private static Icon GetStockIcon(uint type, uint size)
        {
            var info = new SHSTOCKICONINFO();
            info.cbSize = (uint) Marshal.SizeOf(info);

            SHGetStockIconInfo(type, SHGSI_ICON | size, ref info);

            var icon = (Icon) Icon.FromHandle(info.hIcon).Clone(); // Get a copy that doesn't use the original handle
            DestroyIcon(info.hIcon); // Clean up native icon to prevent resource leak

            return icon;
        }

        [DllImport("shell32.dll")]
        public static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHSTOCKICONINFO
        {
            public uint cbSize;
            public IntPtr hIcon;
            public int iSysIconIndex;
            public int iIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szPath;
        }
    }
}