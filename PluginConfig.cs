using System;
using Shared.Config;
using Torch;
using Torch.Views;

namespace TorchPlugin
{
    [Serializable]
    public class PluginConfig : ViewModel, IPluginConfig
    {
        private bool enabled = true;
        private bool detectCodeChanges = true;

        // �����ͺ��̽� ���� �߰�
        private string databaseServer = "localhost";
        private string databaseName = "mydatabase";
        private string databaseUser = "root";
        private string databasePassword = "my-secret-pw";
        private string Server_Id = "0";
        [Display(Order = 1, GroupName = "General", Name = "Enable plugin", Description = "Enable the plugin")]
        public bool Enabled
        {
            get => enabled;
            set => SetValue(ref enabled, value);
        }

        [Display(Order = 2, GroupName = "General", Name = "Detect code changes", Description = "Disable the plugin if any changes to the game code are detected before patching")]
        public bool DetectCodeChanges
        {
            get => detectCodeChanges;
            set => SetValue(ref detectCodeChanges, value);
        }

        // �����ͺ��̽� ���� �ּ� ����
        [Display(Order = 3, GroupName = "Database Settings", Name = "Database Server", Description = "The IP address or hostname of the MySQL server")]
        public string DatabaseServer
        {
            get => databaseServer;
            set => SetValue(ref databaseServer, value);
        }

        // �����ͺ��̽� �̸� ����
        [Display(Order = 4, GroupName = "Database Settings", Name = "Database Name", Description = "The name of the MySQL database")]
        public string DatabaseName
        {
            get => databaseName;
            set => SetValue(ref databaseName, value);
        }

        // �����ͺ��̽� ����� �̸� ����
        [Display(Order = 5, GroupName = "Database Settings", Name = "Database User", Description = "The username for the MySQL database")]
        public string DatabaseUser
        {
            get => databaseUser;
            set => SetValue(ref databaseUser, value);
        }

        // �����ͺ��̽� ��й�ȣ ����
        [Display(Order = 6, GroupName = "Database Settings", Name = "Database Password", Description = "The password for the MySQL database")]
        public string DatabasePassword
        {
            get => databasePassword;
            set => SetValue(ref databasePassword, value);
        }
        [Display(Order = 7, GroupName = "Database Settings", Name = "Database Password", Description = "The password for the MySQL database")]
        public string ServerId
        {
            get => Server_Id;
            set => SetValue(ref Server_Id, value);
        }
        // TODO: Encapsulate them as properties and define their Display properties
    }
}
