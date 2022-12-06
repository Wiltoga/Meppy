using DynamicData;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Wiltoga.Meppy
{
    public class ConfigurationRule
    {
        public ConfigurationRule(string name)
        {
            Name = name;
            Reference = null;
            Process = null;
        }

        [Reactive]
        public bool Active { get; set; }

        public string DisplayName => FriendlyName ?? Name;
        public string? Filename { get; set; }
        public string Name { get; }
        public Process? Process { get; set; }
        public Rule? Reference { get; set; }

        private string? FriendlyName
        {
            get
            {
                string? file = null;
                try
                {
                    file = Filename ?? Process?.MainModule?.FileName;
                }
                catch (Exception)
                {
                }
                if (file is null)
                    return null;
                var versionInfo = FileVersionInfo.GetVersionInfo(file);
                return versionInfo.ProductName ?? null;
            }
        }
    }

    public class ConfigurationViewModel : ReactiveObject
    {
        public ConfigurationViewModel()
        {
            CacheSource = new SourceCache<ConfigurationRule, string>(o => o.Name.ToLowerInvariant());
            CacheSource
                .Connect()
                .Filter(rule => HasRights(rule.Process) &&
                    AvailableWindow(rule.Process) &&
                    !string.IsNullOrWhiteSpace(rule.DisplayName))
                .Bind(out var rules)
                .Subscribe();

            Rules = rules;
        }

        public SourceCache<ConfigurationRule, string> CacheSource { get; }

        public IReadOnlyCollection<ConfigurationRule> Rules { get; set; }

        public void RefreshProcesses()
        {
            var rulesToRemove = new List<ConfigurationRule>();
            foreach (var rule in CacheSource.Items)
            {
                if (rule.Process?.HasExited is true && !rule.Active)
                    rulesToRemove.Add(rule);
                rule.Process?.Dispose();
                rule.Process = null;
            }
            CacheSource.Remove(rulesToRemove);
            var processes = Process.GetProcesses()
                .Where(process =>
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        process.Dispose();
                        return false;
                    }
                    return true;
                });
            foreach (var process in processes)
            {
                var rule = CacheSource.Lookup(process.ProcessName.ToLowerInvariant());
                if (rule.HasValue)
                {
                    rule.Value.Process = process;
                }
                else
                {
                    CacheSource.AddOrUpdate(new ConfigurationRule(process.ProcessName)
                    {
                        Process = process
                    });
                }
            }
        }

        public void RefreshSelection(IEnumerable<Rule> selectedRules)
        {
            foreach (var rule in Rules)
            {
                if (!selectedRules.Any(selectedRule => selectedRule.ProcessName.Equals(rule.Name, StringComparison.InvariantCultureIgnoreCase)))
                    rule.Active = false;
            }
            foreach (var selectedRule in selectedRules)
            {
                var rule = Rules.FirstOrDefault(r => r.Name.Equals(selectedRule.ProcessName, StringComparison.InvariantCultureIgnoreCase));
                if (rule is null)
                {
                    rule = new ConfigurationRule(selectedRule.ProcessName);
                    CacheSource.AddOrUpdate(rule);
                }
                rule.Active = true;
                rule.Reference = selectedRule;
            }
        }

        private static bool AvailableWindow(Process? proc)
        {
            if (proc is null)
                return true;
            var handle = proc.MainWindowHandle;
            var size = new Win32.RECT();
            Win32.GetWindowRect(handle, ref size);
            return size.Width > 1 && size.Height > 1;
        }

        private static bool HasRights(Process? proc)
        {
            try
            {
                var filename = proc?.MainModule?.FileName;
                return true;
            }
            catch (Exception)
            {
                // no rights to acces the process...
                return false;
            }
        }
    }
}