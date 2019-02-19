//============================================================================
//RF Explorer for Windows - A Handheld Spectrum Analyzer for everyone!
//Copyright (C) 2010-19 RF Explorer Technologies SL, www.rf-explorer.com
//
//This application is free software; you can redistribute it and/or
//modify it under the terms of the GNU Lesser General Public
//License as published by the Free Software Foundation; either
//version 3.0 of the License, or (at your option) any later version.
//
//This software is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//General Public License for more details.
//
//You should have received a copy of the GNU General Public
//License along with this library; if not, write to the Free Software
//Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
//=============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using RFExplorerCommunicator;

namespace RFExplorerSimpleClient
{
    public partial class SimpleMainForm : Form
    {
        #region Members
        System.Windows.Forms.Timer m_RefreshTimer = new System.Windows.Forms.Timer();
        string m_sRFEReceivedString = "";
        RFECommunicator m_objRFE;
        #endregion

        #region Main Form handling
        public SimpleMainForm()
        {
            InitializeComponent();

            m_objRFE = new RFECommunicator(true);
            m_objRFE.PortClosedEvent += new EventHandler(OnRFE_PortClosed);
            m_objRFE.ReportInfoAddedEvent += new EventHandler(OnRFE_ReportLog);
            m_objRFE.ReceivedConfigurationDataEvent += new EventHandler(OnRFE_ReceivedConfigData);
            m_objRFE.UpdateDataEvent += new EventHandler(OnRFE_UpdateData);
        }

        private void SimpleMainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            m_objRFE.Close();
        }

        private void SimpleMainForm_Load(object sender, EventArgs e)
        {
            GetConnectedPortsRFExplorer(); 
            UpdateButtonStatus();

            m_RefreshTimer.Enabled = true;
            m_RefreshTimer.Interval = 100;
            m_RefreshTimer.Tick += new System.EventHandler(this.RefreshTimer_tick);
        }
        #endregion

        #region RFExplorer Events
        bool m_bNewConfigurationReceived = false; //use this state variable to know when asyncrhonous config was received
        private void OnRFE_ReceivedConfigData(object sender, EventArgs e)
        {
            m_bNewConfigurationReceived = true;
            ReportDebug(m_sRFEReceivedString);
            m_objRFE.SweepData.CleanAll(); //we do not want mixed data sweep values
        }

        private void OnRFE_UpdateData(object sender, EventArgs e)
        {
            RFESweepData objData = m_objRFE.SweepData.GetData(m_objRFE.SweepData.Count - 1);

            labelSweeps.Text = "Sweeps: " + m_objRFE.SweepData.Count.ToString();
            if (objData != null)
            {
                labelSweeps.Text += " Points: " + objData.TotalDataPoints;

                UInt16 nPeak = objData.GetPeakDataPoint();

                labelFrequency.Text = objData.GetFrequencyMHZ(nPeak).ToString("f3") + " MHZ";
                labelAmplitude.Text = objData.GetAmplitudeDBM(nPeak).ToString("f2") + " dBm";
            }
        }

        private void OnRFE_ReportLog(object sender, EventArgs e)
        {
            EventReportInfo objArg = (EventReportInfo)e;
            ReportDebug(objArg.Data);
        }

        private void ReportDebug(string sLine)
        {
            if (!m_edRFEReportLog.IsDisposed && !m_chkDebug.IsDisposed && m_chkDebug.Checked)
            {
                if (sLine.Length > 0)
                    m_edRFEReportLog.AppendText(sLine);
                m_edRFEReportLog.AppendText(Environment.NewLine);
            }
        }

        private void OnRFE_PortClosed(object sender, EventArgs e)
        {
            ReportDebug("RF Explorer PortClosed");
        }

        private void m_chkDebug_CheckedChanged(object sender, EventArgs e)
        {
            if (m_chkDebug.Checked)
            {
                this.Size = new Size(this.Size.Width, 400);
                m_edRFEReportLog.Visible = true;
            }
            else
            {
                this.Size = new Size(this.Size.Width, 206);
                m_edRFEReportLog.Visible = false;
            }
        }
        #endregion

        #region RF Explorer handling
        private void UpdateButtonStatus()
        {
            btnConnectRFExplorer.Enabled = !m_objRFE.PortConnected && (comboBoxPortsRFExplorer.Items.Count > 0);
            btnDisconnectRFExplorer.Enabled = m_objRFE.PortConnected;
            comboBoxPortsRFExplorer.Enabled = !m_objRFE.PortConnected;
            comboBoxBaudrateRFExplorer.Enabled = !m_objRFE.PortConnected;
            btnRescanPortsRFExplorer.Enabled = !m_objRFE.PortConnected;

            if (!m_objRFE.PortConnected)
            {
                if (comboBoxBaudrateRFExplorer.SelectedItem == null)
                {
                    comboBoxBaudrateRFExplorer.SelectedItem = "500000";
                }
            }
        }

