using DynamicData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;

namespace Wiltoga.Meppy
{
    /// <summary>
    /// Interaction logic for Configuration.xaml
    /// </summary>
    public partial class Configuration : Window
    {
        private Outliner? outliner;
        private CancellationTokenSource? popupCancellation;

        public Configuration()
        {
            InitializeComponent();
            EnabledRules = Array.Empty<string>();
            CloseApp = false;
        }

        public bool CloseApp { get; private set; }
        public string[] EnabledRules { get; private set; }
        private ConfigurationViewModel ViewModel => (ConfigurationViewModel)DataContext;

        public void Refresh(IEnumerable<Rule> rules) => ViewModel.RefreshSelection(rules);

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            EnabledRules = ViewModel.Rules.Where(rule => rule.Active).Select(rule => rule.Name).ToArray();
            outliner?.Hide();
            Hide();
        }

        private void AddExecutableButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executables|*.exe|All files|*.*",
                Title = "Select the executable to add to the rule table",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ViewModel.CacheSource.AddOrUpdate(dialog.FileNames
                    .Where(file => !ViewModel.CacheSource.Lookup(Path.GetFileNameWithoutExtension(file)).HasValue)
                    .Select(file => new ConfigurationRule(Path.GetFileNameWithoutExtension(file))
                    {
                        Active = true,
                        Filename = file
                    }));
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseApp = true;
            Close();
        }

        private async void EyeButton_Click(object sender, RoutedEventArgs e)
        {
            if (popupCancellation is not null)
            {
                popupCancellation.Cancel();
            }
            var rule = (sender as Button)?.DataContext as ConfigurationRule;
            if (rule is null)
                return;
            Win32.RECT position = default;
            var placement = new Win32.WINDOWPLACEMENT();

            if (rule.Process is not null && rule.Process.MainWindowHandle != IntPtr.Zero)
            {
                Win32.GetWindowPlacement(rule.Process.MainWindowHandle, ref placement);
                if (placement.showCmd == Win32.ShowWindowCommands.Maximized)
                {
                    var screen = Screen.FromHandle(rule.Process.MainWindowHandle);
                    position = Win32.RECT.FromSizes(
                        screen.WorkingArea.Left,
                        screen.WorkingArea.Top,
                        screen.WorkingArea.Width,
                        5);
                }
                else if (placement.showCmd == Win32.ShowWindowCommands.Minimized)
                {
                    var screen = Screen.FromHandle(rule.Process.MainWindowHandle);
                    position = Win32.RECT.FromSizes(
                        screen.WorkingArea.Left,
                        screen.WorkingArea.Bottom - 10,
                        screen.WorkingArea.Width,
                        10);
                }
                else
                    Win32.GetWindowRect(rule.Process.MainWindowHandle, ref position);
            }
            else if (rule.Reference?.State is not null)
            {
                position = Win32.RECT.FromSizes(
                    rule.Reference.State.Left,
                    rule.Reference.State.Top,
                    rule.Reference.State.Width,
                    rule.Reference.State.Height);
            }
            else
                return;
            outliner ??= new Outliner();

            outliner.Left = position.Left;
            outliner.Top = position.Top;
            outliner.Width = position.Width;
            outliner.Height = position.Height;
            if (rule.Process is not null && rule.Process.MainWindowHandle != IntPtr.Zero)
                Win32.SetForegroundWindow(rule.Process.MainWindowHandle);
            outliner.Show();
            outliner.Activate(); popupCancellation = new CancellationTokenSource();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), popupCancellation.Token);
                outliner.Hide();
                popupCancellation = null;
            }
            catch (TaskCanceledException)
            {
            }
        }

        private void rulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            rulesList.SelectedItem = null;
        }

        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                ViewModel.RefreshProcesses();
            }
            else
            {
            }
        }
    }
}