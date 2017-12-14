//============================================================================
//RF Explorer for .NET - A Spectrum Analyzer for everyone!
//Copyright © 2010-17 Ariel Rocholl, www.rf-explorer.com
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Raspberry.IO.GeneralPurpose;
using System.Runtime.InteropServices;
using System.Reflection;
using RFExplorerCommunicator;

namespace RFE_ScanRange
{
    class Program
    {
        static bool g_bIoTBoard = false; //True if it is a Raspberry Pi IOT host (using Pi's UART), false for all other platforms (using USB connection)
        static RFECommunicator g_objRFE = null; //the one and only RFExplorer proxy object
        static int g_nBaudrate = 500000; //default baudrate should work in all platforms
        static GpioConnection m_pinConnection = null; //GPIO connections for a Raspberry Pi host for IOT board
        static bool g_bIgnoreSweeps = true; //this will signal when expected sweeps are correct configuration, earlier sweeps can be discarded
        static double g_fStartMHZ = 0.0f;
        static double g_fStopMHZ = 0.0f;
        static int g_nTotalSeconds = 3600; //1 hour by default
        static string g_sFileCSV = "";
        static string g_sFileRFE = "";
        static UInt32 g_nSweepCounter = 0;
        static UInt16 g_nResolutionPoints = 112;

        static void Main(string[] args)
        {
            //start application by displaying some help on screen
            PrintHelp(args.Length == 0);
            if (args.Length == 0)
                return; //no parameters, nothing to do, just finish here

            //Create and initialize a RFECommunicator object to proxy a RF Explorer device (of any kind)
            g_objRFE = new RFECommunicator(true, IsIOT(args)); //Initialize a new Spectrum Analyzer object
            g_objRFE.ReportInfoAddedEvent += new EventHandler(OnRFE_ReportLog);
            g_objRFE.ReceivedConfigurationDataEvent += new EventHandler(OnRFE_ReceivedConfigData);
            g_objRFE.UpdateDataEvent += new EventHandler(OnRFE_UpdateData);
            g_objRFE.AutoConfigure = false;

            UpdateBaudrate(args);

            //Connect to specified serial port
            if (!ConnectPort(args))
            {
                ClosePort();
                return; //if cannot connect to serial port, we cannot do anything else
            }

            //g_objRFE.DebugSentTracesEnabled = true;
            //g_objRFE.DebugTracesEnabled = true;

            //Request analyzer to send current configuration and enable sweep data dump
            if (UpdateFrequencyRange(args))
            {
                g_objRFE.SendCommand_RequestConfigData();
                UpdateSweepTime(args);
                WaitAndProcess(2, false);

                //Set working frequency range from command line parameters
                if (SetFrequencyRange(args))
                {
                    UpdateFileNames(args);
                    //If correct, process responses and display output for some seconds
                    WaitAndProcess(g_nTotalSeconds);
                }
            }

            //Close port and finish
            ClosePort();
        }

