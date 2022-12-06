using DynamicData;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Interop;

namespace Wiltoga.Meppy
{
    internal class MeppyBackgroundService : BackgroundService
    {
        private Configuration? config;
        private ProcessWatcher processWatcher;

        public MeppyBackgroundService(ILogger<MeppyBackgroundService> logger)
        {
            Logger = logger;
            Rules = new();
            Handles = new List<(Rule, Task, CancellationTokenSource)>();
            processWatcher = new ProcessWatcher();
        }

        private List<(Rule Rule, Task Task, CancellationTokenSource TokenSource)> Handles { get; }

        private ILogger Logger { get; }
        private List<RuleSet> Rules { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var data = SaveData.LoadRules();

            lock (Rules)
            {
                Rules.AddRange(data.Rules.Select(rule => new RuleSet(rule)));

                foreach (var rule in Rules)
                {
                    processWatcher.Watch(rule.Rule.ProcessName);
                }
            }

            processWatcher.ProcessStarted += ProcessWatcher_ProcessStarted;
            processWatcher.NewWindowOpened += ProcessWatcher_NewWindowOpened;
            processWatcher.ProcessStopped += ProcessWatcher_ProcessStopped;

            processWatcher.Start(stoppingToken);

            var icon = new NotifyIcon
            {
                Icon = Properties.Resources.icon,
                Text = "Meppy",
                BalloonTipText = "Configure Meppy here !"
            };
            icon.Click += Icon_Click;
            icon.BalloonTipClicked += Icon_Click;
            icon.Visible = true;

            lock (Rules)
            {
                if (!Rules.Any())
                    icon.ShowBalloonTip(10000);
            }

            var saveTask = Task.Run(() =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    Thread.Sleep(1000);
                    lock (Rules)
                    {
                        SaveData.SaveRules(new Data
                        {
                            Rules = Rules.Select(rule => rule.Rule).ToArray()
                        });
                    }
                }
            }, stoppingToken);

            await processWatcher.ThreadTask;

            foreach (var taskHandle in Handles)
            {
                taskHandle.TokenSource.Cancel();
            }
            await Task.WhenAll(Handles.Select(taskHandle => taskHandle.Task));

            await saveTask;

            processWatcher.Dispose();

            icon.Dispose();

            Application.Exit();
        }

        private void Icon_Click(object? sender, EventArgs e)
        {
            config ??= new Configuration();
            lock (Rules)
            {
                config.Refresh(Rules.Select(set => set.Rule));
            }
            config.Show();
            void Config_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
            {
                if (e.NewValue is false)
                {
                    config.IsVisibleChanged -= Config_IsVisibleChanged;
                    lock (Rules)
                    {
                        var toRemove = Rules.Where(ruleSet => !config.EnabledRules.Contains(ruleSet.Rule.ProcessName, StringComparer.InvariantCultureIgnoreCase));
                        var toAdd = config.EnabledRules.Where(rule => !Rules.Any(currRuleSet => currRuleSet.Rule.ProcessName.Equals(rule, StringComparison.InvariantCultureIgnoreCase)));
                        foreach (var item in toRemove.ToArray())
                        {
                            Rules.Remove(item);
                            processWatcher.UnWatch(item.Rule.ProcessName);
                        }
                        foreach (var item in toAdd.ToArray())
                        {
                            processWatcher.Watch(item);
                            Rules.Add(new RuleSet(new Rule
                            {
                                ProcessName = item
                            }));
                        }
                    }

                    if (config.CloseApp)
                    {
                        Program.CancellationTokenSource.Cancel();
                    }
                }
            }
            config.IsVisibleChanged += Config_IsVisibleChanged;
        }

        private void OperateWindow(IntPtr hwnd, State state)
        {
            //https://learn.microsoft.com/en-us/answers/questions/522265/movewindow-and-setwindowpos-is-moving-window-for-e.html
            Win32.RECT required_rect = Win32.RECT.FromSizes(state.Left, state.Top, state.Width, state.Height);

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
            Win32.ShowWindow(hwnd, state.WindowState);
        }

        private void ProcessWatcher_NewWindowOpened(object? sender, NewWindowOpenedEventArgs e)
        {
            try
            {
                Logger.LogInformation("{ProcessName} opened", e.Process.ProcessName);
                RuleSet? ruleSet;
                lock (Rules)
                    ruleSet = Rules.FirstOrDefault(r => r.Rule.ProcessName == e.Process.ProcessName);
                var rule = ruleSet?.Rule;
                if (rule is null)
                    return;

                var hwnd = e.Process.MainWindowHandle;
                var state = ruleSet?.InitialState;
                if (state is not null)
                {
                    OperateWindow(hwnd, state);
                }
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Error during window change");
            }
        }

        private void ProcessWatcher_ProcessStarted(object? sender, ProcessStartedEventArgs e)
        {
            try
            {
                Logger.LogInformation("{ProcessName} opened", e.Process.ProcessName);
                RuleSet? ruleSet;
                lock (Rules)
                    ruleSet = Rules.FirstOrDefault(r => r.Rule.ProcessName == e.Process.ProcessName);
                var rule = ruleSet?.Rule;
                if (rule is null)
                    return;

                var hwnd = e.Process.MainWindowHandle;
                var state = ruleSet?.InitialState;
                if (state is not null)
                {
                    OperateWindow(hwnd, state);
                }

                var cancellationTokenSource = new CancellationTokenSource();

                var element = AutomationElement.FromHandle(hwnd);

                (Rule Rule, Task Task, CancellationTokenSource TokenSource)? taskHandle = null;

                void closedHandler(object sender, EventArgs e2)
                {
                    cancellationTokenSource.Cancel();
                    Logger.LogInformation("{ProcessName} closed", rule.ProcessName);
                    Automation.RemoveAutomationEventHandler(WindowPattern.WindowClosedEvent, element, closedHandler);
                    if (taskHandle is not null)
                        Handles.RemoveAt(Handles.FindIndex(handle => handle.Task == taskHandle.Value.Task));
                }

                Automation.AddAutomationEventHandler(
                    WindowPattern.WindowClosedEvent, element,
                    TreeScope.Subtree, closedHandler);
                taskHandle = (rule, Task.Run(() =>
                {
                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        lock (Rules)
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
                        }
                        Thread.Sleep(500);
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
            RuleSet? ruleSet;
            lock (Rules)
            {
                ruleSet = Rules.FirstOrDefault(rule => handle.Rule == rule.Rule);
                if (ruleSet is null)
                    return;
                ruleSet.InitialState = ruleSet.Rule.State;
            }
        }

        private class RuleSet
        {
            public RuleSet(Rule rule)
            {
                Rule = rule;
                InitialState = rule.State?.Copy;
            }

            public State? InitialState { get; set; }
            public Rule Rule { get; }
        }
    }
}