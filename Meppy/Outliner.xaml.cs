using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Wiltoga.Meppy
{
    /// <summary>
    /// Interaction logic for Outliner.xaml
    /// </summary>
    public partial class Outliner : Window
    {
        public Outliner()
        {
            InitializeComponent();

            Loaded += Outliner_Loaded;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            e.Cancel = true;
        }

        private void Outliner_Loaded(object sender, RoutedEventArgs e)
        {
            WindowInteropHelper wndHelper = new WindowInteropHelper(this);

            int exStyle = (int)Win32.GetWindowLong(wndHelper.Handle, (int)Win32.GetWindowLongFields.GWL_EXSTYLE);

            exStyle |= (int)Win32.ExtendedWindowStyles.WS_EX_TOOLWINDOW;
            Win32.SetWindowLong(wndHelper.Handle, (int)Win32.GetWindowLongFields.GWL_EXSTYLE, (IntPtr)exStyle);
        }
    }
}