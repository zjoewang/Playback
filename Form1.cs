using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Playback
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
          
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // Select file
            openFileDialog1.OpenFile();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // Play
        }

        private void button3_Click(object sender, EventArgs e)
        {
            // Pause
        }

        private void button4_Click(object sender, EventArgs e)
        {
            // Restart
        }

        private void button5_Click(object sender, EventArgs e)
        {
            // Exit
            Application.Exit();
        }
    }
}
