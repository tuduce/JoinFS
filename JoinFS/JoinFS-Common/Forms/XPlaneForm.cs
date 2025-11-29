using System;
using System.Windows.Forms;

namespace JoinFS
{
    public partial class XPlaneForm : Form
    {
        /// <summary>
        /// Get simulator folder
        /// </summary>
        /// <returns></returns>
        public string GetFolder()
        {
            // return folder
            return Text_Folder.Text;
        }

        /// <summary>
        /// initial parameters
        /// </summary>
        string initialFolder;

        public XPlaneForm(Main main, string initialFolder)
        {
            InitializeComponent();

            // change icon
            Icon = main.icon;
            // remove JoinFS from title
            Text = Text.Replace("JoinFS: ", "");

            this.initialFolder = initialFolder;

            // change font
            Text_Folder.Font = main.dataFont;
        }

        private void XPlane_Load(object sender, EventArgs e)
        {
            // set initial folder
            Text_Folder.Text = initialFolder;
        }

        private void Button_Browse_Click(object sender, EventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Select the main simulator folder",
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // get simulator folder
                Text_Folder.Text = dialog.SelectedPath;
            }
        }
    }
}
