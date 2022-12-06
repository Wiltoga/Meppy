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
                var file = Filename ?? Process?.MainModule?.FileName;
                if (file is null)
                    return null;
                var versionInfo = FileVersionInfo.GetVersionInfo(file);
                return versionInfo.FileDescription ?? null;
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
                .Filter(rule => rule.Process?.ProcessName != "explorer" &&
                    !string.IsNullOrWhiteSpace(rule.DisplayName))
                .Bind(out var rules)
                .Subscribe();

            Rules = rules;
        }

        public SourceCache<ConfigurationRule, string> CacheSource { get; }
        public IReadOnlyCollection<ConfigurationRule> Rules { get; set; }

        public void RefreshProcesses()
        {
            foreach (var rule in CacheSource.Items)
            {
                rule.Process?.Dispose();
            }
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
    }
}