        /// <summary>
        /// Display a help screen
        /// </summary>
        /// <param name="bParameters">Include command line parameters (true) or display current envinronment details</param>
        static void PrintHelp(bool bParameters)
        {
            Console.WriteLine("RF Explorer ScanRange Tool - " + Assembly.GetExecutingAssembly().GetName().Version.ToString());
            Console.WriteLine("---------------------------------------" + Environment.NewLine);
            string sData = "";
            if (bParameters)
                sData =
                    "Format: " + Environment.NewLine +
                    "    Console_ConnectRange [/IOT | /p:PORT] [/csv:path] [/time:secs] StartRangeMHZ StopRangeMHZ" + Environment.NewLine +
                    "where: " + Environment.NewLine +
                    "    /IOT: Connect to a Raspberry Pi assembled RF Explorer IoT board" + Environment.NewLine +
                    "    /p: Connect to a USB port such as COM3 or /dev/ttyUSB0" + Environment.NewLine +
                    "        Using AUTO assumes only RF Explorer connected to USB port" + Environment.NewLine +
                    "    /s: [low|high] Baudrate speed being high=500Kbps default, and low=2400bps" + Environment.NewLine +
                    "    /csv: Output CSV filename (no extension) to save consecutive scans" + Environment.NewLine +
                    "    /rfe: Output .RFE binary filename (no extension) to save consecutive scans" + Environment.NewLine +
                    "    /time: total seconds to scan before close automatically. If not specified" + Environment.NewLine +
                    "       will scan 1 hour and close automatically." + Environment.NewLine +
                    "       If set to 0 will do one single scan and close automatically." + Environment.NewLine +
                    "       If set to -1 will run forever." + Environment.NewLine +
                    "    /res: [high|normal|low] resolution used for scan data eq to 4096, 1024 or 112 points" + Environment.NewLine +
                    "    StartRangeMHZ: is a number in range 15-2700MHz to start scan sweep" + Environment.NewLine +
                    "    StopRangeMHZ: is a number in range 15-2700MHz to stop scan sweep" + Environment.NewLine +
                    "       Note: Current limit for full range scanning is up to 1000MHz" + Environment.NewLine +
                    "             Some models and configurations may have a reduced range" + Environment.NewLine +
                    Environment.NewLine +
                    "Keyboard control: " + Environment.NewLine +
                    "    <Q> Finish application" + Environment.NewLine +
                    "    <LEFT> move scan range to low frequency equals to span" + Environment.NewLine +
                    "    <RIGHT> move scan range to high frequency equals to span" + Environment.NewLine;
            else
                sData =
                    "  Computer: " + Environment.MachineName + Environment.NewLine +
                    "  Username: " + Environment.UserDomainName + "\\" + Environment.UserName + Environment.NewLine +
                    "  Folder:   " + Environment.CurrentDirectory + Environment.NewLine +
                    "  Assembly: " + Assembly.GetCallingAssembly().FullName + Environment.NewLine +
                    "  OS:       " + Environment.OSVersion + Environment.NewLine +
                    "  Platform: " + Environment.OSVersion.Platform + Environment.NewLine +
                    "  CLR:      " + Environment.Version + Environment.NewLine;

            Console.WriteLine(sData);
        }

        static void ClosePort()
        {
            if (m_pinConnection != null)
            {
                m_pinConnection.Close();
                m_pinConnection = null;
            }
            g_objRFE.ClosePort();
            g_objRFE.Close();
            g_objRFE = null;
        }

