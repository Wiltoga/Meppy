using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wiltoga.Meppy
{
    internal class ProcessStartedEventArgs : EventArgs
    {
        public ProcessStartedEventArgs(Process process)
        {
            Process = process;
        }

        public Process Process { get; }
    }

    internal class ProcessStoppedEventArgs : EventArgs
    {
        public ProcessStoppedEventArgs(Process process)
        {
            Process = process;
        }

        public Process Process { get; }
    }

    internal class ProcessWatcher : IDisposable
    {
        private readonly object isRunningLock;
        private readonly object threadLock;
        private CancellationToken cancellationToken;
        private bool isRunning;
        private Task? threadTask;

        public ProcessWatcher() : this(TimeSpan.FromSeconds(1))
        {
        }

        public ProcessWatcher(TimeSpan pollingDelay)
        {
            TargetProcesses = new List<ProcessInfo>();
            PollingDelay = pollingDelay;
            threadLock = new object();
            isRunningLock = new object();
            cancellationToken = CancellationToken.None;
            isRunning = false;
        }

        ~ProcessWatcher()
        {
            Dispose(false);
        }

        public event EventHandler<ProcessStartedEventArgs>? ProcessStarted;

        public event EventHandler<ProcessStoppedEventArgs>? ProcessStopped;

        public bool IsRunning
        {
            get
            {
                lock (isRunningLock)
                    return isRunning;
            }
            private set
            {
                lock (isRunningLock)
                    isRunning = value;
            }
        }

        public TimeSpan PollingDelay { get; }

        public Task ThreadTask
        {
            get
            {
                ThrowIfNotStarted();
                return threadTask!;
            }
        }

        protected bool Disposed { get; private set; }

        private static Exception DisposedException => new ObjectDisposedException(nameof(ProcessWatcher));

        private static Exception NotStartedException => new InvalidOperationException("Watcher is not started");

        private static Exception StartedException => new InvalidOperationException("Watcher is already started");

        private List<ProcessInfo> TargetProcesses { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Start() => Start(CancellationToken.None);

        public void Start(CancellationToken token)
        {
            cancellationToken = token;
            ThrowIfStarted();
            IsRunning = true;
            threadTask = Task.Run(StartThreadLoop, token);
        }

        public void UnWatch(string name)
        {
            lock (threadLock)
            {
                var index = TargetProcesses.FindIndex(proc => proc.Name == name);
                if (index != -1)
                    TargetProcesses.RemoveAt(index);
            }
        }

        public void Watch(string name)
        {
            lock (threadLock)
            {
                if (!TargetProcesses.Any(proc => proc.Name == name))
                    TargetProcesses.Add(new ProcessInfo(name, null));
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    IsRunning = false;
                    lock (threadLock)
                    {
                        foreach (var target in TargetProcesses)
                        {
                            target.Process?.Dispose();
                        }
                        if (ProcessStarted != null)
                            foreach (var d in ProcessStarted.GetInvocationList())
                                ProcessStarted -= (d as EventHandler<ProcessStartedEventArgs>);
                        if (ProcessStopped != null)
                            foreach (var d in ProcessStopped.GetInvocationList())
                                ProcessStopped -= (d as EventHandler<ProcessStoppedEventArgs>);
                    }
                }
                Disposed = true;
            }
        }

        private void StartThreadLoop()
        {
            var foundProcesses = new List<string>();
            while (true)
            {
                lock (threadLock)
                {
                    if (cancellationToken.IsCancellationRequested)
                        IsRunning = false;
                    if (!IsRunning)
                        break;
                    foundProcesses.Clear();
                    foreach (var process in Process.GetProcesses())
                    {
                        foreach (var target in TargetProcesses)
                        {
                            if (process.ProcessName == target.Name && process.MainWindowHandle != IntPtr.Zero)
                            {
                                if (target.Process is null)
                                {
                                    ProcessStarted?.Invoke(this, new ProcessStartedEventArgs(process));
                                    target.Process = process;
                                }
                                else
                                    process.Dispose();
                                foundProcesses.Add(target.Name);
                            }
                        }
                    }
                    foreach (var targetProcess in TargetProcesses)
                    {
                        if (targetProcess.Process is not null && !foundProcesses.Contains(targetProcess.Name))
                        {
                            ProcessStopped?.Invoke(this, new ProcessStoppedEventArgs(targetProcess.Process));
                            targetProcess.Process.Dispose();
                            targetProcess.Process = null;
                        }
                    }
                }

                Thread.Sleep(PollingDelay);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Disposed)
                throw DisposedException;
        }

        private void ThrowIfNotStarted()
        {
            ThrowIfDisposed();
            if (!IsRunning)
                throw NotStartedException;
        }

        private void ThrowIfStarted()
        {
            ThrowIfDisposed();
            if (IsRunning)
                throw StartedException;
        }

        private class ProcessInfo
        {
            public ProcessInfo(string name, Process? process)
            {
                Name = name;
                Process = process;
            }

            public string Name { get; }
            public Process? Process { get; set; }
        }
    }
}