        private void btnConnectRFExplorer_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            if (comboBoxPortsRFExplorer.Items.Count > 0)
            {
                m_objRFE.ConnectPort(comboBoxPortsRFExplorer.SelectedValue.ToString(), Convert.ToInt32(comboBoxBaudrateRFExplorer.SelectedItem.ToString()));
                if (m_objRFE.PortConnected)
                {
                    m_objRFE.SendCommand_RequestConfigData();
                }
                Thread.Sleep(2000);
                m_objRFE.ProcessReceivedString(true, out m_sRFEReceivedString);
            }
            UpdateButtonStatus();
            Cursor.Current = Cursors.Default;
        }

        private void btnDisconnectRFExplorer_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            m_objRFE.ClosePort();
            UpdateButtonStatus();
            Cursor.Current = Cursors.Default;
        }

        private void GetConnectedPortsRFExplorer()
        {
            Cursor.Current = Cursors.WaitCursor;
            comboBoxPortsRFExplorer.DataSource = null;
            if (m_objRFE.GetConnectedPorts())
            {
                comboBoxPortsRFExplorer.DataSource = m_objRFE.ValidCP2101Ports;
            }
            UpdateButtonStatus();
            Cursor.Current = Cursors.Default;
        }

        private void btnRescanPortsRFExplorer_Click(object sender, EventArgs e)
        {
            GetConnectedPortsRFExplorer();
        }

        private void RefreshTimer_tick(object sender, EventArgs e)
        {
            if (m_objRFE.PortConnected)
            {
                m_objRFE.ProcessReceivedString(true, out m_sRFEReceivedString);
            }
        }
        #endregion

        #region example commands

        private void OnCommandA_Click(object sender, EventArgs e)
        {
            //Example command LCD ON, this command does not require wait for answer
            if (m_objRFE!=null && m_objRFE.PortConnected)
            {
                m_objRFE.SendCommand_ScreenON();
                Thread.Sleep(100); //Wait 100ms to wait for command to settle
            }
        }

        private void OnCommandB_Click(object sender, EventArgs e)
        {
            //Example command LCD OFF, this command does not require wait for answer
            if (m_objRFE != null && m_objRFE.PortConnected)
            {
                m_objRFE.SendCommand_ScreenOFF();
                Thread.Sleep(100); //Wait 100ms to wait for command to settle
            }
        }

        private void OnCommandC_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;

            //Example command new configuration request, requires wait before allowing user to change config again
            if (m_objRFE != null && m_objRFE.PortConnected)
            {
                //ask device to change configuration
                m_bNewConfigurationReceived = false;
                m_objRFE.UpdateDeviceConfig(584.800, 587.575, 0f, -120f);
                //wait for device to reconfigure
                while (!m_bNewConfigurationReceived)
                {
                    Thread.Sleep(100); //Wait 100ms
                    Application.DoEvents(); //process events to get new configuration
                }
                //ask device to change resolution
                m_bNewConfigurationReceived = false;
                m_objRFE.SendCommand_SweepDataPointsEx(5570);
                //wait for device to reconfigure
                while (!m_bNewConfigurationReceived)
                {
                    Thread.Sleep(100); //Wait 100ms
                    Application.DoEvents(); //process events to get new configuration
                }
            }

            Cursor.Current = Cursors.Default;
        }

        private void OnCommandD_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;

            //Example command new configuration request, requires wait before allowing user to change config again
            if (m_objRFE != null && m_objRFE.PortConnected)
            {
                //ask device to change configuration
                m_bNewConfigurationReceived = false;
                m_objRFE.UpdateDeviceConfig(580.000, 600.000, 0f, -120f);
                //wait for device to reconfigure
                while (!m_bNewConfigurationReceived)
                {
                    Thread.Sleep(100); //Wait 100ms
                    Application.DoEvents(); //process events to get new configuration
                }
                //ask device to change resolution
                m_bNewConfigurationReceived = false;
                m_objRFE.SendCommand_SweepDataPointsEx(1200);
                //wait for device to reconfigure
                while (!m_bNewConfigurationReceived)
                {
                    Thread.Sleep(100); //Wait 100ms
                    Application.DoEvents(); //process events to get new configuration
                }
            }

            Cursor.Current = Cursors.Default;
        }
        #endregion
    }
}