        static void WaitAndProcess(int nSeconds, bool bCheckKeyboard=true)
        {
            bool bRunForever = false;
            if (nSeconds == -1)
                bRunForever = true;

            DateTime objTimeStart = DateTime.Now;
            do
            {
                string sDummy; //we do not use the string in this loop
                //Process all pending messages received from device
                g_objRFE.ProcessReceivedString(true, out sDummy);

                //check if user pressed any key
                if (bCheckKeyboard && Console.KeyAvailable)
                {
                    ConsoleKeyInfo cki = new ConsoleKeyInfo();
                    cki = Console.ReadKey(true);
                    bool bChangeSettings = false;
                    double fStartMHz = g_objRFE.StartFrequencyMHZ;
                    double fSpanMHZ = g_objRFE.CalculateFrequencySpanMHZ();
                    switch (cki.Key)
                    {
                        case ConsoleKey.Escape:
                        case ConsoleKey.Q:
                            //finish function and therefore end up closing the application
                            Console.WriteLine("<Q> - Closing the application...");
                            return;
                        case ConsoleKey.LeftArrow:
                            Console.WriteLine("<LEFT> - Updating scan to lower frequency...");
                            Console.WriteLine("Current configuration - Start:" + g_objRFE.StartFrequencyMHZ.ToString("f3") + "MHZ, Stop: " + g_objRFE.StopFrequencyMHZ.ToString("f3") + "MHz, Span:" + fSpanMHZ.ToString("f3"));
                            //Set new start frequency, keeping same span
                            fStartMHz = fStartMHz - fSpanMHZ;
                            //Check start frequency is in valid device range
                            if (fStartMHz < g_objRFE.MinFreqMHZ)
                                fStartMHz = g_objRFE.MinFreqMHZ;
                            bChangeSettings = true;
                            break;
                        case ConsoleKey.RightArrow:
                            Console.WriteLine("<RIGHT> - Updating scan to higher frequency...");
                            Console.WriteLine("Current configuration - Start:" + g_objRFE.StartFrequencyMHZ.ToString("f3") + "MHZ, Stop: " + g_objRFE.StopFrequencyMHZ.ToString("f3") + "MHz, Span:" + fSpanMHZ.ToString("f3"));
                            //Set new start frequency, keeping same span
                            fStartMHz = fStartMHz + fSpanMHZ;
                            //Check start frequency is in valid device range
                            if ((fStartMHz + fSpanMHZ) > g_objRFE.MaxFreqMHZ)
                                fStartMHz = g_objRFE.MaxFreqMHZ - fSpanMHZ;
                            bChangeSettings = true;
                            break;
                        default:
                            Console.WriteLine("Ignored key.");
                            break;
                    }
                    if (bChangeSettings)
                    {
                        //Send new configuration
                        g_fStartMHZ = fStartMHz; //change flag value to recognize when new configuration is already in place
                        g_fStopMHZ = fStartMHz + fSpanMHZ;
                        Console.WriteLine("New configuration - Start:" + g_fStartMHZ.ToString("f3") + "MHZ, Stop: " + g_fStopMHZ.ToString("f3") + "MHz");
                        g_bIgnoreSweeps = true;
                        g_objRFE.UpdateDeviceConfig(fStartMHz, g_fStopMHZ, g_objRFE.AmplitudeTopDBM, g_objRFE.AmplitudeBottomDBM);
                    }
                }
            } while (bRunForever || ((DateTime.Now - objTimeStart).TotalSeconds < nSeconds));
            //save file before closing, if any.
            SaveRFEFile();
        }

        static void OnRFE_ReceivedConfigData(object sender, EventArgs e)
        {
            Console.WriteLine("-------- Received config settings");
            if (!g_bIgnoreSweeps)
                SaveRFEFile();
            //we do not want mixed data sweep values, so clean old ones
            g_objRFE.SweepData.CleanAll();
            //Check if configuration being received is that being expected
            if (Math.Abs(g_objRFE.StartFrequencyMHZ - g_fStartMHZ) < 0.001)
                g_bIgnoreSweeps = false;
        }

        static void SaveRFEFile()
        {
            if (!String.IsNullOrEmpty(g_sFileRFE) && g_objRFE.SweepData.Count>0)
            {
                string sFile = g_sFileRFE + "_" + g_nSweepCounter.ToString("000") + "_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss") + ".rfe";
                g_objRFE.SaveFileRFE(sFile, false);
                Console.WriteLine("Saved file " + sFile);
            }
        }

