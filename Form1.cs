using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Playback
{
    public partial class Form1 : Form
    {
        private bool m_bInited = false;
        private bool m_bPaused = false;
        private int m_nStep = -1;
        private string m_logFile = "";
        System.IO.StreamReader m_srLog = null;

        public Form1()
        {
            InitializeComponent();
        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            m_logFile = openFileDialog1.FileName;
            m_bInited = true;
            label2.Text = m_logFile;
            button2.Enabled = true;
            button3.Enabled = true;
            button4.Enabled = true;
            m_nStep = -1;

            if (m_srLog != null)
            {
                m_srLog.Close();
                m_srLog = null;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // Select file
            openFileDialog1.ShowDialog();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // Play
            if (m_bInited && m_nStep < 0)
            {
                try
                {
                    m_srLog = new System.IO.StreamReader(m_logFile);
                }
                catch
                {
                    MessageBox.Show("Error opening " + m_logFile);
                    m_bInited = false;
                    return;
                }

                m_nStep = 0;
                button2.Enabled = false;
                timer1.Start();
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            // Pause
            m_bPaused = !m_bPaused;

            if (m_bPaused)
                button3.Text = "Resume";
            else
                button3.Text = "Pause";
        }

        private void button4_Click(object sender, EventArgs e)
        {
            // Restart
            if (m_bInited && m_srLog != null)
                m_nStep = 0;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            // Exit
            Application.Exit();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            string line;

            if (!m_bPaused && m_bInited && m_nStep >= 0 && m_srLog != null)
            {
                ++m_nStep;
                label7.Text = m_nStep.ToString();

                int count = 0;

                while ((line = m_srLog.ReadLine()) != null && ++count < 111)
                {
                    Match match;

                    match = Regex.Match(line, @"time=(\d+), red=(\d+), ir=(\d+)");

                    int time = -1, red = -1, ir = -1, hr = -1, sp = -1;
                    bool found_rawdata = false, found_hrsp = false;

                    if (match.Success && match.Groups.Count == 4)
                    {
                        time = Convert.ToInt32(match.Groups[1].Value);
                        red = Convert.ToInt32(match.Groups[2].Value);
                        ir = Convert.ToInt32(match.Groups[3].Value);

                        found_rawdata = true;
                    }
                    else
                    {
                        match = Regex.Match(line, @"HR=(\d+), SP=(\d+)");

                        if (match.Success && match.Groups.Count == 3)
                        {
                            hr = Convert.ToInt32(match.Groups[1].Value);
                            sp = Convert.ToInt32(match.Groups[2].Value);

                            found_hrsp = true;
                        }
                    }

                    Debug.Assert(found_rawdata || found_hrsp);

                    if (found_hrsp)
                    {
                        label3.Text = "HR=" + hr.ToString() + ", SP=" + sp.ToString();
                        label4.Text = "HR=" + hr.ToString() + ", SP=" + sp.ToString();
                        break;
                    }
                }

                if (line == null)       // EOF
                {
                    button2.Enabled = true;
                    m_srLog.Close();
                    m_srLog = null;
                    m_nStep = -1;
                    timer1.Stop();
                }
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (m_srLog != null)
                m_srLog.Close();
        }
    }
}
