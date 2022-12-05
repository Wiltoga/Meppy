using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Wiltoga.Meppy
{
    internal class MeppyBackgroundService : BackgroundService
    {
        public MeppyBackgroundService(ILogger<MeppyBackgroundService> logger)
        {
            Logger = logger;
            Rules = new List<Rule>();
            Handles = new List<(Rule, Task, CancellationTokenSource)>();
        }

        private List<(Rule Rule, Task Task, CancellationTokenSource TokenSource)> Handles { get; }

        private ILogger Logger { get; }

        private List<Rule> Rules { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var data = SaveData.LoadRules();
            Rules.AddRange(data.Rules);

            var processWatcher = new ProcessWatcher();

            foreach (var rule in Rules)
            {
                processWatcher.Watch(rule.ProcessName);
            }

            processWatcher.ProcessStarted += ProcessWatcher_ProcessStarted;
            processWatcher.ProcessStopped += ProcessWatcher_ProcessStopped;

            processWatcher.Start(stoppingToken);

            /*var icon = new NotifyIcon
            {
                Icon = Properties.Resources.icon,
                Text = "Meppy",
                BalloonTipText = "Configure Meppy here !"
            };
            icon.Click += Icon_Click;
            icon.BalloonTipClicked += Icon_Click;
            icon.Visible = true;
            if (createdRules)
                icon.ShowBalloonTip(5000);*/

            await processWatcher.ThreadTask;

            foreach (var taskHandle in Handles)
            {
                taskHandle.TokenSource.Cancel();
            }
            await Task.WhenAll(Handles.Select(taskHandle => taskHandle.Task));

            SaveData.SaveRules(new Data
            {
                Rules = Rules.ToArray()
            });

            processWatcher.Dispose();
        }

        private void ProcessWatcher_ProcessStarted(object? sender, ProcessStartedEventArgs e)
        {
            try
            {
                Logger.LogInformation("{ProcessName} opened", e.Process.ProcessName);
                var rule = Rules.FirstOrDefault(r => r.ProcessName == e.Process.ProcessName);
                if (rule is null)
                    return;

                var hwnd = e.Process.MainWindowHandle;
                if (rule.State is not null)
                {
                    //https://learn.microsoft.com/en-us/answers/questions/522265/movewindow-and-setwindowpos-is-moving-window-for-e.html
                    Win32.RECT required_rect = Win32.RECT.FromSizes(rule.State.Left, rule.State.Top, rule.State.Width, rule.State.Height);

                    // I have no idea what these values are, thanks to the source post, but it works
                    var wrect = new Win32.RECT();
                    var xrect = new Win32.RECT();

                    Win32.GetWindowRect(hwnd, ref wrect);
                    Win32.DwmGetWindowAttribute(hwnd, 9, ref xrect, Marshal.SizeOf(typeof(Win32.RECT)));

                    var wtl = new Point(wrect.Left, wrect.Top);
                    var wbr = new Point(wrect.Right, wrect.Bottom);

                    var xtl = new Point(xrect.Left, xrect.Top);
                    var xbr = new Point(xrect.Right, xrect.Bottom);

                    Win32.PhysicalToLogicalPointForPerMonitorDPI(hwnd, ref xtl);
                    Win32.PhysicalToLogicalPointForPerMonitorDPI(hwnd, ref xbr);

                    var adjusted_rect = Win32.RECT.FromSizes(
                       required_rect.Left - (xtl.X - wtl.X),
                       required_rect.Top - (xtl.Y - wtl.Y),
                       required_rect.Width + (xtl.X - wtl.X) + (wbr.X - xbr.X),
                       required_rect.Height + (xtl.Y - wtl.Y) + (wbr.Y - xbr.Y));

                    Win32.MoveWindow(hwnd, adjusted_rect.Left, adjusted_rect.Top, adjusted_rect.Width, adjusted_rect.Height, true);
                    Win32.ShowWindow(hwnd, rule.State.WindowState);
                }

                var cancellationTokenSource = new CancellationTokenSource();

                (Rule, Task, CancellationTokenSource)? taskHandle = null;

                taskHandle = (rule, Task.Run(() =>
                {
                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        var rect = new Win32.RECT();
                        var placement = new Win32.WINDOWPLACEMENT();
                        if (Win32.GetWindowRect(hwnd, ref rect) && Win32.GetWindowPlacement(hwnd, ref placement))
                        {
                            if (placement.showCmd == Win32.ShowWindowCommands.Normal)
                                rule.State = new State
                                {
                                    Left = rect.Left,
                                    Top = rect.Top,
                                    Width = rect.Width,
                                    Height = rect.Height,
                                    WindowState = placement.showCmd
                                };
                            else
                            {
                                var currentScreen = Screen.FromHandle(hwnd);
                                rule.State ??= new State();
                                rule.State.Left = currentScreen.WorkingArea.Left + currentScreen.WorkingArea.Width / 2 - rule.State.Width / 2;
                                rule.State.Top = currentScreen.WorkingArea.Top + currentScreen.WorkingArea.Height / 2 - rule.State.Height / 2;
                                rule.State.WindowState = placement.showCmd;
                            }
                        }
                        Thread.Sleep(200);
                    }
                }, cancellationTokenSource.Token), cancellationTokenSource);
                Handles.Add(taskHandle.Value);
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Error during window change");
            }
        }

        private void ProcessWatcher_ProcessStopped(object? sender, ProcessStoppedEventArgs e)
        {
            var handle = Handles.FirstOrDefault(h => h.Rule.ProcessName.Equals(e.Process.ProcessName, StringComparison.InvariantCultureIgnoreCase));
            if (handle.Task is null)
                return;
            handle.TokenSource.Cancel();
            Logger.LogInformation("{ProcessName} closed", handle.Rule.ProcessName);
            Handles.Remove(handle);
        }

        /*private void Icon_Click(object? sender, EventArgs e)
        {
            config ??= new Configuration();
            config.Refresh(Rules);
            config.Show();
            void Config_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
            {
                if (e.NewValue is false)
                {
                    config.IsVisibleChanged -= Config_IsVisibleChanged;
                    var toRemove = Rules.Where(rule => !config.EnabledRules.Contains(rule.ProcessName, StringComparer.InvariantCultureIgnoreCase));
                    var toAdd = config.EnabledRules.Where(rule => !Rules.Any(currRule => currRule.ProcessName.Equals(rule, StringComparison.InvariantCultureIgnoreCase)));
                    foreach (var item in toRemove.ToArray())
                    {
                        Rules.Remove(item);
                    }
                    foreach (var item in toAdd.ToArray())
                    {
                        Rules.Add(new Rule
                        {
                            ProcessName = item
                        });
                    }
                }
            }
            config.IsVisibleChanged += Config_IsVisibleChanged;
        }*/
    }
}