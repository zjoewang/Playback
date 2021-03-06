﻿using System;
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
        private System.IO.StreamReader m_srLog = null;
        private int m_nLastTimeStamp = -1;
        private int[] m_red_buffer = new int[Algorithm30102.BUFFER_SIZE];
        private int[] m_time_buffer = new int[Algorithm30102.BUFFER_SIZE];
        private int[] m_ir_buffer = new int[Algorithm30102.BUFFER_SIZE];
        private const int MAX_TEMP_SIZE = 10 * Algorithm30102.BUFFER_SIZE;
        private int[] m_temp_red_buffer = new int[MAX_TEMP_SIZE];
        private int[] m_temp_ir_buffer = new int[MAX_TEMP_SIZE];
        private int[] m_temp_time_buffer = new int[MAX_TEMP_SIZE];
        private int m_nBufferSize = 0;
        private Algorithm30102 m_alg = new Algorithm30102();
        private bool m_bShowHRSP = true;
        private bool m_bShowACDC = true;
        private bool m_bShowRaw = true;
        private bool m_bShowNewHRSP = true;
        private FilterData m_sp_filter = new FilterData(10, 8, 1.0, 50, 95.0, 5.0, 5, 1.0);
        private FilterData m_hr_filter = new FilterData(10, 8, 1.5, 50, 50.0, 20.0, 5, 2.0);
        private bool m_bHasHRData = false;
        private bool m_bHasSPData = false;
        
        public static DialogResult InputBox(string title, string promptText, ref string value)
        {
            Form form = new Form();
            Label label = new Label();
            TextBox textBox = new TextBox();
            Button buttonOk = new Button();
            Button buttonCancel = new Button();

            form.Text = title;
            label.Text = promptText;
            textBox.Text = value;

            buttonOk.Text = "OK";
            buttonCancel.Text = "Cancel";
            buttonOk.DialogResult = DialogResult.OK;
            buttonCancel.DialogResult = DialogResult.Cancel;

            label.SetBounds(9, 20, 372, 13);
            textBox.SetBounds(12, 36, 372, 20);
            buttonOk.SetBounds(228, 72, 75, 23);
            buttonCancel.SetBounds(309, 72, 75, 23);

            label.AutoSize = true;
            textBox.Anchor = textBox.Anchor | AnchorStyles.Right;
            buttonOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            form.ClientSize = new Size(396, 107);
            form.Controls.AddRange(new Control[] { label, textBox, buttonOk, buttonCancel });
            form.ClientSize = new Size(Math.Max(300, label.Right + 10), form.ClientSize.Height);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.AcceptButton = buttonOk;
            form.CancelButton = buttonCancel;

            DialogResult dialogResult = form.ShowDialog();
            value = textBox.Text;
            return dialogResult;
        }

        public Form1()
        {
            InitializeComponent();
            label9.Text = "built on " + DateTime.Now.ToString(@"MM\/dd\/yyyy h\:mm tt");
            comboBox1.SelectedIndex = 1;
        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            m_logFile = openFileDialog1.FileName;
            m_bInited = true;
            label2.Text = m_logFile;
            button2.Enabled = true;
            button3.Enabled = true;
            button4.Enabled = false;
            m_nStep = -1;

            if (m_srLog != null)
            {
                m_srLog.Close();
                m_srLog = null;
            }

            button7.Enabled = true;
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
                m_nBufferSize = 0;
                m_bHasHRData = false;
                m_bHasSPData = false;
                chart1.Series["HR"].Points.Clear();
                chart1.Series["SP"].Points.Clear();
                chart1.Series["newHR"].Points.Clear();
                chart1.Series["newSP"].Points.Clear();
                chart1.Series["Red"].Points.Clear();
                chart1.Series["IR"].Points.Clear();
                chart1.Series["Green"].Points.Clear();
                chart1.Series["AD for Red"].Points.Clear();
                chart1.Series["AD for IR"].Points.Clear();
                chart1.Series["AD for Green"].Points.Clear();
                button2.Enabled = false;
                button4.Enabled = true;
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
            {
                m_nStep = -1;
                m_srLog.Close();
                m_srLog = null;
                timer1.Stop();
                m_bPaused = false;
                m_bHasHRData = false;
                m_bHasSPData = false;
                button3.Text = "Pause";
                button2_Click(sender, e);
            }
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
                int nTempSize = 0;

                while ((line = m_srLog.ReadLine()) != null)
                {
                    Match match = Regex.Match(line, @"HR=(\d+), SP=(\d+)");

                    int time = -1, red = -1, ir = -1, hr = -1, sp = -1;
                    bool found_rawdata = false, found_hrsp = false;

                    if (match.Success && match.Groups.Count == 3)
                    {
                        found_hrsp = true;

                        hr = Convert.ToInt32(match.Groups[1].Value);
                        sp = Convert.ToInt32(match.Groups[2].Value);
                        chart1.Series["HR"].Points.AddXY(m_nLastTimeStamp, hr);
                        chart1.Series["SP"].Points.AddXY(m_nLastTimeStamp, sp);

                        // Original values
                        label4.Text = "HR = " + hr.ToString() + ", SP = " + sp.ToString();
                    }
                    else
                    {
                        match = Regex.Match(line, @"time=(\d+), red=(\d+), ir=(\d+)");

                        if (match.Success && match.Groups.Count == 4)
                            found_rawdata = true;
                        else 
                        {
                            // CSV type
                            match = Regex.Match(line, @"\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");

                            if (match.Success && match.Groups.Count == 4)
                                found_rawdata = true;
                        }

                        if (found_rawdata)
                        {
                            time = Convert.ToInt32(match.Groups[1].Value);
                            red = Convert.ToInt32(match.Groups[2].Value);
                            ir = Convert.ToInt32(match.Groups[3].Value);

                            if (count < MAX_TEMP_SIZE)
                            {
                                m_temp_red_buffer[count] = red;
                                m_temp_ir_buffer[count] = ir;
                                m_temp_time_buffer[count] = time;
                                nTempSize = count + 1;
                                chart1.Series["Red"].Points.AddXY(time, red);
                                chart1.Series["IR"].Points.AddXY(time, ir);
                            }

                            m_nLastTimeStamp = time;
                            ++count;
                        }
                    }

                    if (found_hrsp || count > 100)
                    {
                        int nTotalSize = m_nBufferSize + nTempSize;

                        if (nTempSize >= Algorithm30102.BUFFER_SIZE)
                        {
                            // Temp is enough
                            int nLeftShift = nTempSize - Algorithm30102.BUFFER_SIZE;

                            for (int i = 0; i < Algorithm30102.BUFFER_SIZE; ++i)
                            {
                                m_red_buffer[i] = m_temp_red_buffer[i + nLeftShift];
                                m_ir_buffer[i] = m_temp_ir_buffer[i + nLeftShift];
                                m_time_buffer[i] = m_temp_time_buffer[i + nLeftShift];
                            }

                            m_nBufferSize = Algorithm30102.BUFFER_SIZE;
                        }
                        else if (nTotalSize > Algorithm30102.BUFFER_SIZE)
                        {
                            // Need to left-shift the most recent data on the system buffer
                            int nKeep = Algorithm30102.BUFFER_SIZE - nTempSize;
                            int nLeftShift = m_nBufferSize - nKeep;

                            for (int i = 0; i < nKeep; ++i)
                            {
                                m_red_buffer[i] = m_red_buffer[i + nLeftShift];
                                m_ir_buffer[i] = m_ir_buffer[i + nLeftShift];
                                m_time_buffer[i] = m_time_buffer[i + nLeftShift];
                            }

                            // Then copy the whole temp to fill the system buffer
                            for (int i = nKeep; i < Algorithm30102.BUFFER_SIZE; ++i)
                            {
                                m_red_buffer[i] = m_temp_red_buffer[i - nKeep];
                                m_ir_buffer[i] = m_temp_ir_buffer[i - nKeep];
                                m_time_buffer[i] = m_temp_time_buffer[i - nKeep];
                            }

                            m_nBufferSize = Algorithm30102.BUFFER_SIZE;
                        }
                        else
                        {
                            // Just add the temp to the system buffer
                            for (int i = m_nBufferSize; i < nTotalSize; ++i)
                            {
                                m_red_buffer[i] = m_temp_red_buffer[i - m_nBufferSize];
                                m_ir_buffer[i] = m_temp_ir_buffer[i - m_nBufferSize];
                                m_time_buffer[i] = m_temp_time_buffer[i - m_nBufferSize];
                            }

                            m_nBufferSize = nTotalSize;
                        }

                        int window = Convert.ToInt32(textBox1.Text);

                        if (window < 100)
                            window = 100;
                        else if (window > Algorithm30102.BUFFER_SIZE)
                            window = Algorithm30102.BUFFER_SIZE;

                        textBox1.Text = window.ToString();

                        if (m_nBufferSize >= window)
                        {
                            Debug.Assert(m_nBufferSize <= Algorithm30102.BUFFER_SIZE);

                            int nNewHR = -1, nNewSP = -1;
                            bool bHRValid, bSPValid;

                            try
                            {
                                int nStart = m_nBufferSize - window;

                                int nSelected = comboBox1.SelectedIndex, sr = 100;

                                if (nSelected == 0)
                                   sr = 50;
                                else if (nSelected == 1)
                                   sr = 100;
                                else if (nSelected == 2)
                                   sr = 200;

                                RData rdata = new RData();

                                m_alg.maxim_heart_rate_and_oxygen_saturation(sr, m_ir_buffer.Skip(nStart), window,
                                        m_red_buffer.Skip(nStart), out nNewSP, out bSPValid, out nNewHR,
                                        out bHRValid, rdata);

                                for (int i = 0; i < rdata.m_size; ++i)
                                {
                                    chart1.Series["AD for Red"].Points.AddXY(m_time_buffer[nStart + rdata.m_red_indices[i]],
                                            rdata.m_red_ratios[i]);
                                    chart1.Series["AD for IR"].Points.AddXY(m_time_buffer[nStart + rdata.m_ir_indices[i]],
                                            rdata.m_ir_ratios[i]);
                                }
                            }
                            catch
                            {
                                bHRValid = bSPValid = false;
                            }

                            if (!bHRValid)
                                nNewHR = -1;

                            if (!bSPValid)
                                nNewSP = -1;

                            if (nNewHR > 20 && nNewHR <= 200)
                            {
                                if (m_hr_filter.AddPoint((double)nNewHR))
                                    m_bHasHRData = true;
                                else
                                    nNewHR = -1;
                            }
                            else
                                nNewHR = -1;

                            if (m_bHasHRData)
                                nNewHR = (int) Math.Round(m_hr_filter.GetValue());

                            if (nNewSP > 50 && nNewSP <= 100)
                            {
                                if (m_sp_filter.AddPoint((double)nNewSP))
                                    m_bHasSPData = true;
                                else
                                    nNewSP = -1;
                            }
                            else
                                nNewSP = -1;

                            if (m_bHasSPData)
                                nNewSP = (int) Math.Round(m_sp_filter.GetValue());

                            label3.Text = "HR = " + nNewHR.ToString() + ", SP = " + nNewSP.ToString();

                            if (nNewHR > 0)
                                chart1.Series["newHR"].Points.AddXY(m_nLastTimeStamp, nNewHR);

                            if (nNewSP > 0)
                                chart1.Series["newSP"].Points.AddXY(m_nLastTimeStamp, nNewSP);
                        }
                        else
                            label3.Text = "Not enough raw data";

                        break;
                    }
                }

                if (line == null)       // EOF
                {
                    button2.Enabled = true;
                    button3.Enabled = false;
                    button4.Enabled = false;
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

        private void button6_Click(object sender, EventArgs e)
        {
            string value = "Chart Title";

            if (InputBox("Enter Title", "Chart Title:", ref value) == DialogResult.OK)
            {
                chart1.Titles[0].Text = value;
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            Process.Start("notepad.exe", m_logFile);
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            // Raw reflectance
            m_bShowRaw = checkBox1.Checked;
            chart1.Series["Red"].Enabled = m_bShowRaw;
            chart1.Series["IR"].Enabled = m_bShowRaw;
            chart1.Series["Green"].Enabled = m_bShowRaw;
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            // HR and SP
            m_bShowHRSP = checkBox2.Checked;
            chart1.Series["HR"].Enabled = m_bShowHRSP;
            chart1.Series["SP"].Enabled = m_bShowHRSP;
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            // AC/DC
            m_bShowACDC = checkBox3.Checked;
            chart1.Series["AD for Red"].Enabled = m_bShowACDC;
            chart1.Series["AD for IR"].Enabled = m_bShowACDC;
            chart1.Series["AD for Green"].Enabled = m_bShowACDC;
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            // New HR & SP
            m_bShowNewHRSP = checkBox4.Checked;
            chart1.Series["newHR"].Enabled = m_bShowNewHRSP;
            chart1.Series["newSP"].Enabled = m_bShowNewHRSP;
        }

        private void button8_Click(object sender, EventArgs e)
        {
            // Flip primarhy
            chart1.SuspendLayout();

            System.Windows.Forms.DataVisualization.Charting.AxisType old_raw_type = chart1.Series["IR"].YAxisType,
                new_raw_type = old_raw_type == System.Windows.Forms.DataVisualization.Charting.AxisType.Primary ?
                    System.Windows.Forms.DataVisualization.Charting.AxisType.Secondary :
                    System.Windows.Forms.DataVisualization.Charting.AxisType.Primary,
                new_other_type = old_raw_type;

            string old_primary_label = chart1.ChartAreas["ChartArea1"].AxisY.Title;

            chart1.ChartAreas["ChartArea1"].AxisY.Title = chart1.ChartAreas["ChartArea1"].AxisY2.Title;
            chart1.ChartAreas["ChartArea1"].AxisY2.Title = old_primary_label;

            chart1.Series["Red"].YAxisType = new_raw_type;
            chart1.Series["IR"].YAxisType = new_raw_type;
            chart1.Series["Green"].YAxisType = new_raw_type;
            chart1.Series["HR"].YAxisType = new_other_type;
            chart1.Series["SP"].YAxisType = new_other_type;
            chart1.Series["newHR"].YAxisType = new_other_type;
            chart1.Series["newSP"].YAxisType = new_other_type;
            chart1.Series["AD for Red"].YAxisType = new_other_type;
            chart1.Series["AD for IR"].YAxisType = new_other_type;
            chart1.Series["AD for Green"].YAxisType = new_other_type;

            chart1.ResumeLayout();

            // Restart
            button4_Click(sender, e);
        }

        private void chart1_GetToolTipText(object sender, System.Windows.Forms.DataVisualization.Charting.ToolTipEventArgs e)
        {
            // Check selected chart element and set tooltip text for it
            switch (e.HitTestResult.ChartElementType)
            {
                case System.Windows.Forms.DataVisualization.Charting.ChartElementType.DataPoint:
                    var dataPoint = e.HitTestResult.Series.Points[e.HitTestResult.PointIndex];
                    e.Text = string.Format("X:\t{0}\nY:\t{1}", dataPoint.XValue, dataPoint.YValues[0]);
                    break;
            }
        }
    }
}
