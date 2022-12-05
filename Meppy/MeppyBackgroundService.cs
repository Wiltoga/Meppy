﻿using Microsoft.Extensions.Hosting;
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
using static System.Windows.Forms.AxHost;

namespace Wiltoga.Meppy
{
    internal class MeppyBackgroundService : BackgroundService
    {
        public MeppyBackgroundService(ILogger<MeppyBackgroundService> logger)
        {
            Logger = logger;
            Rules = new();
            Handles = new List<(Rule, Task, CancellationTokenSource)>();
        }

        private List<(Rule Rule, Task Task, CancellationTokenSource TokenSource)> Handles { get; }

        private ILogger Logger { get; }

        private List<RuleSet> Rules { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var data = SaveData.LoadRules();
            Rules.AddRange(data.Rules.Select(rule => new RuleSet(rule, rule.State?.Copy)));

            var processWatcher = new ProcessWatcher();

            foreach (var rule in Rules)
            {
                processWatcher.Watch(rule.Rule.ProcessName);
            }

            processWatcher.ProcessStarted += ProcessWatcher_ProcessStarted;
            processWatcher.NewWindowOpened += ProcessWatcher_NewWindowOpened;
            processWatcher.ProcessStopped += ProcessWatcher_ProcessStopped;

            processWatcher.Start(stoppingToken);

            await processWatcher.ThreadTask;

            foreach (var taskHandle in Handles)
            {
                taskHandle.TokenSource.Cancel();
            }
            await Task.WhenAll(Handles.Select(taskHandle => taskHandle.Task));

            SaveData.SaveRules(new Data
            {
                Rules = Rules.Select(rule => rule.Rule).ToArray()
            });

            processWatcher.Dispose();
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
                var ruleSet = Rules.FirstOrDefault(r => r.Rule.ProcessName == e.Process.ProcessName);
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
                var ruleSet = Rules.FirstOrDefault(r => r.Rule.ProcessName == e.Process.ProcessName);
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
            var ruleSet = Rules.FirstOrDefault(rule => handle.Rule == rule.Rule);
            if (ruleSet is null)
                return;
            ruleSet.InitialState = ruleSet.Rule.State;
        }

        private class RuleSet
        {
            public RuleSet(Rule rule, State? initialState)
            {
                Rule = rule;
                InitialState = initialState;
            }

            public State? InitialState { get; set; }
            public Rule Rule { get; }
        }
    }
}