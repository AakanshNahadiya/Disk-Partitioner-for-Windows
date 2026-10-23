using System.Reflection;
using System.Windows;
using DiskPartitioner.Helpers;

namespace DiskPartitioner.Views
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();

            var ver = typeof(AboutDialog).Assembly.GetName().Version;
            string versionStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.2";
            string storeSuffix = PackageHelper.IsStorePackage() ? " (Microsoft Store)" : "";
            txtVersion.Text = $"Version {versionStr}{storeSuffix} by Accessible Tools";

            Loaded += (s, e) => btnClose.Focus();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
