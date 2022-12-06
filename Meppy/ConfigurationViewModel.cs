using DynamicData;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Wiltoga.Meppy
{
    public class ConfigurationRule
    {
        private static Dictionary<string, ImageSource> iconsCache;

        private string? filename;

        private Process? process;

        static ConfigurationRule()
        {
            iconsCache = new(StringComparer.InvariantCultureIgnoreCase);
        }

        public ConfigurationRule(string name)
        {
            Name = name;
            Reference = null;
            Process = null;
        }

        [Reactive]
        public bool Active { get; set; }

        public string DisplayName => FriendlyName ?? Name;

        public string? Filename
        {
            get => filename;
            set
            {
                filename = value;
                if (iconsCache.TryGetValue(filename ?? "", out var cachedIcon))
                {
                    Icon = cachedIcon;
                }
                else
                {
                    Icon = null;
                }
                if (Icon is null)
                {
                    if (filename is not null)
                    {
                        var info = new Win32.SHFILEINFO
                        {
                            szDisplayName = "",
                            szTypeName = ""
                        };
                        int cbFileInfo = Marshal.SizeOf(info);
                        Win32.SHGFI flags = Win32.SHGFI.SHGFI_ICON | Win32.SHGFI.SHGFI_SMALLICON | Win32.SHGFI.SHGFI_USEFILEATTRIBUTES;

                        Win32.SHGetFileInfo(filename, 256, out info, (uint)cbFileInfo, flags);
                        var icon = System.Drawing.Icon.FromHandle(info.hIcon);
                        var stream = new MemoryStream();
                        icon.ToBitmap().Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        Win32.DestroyIcon(info.hIcon);
                        stream.Seek(0, SeekOrigin.Begin);
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.StreamSource = stream;
                        image.EndInit();
                        image.Freeze();
                        Icon = image;
                        iconsCache[filename] = image;
                    }
                }
            }
        }

        [Reactive]
        public ImageSource? Icon { get; private set; }

        public string Name { get; }

        public Process? Process
        {
            get => process;
            set
            {
                process = value;
                Filename = process?.MainModule?.FileName;
            }
        }

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