        static void OnRFE_UpdateData(object sender, EventArgs e)
        {
            RFESweepData objSweep = g_objRFE.SweepData.GetData(g_objRFE.SweepData.Count - 1);
            if (!g_bIgnoreSweeps)
            {
                g_nSweepCounter++;
                ushort nPeak = objSweep.GetPeakDataPoint();
                Console.WriteLine("Sweep: " + g_objRFE.SweepData.Count.ToString("D3") + " Peak: " + objSweep.GetFrequencyMHZ(nPeak).ToString("f3") + "MHz " + objSweep.GetAmplitudeDBM(nPeak).ToString("f1") + "dBm");
                if (!String.IsNullOrEmpty(g_sFileCSV))
                {
                    string sFile = g_sFileCSV + "_" + g_nSweepCounter.ToString("0000") + "_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss") + ".csv";
                    objSweep.SaveFileCSV(sFile, '\t', null);
                    Console.WriteLine("Saved file " + sFile);
                }
                if (g_objRFE.SweepData.Count>100)
                {
                    SaveRFEFile();
                    g_objRFE.SweepData.CleanAll();
                }
            }
            //else
            //{
            //    Console.WriteLine("Ignored sweep " + objSweep.Dump());
            //}
        }

        static bool UpdateFrequencyRange(string[] args)
        {
            bool bOk = true;

            //check command line frequency range arguments
            double fStartMHZ = 0.0f;
            double fStopMHZ = 0.0f;
            foreach (string sVal in args)
            {
                if (sVal.Contains("/"))
                    continue;

                if (fStartMHZ == 0.0f)
                {
                    //Get first frequency value
                    fStartMHZ = Convert.ToDouble(sVal);
                }
                else
                {
                    //Get the second frequency value
                    fStopMHZ = Convert.ToDouble(sVal);
                    //Check frequency value is valid
                    bOk = fStopMHZ > fStartMHZ;
                    if (!bOk)
                        Console.WriteLine("ERROR: Stop frequency lower than Start frequ!");
                    break; //finish after 2nd value found
                }

                //if errors, no need to go on anymore
                if (!bOk)
                    break;
            }

            //Check if values were initialized
            bOk = bOk && (fStartMHZ > 0.0f) && (fStopMHZ > 0.0f);
            if (!bOk)
                Console.WriteLine("ERROR: Start/Stop frequency values missing or out of range");

            if (bOk)
            {
                g_fStartMHZ = fStartMHZ; //change flag value to recognize when new configuration is already in place
                g_fStopMHZ = fStopMHZ;
                g_bIgnoreSweeps = true;
            }

            return bOk;
        }

        static bool SetFrequencyRange(string[] args)
        {
            bool bOk = true;

            UpdateSweepPoints(args);
            WaitAndProcess(1, false);

            //Check frequency value is valid
            bOk = g_fStartMHZ >= g_objRFE.MinFreqMHZ;
            bOk = bOk && g_fStartMHZ < g_objRFE.MaxFreqMHZ;
            if (!bOk)
                Console.WriteLine("ERROR: Start frequency is outside range!");
            else
            {
                bOk = bOk && g_fStopMHZ <= g_objRFE.MaxFreqMHZ;
                if (!bOk)
                    Console.WriteLine("ERROR: Stop frequency is outside range!");
            }
            if (bOk)
                g_objRFE.UpdateDeviceConfig(g_fStartMHZ, g_fStopMHZ);

            return bOk;
        }

        ///res: [high|normal|low]
        static void UpdateSweepPoints(string[] args)
        {
            string sRes = FindOptionValue(args, "/res:");
            if (!String.IsNullOrEmpty(sRes))
            {
                switch (sRes)
                {
                    case "high": g_nResolutionPoints = 4096; break;
                    case "low": g_nResolutionPoints = 112; break;

                    default:
                        if (g_bIoTBoard)
                            g_nResolutionPoints = 1024;
                        else
                            g_nResolutionPoints = 112;
                        break;

                    case "normal": g_nResolutionPoints = 1024; break;
                }
            }
            Console.WriteLine("Resolution sweep points: " + g_nResolutionPoints);
            g_objRFE.SendCommand_SweepDataPoints(g_nResolutionPoints);
        }

        static void UpdateSweepTime(string[] args)
        {
            string sTime = FindOptionValue(args, "/time:");
            if (!String.IsNullOrEmpty(sTime))
            {
                g_nTotalSeconds = Convert.ToInt32(sTime);
                Console.WriteLine("Time option: " + g_nTotalSeconds + " seconds");
            }
        }

        static void UpdateBaudrate(string[] args)
        {
            string sBaudrate = FindOptionValue(args, "/s:");
            if (!String.IsNullOrEmpty(sBaudrate))
            {
                if (sBaudrate == "low")
                    g_nBaudrate = 2400;
                else
                    g_nBaudrate = 500000;
                Console.WriteLine("Baudrate speed: " + g_nBaudrate);
            }
        }


        static void UpdateFileNames(string[] args)
        {
            string sFile = FindOptionValue(args, "/csv:");
            if (!String.IsNullOrEmpty(sFile))
            {
                g_sFileCSV = sFile;
                Console.WriteLine("CSV file path: " + g_sFileCSV);
            }

            sFile = FindOptionValue(args, "/rfe:");
            if (!String.IsNullOrEmpty(sFile))
            {
                g_sFileRFE = sFile;
                Console.WriteLine("RFE file path: " + g_sFileRFE);
            }
        }

        static string FindOptionValue(string[] args, string sOption)
        {
            string sResult = "";
            //Use specified port from command line
            int nPos = Array.FindIndex(args, x => x.StartsWith(sOption, StringComparison.Ordinal));
            if (nPos >= 0)
            {
                sResult = args[nPos].Replace(sOption, "");
            }
            return sResult;
        }

        static bool IsIOT(string[] args)
        {
            if (args.Contains("/IOT", StringComparer.Ordinal))
            {
                //This is a Raspberry Pi with a RF Explorer IoT Hat
                g_bIoTBoard = (RFECommunicator.IsRaspberry());
                Console.WriteLine("Working in IoT - Raspberry Pi mode");
                if (!g_bIoTBoard)
                {
                    Console.WriteLine("ERROR: Unrecognized Raspberry Pi platform");
                    return false;
                }
                return true;
            }
            return false;
        }

        static bool ConnectPort(string[] args)
        {
            //Connect to the right UART port (may be USB in Windows/Unix/Mac or a Raspberry Mainboard)
            if (g_bIoTBoard)
            {
                //Define pins to control baudrate (GPIO2 on Pin21) and force a HW reset of the MWSUB3G (Pin12)
                OutputPinConfiguration pinGPIO2 = ConnectorPin.P1Pin21.Output();
                m_pinConnection = new GpioConnection(pinGPIO2);
                OutputPinConfiguration pinRESET = ConnectorPin.P1Pin12.Output();
                m_pinConnection.Add(pinRESET);

                //Reset sequence
                m_pinConnection[pinRESET] = false;
                Thread.Sleep(100);
                m_pinConnection[pinGPIO2] = true; //true for 500Kbps, change to false for 2400bps low speed
                m_pinConnection[pinRESET] = true;
                Thread.Sleep(2500); //wait for initialization firmware code to finish startup
                
                //Open COM port from Raspberry mainboard
                string sCOMPort = "/dev/ttyAMA0";
                g_objRFE.ConnectPort(sCOMPort, g_nBaudrate, true);
                Console.WriteLine("Connected to port " + sCOMPort);
            }
            else if (args.Contains("/p:AUTO", StringComparer.Ordinal))
            {
                //This is any non-IoT platform with a single device connected to USB
                if (g_objRFE.GetConnectedPorts())
                {
                    if (g_objRFE.ValidCP2101Ports.Length == 1)
                    {
                        bool bForceBaudrate = (RFECommunicator.IsRaspberry() && g_nBaudrate > 115200);
                        g_objRFE.ConnectPort(g_objRFE.ValidCP2101Ports[0], g_nBaudrate, RFECommunicator.IsUnixLike() && !RFECommunicator.IsMacOS(), bForceBaudrate);
                    }
                }
                if (g_objRFE.PortConnected)
                    Console.WriteLine("Connected to port " + g_objRFE.ValidCP2101Ports[0]);
                else
                {
                    Console.WriteLine("ERROR: no port available, please review your connection");
                    return false;
                }
            }
            else
            {
                //Use specified port from command line
                int nPos = Array.FindIndex(args, x => x.StartsWith("/p:"));
                if (nPos >= 0)
                {
                    string sCOMPort = args[nPos].Replace("/p:","");
                    Console.WriteLine("Trying manual port: " + sCOMPort);
                    g_objRFE.ConnectPort(sCOMPort, g_nBaudrate, RFECommunicator.IsUnixLike() && !RFECommunicator.IsMacOS());
                    Console.WriteLine("Connected to port " + sCOMPort);
                }
            }

            return g_objRFE.PortConnected;
        }
        static void OnRFE_ReportLog(object sender, EventArgs e)
        {
            EventReportInfo objArg = (EventReportInfo)e;
            string sLine = objArg.Data;
            if (!sLine.StartsWith("::DBG"))
                Console.WriteLine(sLine);
        }
    }
}
