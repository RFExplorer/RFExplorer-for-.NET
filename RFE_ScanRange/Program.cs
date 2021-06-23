//============================================================================
//RF Explorer for .NET - A Spectrum Analyzer for everyone!
//Copyright © 2010-21 RF Explorer Technologies SL, www.rf-explorer.com
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
using System.Linq;
using System.Threading;
using Raspberry.IO.GeneralPurpose;
using System.Reflection;
using RFExplorerCommunicator;
using System.Diagnostics;
using Console_ConnectRange;
using System.IO;

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
        static int g_nTotalFiles = 0;//changes its value when /count is an argument
        static bool g_bCountActive = false; //true if /count is an argument
        static bool g_bTimeActive = false; //true if /time is an argument
        static bool g_bHighSpeed = true;   //True if 500kbps and false ig 2400bps, used in RPi PinOut
        //Note: g_bCountActive and g_bTimeActive can not be true at the same time

        static void Main(string[] args)
        {
            //start application by displaying some help on screen
            TextWriterTraceListener objConsole = new ReportTraceListener(Console.Out);
            Trace.Listeners.Add(objConsole);
            PrintHelp(args.Length == 0);
            if (args.Length == 0)
                return; //no parameters, nothing to do, just finish here                       

            //Create and initialize a RFECommunicator object to proxy a RF Explorer device (of any kind)
            g_objRFE = new RFECommunicator(true, IsIOT(args)); //Initialize a new Spectrum Analyzer object
            g_objRFE.ReportInfoAddedEvent += new EventHandler(OnRFE_ReportLog);
            g_objRFE.ReceivedConfigurationDataEvent += new EventHandler(OnRFE_ReceivedConfigData);
            g_objRFE.UpdateDataEvent += new EventHandler(OnRFE_UpdateData);
            g_objRFE.AutoConfigure = false;
            g_objRFE.AutoClose = true;

            UpdateBaudrate(args);

            //Connect to specified serial port
            if (!ConnectPort(args))
            {
                ClosePort();
                return; //if cannot connect to serial port, we cannot do anything else
            }


            //Request analyzer to send current configuration and enable sweep data dump
            if (UpdateFrequencyRange(args))
            {
                g_objRFE.SendCommand_RequestConfigData();
                UpdateNumberOfCsv(args);
                UpdateSweepTime(args);

                if (g_bTimeActive && g_bCountActive)
                    Trace.WriteLine("ERROR: Incompatibility of parameters /time and /count");
                else
                {
                    WaitAndProcess(2, false);
                    DeviceInputStage(args);
                    //Set working frequency range from command line parameters
                    if (SetFrequencyRange(args))
                    {
                        UpdateFileNames(args);
                        //If correct, process responses and display output for some seconds
                        WaitAndProcess(g_nTotalSeconds, true, g_bCountActive);
                    }
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
            Trace.WriteLine("RF Explorer ScanRange Tool - " + Assembly.GetExecutingAssembly().GetName().Version.ToString());
            Trace.WriteLine("---------------------------------------" + Environment.NewLine);
            string sData = "";
            if (bParameters)
            {
                sData =
                   "Format: " + Environment.NewLine +
                   "    </IOT | /p:PORT> [/s:[low|high]] [/csv:path] [/rfe:path] [/time:secs] [/res: [low|normal|high]] [/input:[dir|att|lna]] StartRangeMHZ  StopRangeMHZ " + Environment.NewLine +
                   Environment.NewLine +
                   "where: " + Environment.NewLine +
                   "    /s: [low|high] Baudrate speed being high=500Kbps (default) and low=2400bps" + Environment.NewLine +
                   "    /csv: Output CSV filename (no extension) to save consecutive scans" + Environment.NewLine +
                   "    /rfe: Output .RFE binary filename (no extension) to save consecutive scans" + Environment.NewLine +
                   "    /time: Total seconds to scan before close automatically. If not specified" + Environment.NewLine +
                   "           will scan 1 hour and close automatically." + Environment.NewLine +
                   "           If set to 0 will do one single scan and close automatically." + Environment.NewLine +
                   "           If set to -1 will run forever." + Environment.NewLine +
                   "           NOTE: Only one of / count or / time can be used as argument." + Environment.NewLine +
                   "    /count: Number files to save before close automatically." + Environment.NewLine +
                   "            NOTE: Only one of /count or /time can be used as argument." + Environment.NewLine +
                   "    /res: [high|normal|low] resolution used for scan data eq to 4096, 1024 or 112 points" + Environment.NewLine +
                   "    /input: [dir|att|lna] Select device input stage as dir=Direct Input, att=Attenuator 30dB or lna=Amplifier 25dB" + Environment.NewLine +
                   Environment.NewLine +
                   "Required Parameters:" + Environment.NewLine +
                   "    /IOT: Connect to a Raspberry Pi assembled RF Explorer IoT board" + Environment.NewLine +
                   "    /p: Connect to a USB port such as COM3 or /dev/ttyUSB0" + Environment.NewLine +
                   "        Using AUTO assumes only RF Explorer device connected to USB port" + Environment.NewLine +
                   "    NOTE: One of / IOT or / p parameters are required" + Environment.NewLine +
                   "    StartRangeMHZ: Is Start Frequency in MHz number to start scan sweep" + Environment.NewLine +
                   "    StopRangeMHZ: Is Stop Frequency in MHz number to stop scan sweep" + Environment.NewLine +
                   "    NOTE: Current limit for full range scanning is up to 1000MHz" + Environment.NewLine +
                   "          Some models and configurations may have a reduced range" + Environment.NewLine +
                   Environment.NewLine +
                   "Keyboard control: " + Environment.NewLine +
                   "    <Q> Finish application" + Environment.NewLine +
                   "    <LEFT> Move scan range to low frequency equals to span" + Environment.NewLine +
                   "    <RIGHT> Move scan range to high frequency equals to span" + Environment.NewLine;
            }

            else
                sData =
                    "  Computer: " + Environment.MachineName + Environment.NewLine +
                    "  Username: " + Environment.UserDomainName + "\\" + Environment.UserName + Environment.NewLine +
                    "  Folder:   " + Environment.CurrentDirectory + Environment.NewLine +
                    "  Assembly: " + Assembly.GetCallingAssembly().FullName + Environment.NewLine +
                    "  OS:       " + Environment.OSVersion + Environment.NewLine +
                    "  Platform: " + Environment.OSVersion.Platform + Environment.NewLine +
                    "  CLR:      " + Environment.Version + Environment.NewLine;

            Trace.WriteLine(sData);
        }

        /// <summary>
        /// Close specific serial port and finish
        /// </summary>
        static void ClosePort()
        {
            if (m_pinConnection != null)
            {
                m_pinConnection.Close();
                m_pinConnection = null;
            }
            g_objRFE.Close();
            g_objRFE.Dispose();
            //g_objRFE = null;
        }

        /// <summary>
        /// During this time process, user can write on the keyboard if it is active and it is checking keyboard.
        /// If there is any new setting, it will sending it to the device
        /// If bCountActive is active and g_nTotalFiles, number of files .csv created is equal to 0, then the function ends.
        /// </summary>
        /// <param name="nSeconds"></param>
        /// <param name="bCheckKeyboard"></param>
        /// <param name="bCountActive"> This means "/count:" is an argument. False by default </param>
        static void WaitAndProcess(int nSeconds, bool bCheckKeyboard = true, bool bCountActive = false)
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

                if (bCountActive && g_nTotalFiles == 0)
                    break;
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
                            Trace.WriteLine("<Q> - Closing the application...");
                            return;
                        case ConsoleKey.LeftArrow:
                            Trace.WriteLine("<LEFT> - Updating scan to lower frequency...");
                            Trace.WriteLine("Current configuration - Start:" + g_objRFE.StartFrequencyMHZ.ToString("f3") + "MHZ, Stop: " + g_objRFE.StopFrequencyMHZ.ToString("f3") + "MHz, Span:" + fSpanMHZ.ToString("f3"));
                            //Set new start frequency, keeping same span
                            fStartMHz = fStartMHz - fSpanMHZ;
                            //Check start frequency is in valid device range
                            if (fStartMHz < g_objRFE.MinFreqMHZ)
                                fStartMHz = g_objRFE.MinFreqMHZ;
                            bChangeSettings = true;
                            break;
                        case ConsoleKey.RightArrow:
                            Trace.WriteLine("<RIGHT> - Updating scan to higher frequency...");
                            Trace.WriteLine("Current configuration - Start:" + g_objRFE.StartFrequencyMHZ.ToString("f3") + "MHZ, Stop: " + g_objRFE.StopFrequencyMHZ.ToString("f3") + "MHz, Span:" + fSpanMHZ.ToString("f3"));
                            //Set new start frequency, keeping same span
                            fStartMHz = fStartMHz + fSpanMHZ;
                            //Check start frequency is in valid device range
                            if ((fStartMHz + fSpanMHZ) > g_objRFE.MaxFreqMHZ)
                                fStartMHz = g_objRFE.MaxFreqMHZ - fSpanMHZ;
                            bChangeSettings = true;
                            break;
                        default:
                            Trace.WriteLine("Ignored key.");
                            break;
                    }
                    if (bChangeSettings)
                    {
                        //Send new configuration
                        g_fStartMHZ = fStartMHz; //change flag value to recognize when new configuration is already in place
                        g_fStopMHZ = fStartMHz + fSpanMHZ;
                        Trace.WriteLine("New configuration - Start:" + g_fStartMHZ.ToString("f3") + "MHZ, Stop: " + g_fStopMHZ.ToString("f3") + "MHz");
                        g_bIgnoreSweeps = true;
                        g_objRFE.UpdateDeviceConfig(fStartMHz, g_fStopMHZ, g_objRFE.AmplitudeTopDBM, g_objRFE.AmplitudeBottomDBM);
                    }
                }
            } while ((bRunForever || ((DateTime.Now - objTimeStart).TotalSeconds < nSeconds)));
            //save file before closing, if any.
            SaveRFEFile();
        }

        static void OnRFE_ReceivedConfigData(object sender, EventArgs e)
        {
            Trace.WriteLine("-------- Received config settings");
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
            if (!String.IsNullOrEmpty(g_sFileRFE) && g_objRFE.SweepData.Count > 0)
            {
                string sFile = g_sFileRFE + "_" + g_nSweepCounter.ToString("000") + "_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss") + ".rfe";
                g_objRFE.SaveFileRFE(sFile, false);
                Trace.WriteLine("Saved file " + sFile);
            }
        }

        static void OnRFE_UpdateData(object sender, EventArgs e)
        {
            RFESweepData objSweep = g_objRFE.SweepData.GetData(g_objRFE.SweepData.Count - 1);
            if (!g_bIgnoreSweeps)
            {
                g_nSweepCounter++;
                ushort nPeak = objSweep.GetPeakDataPoint();
                Trace.WriteLine("Sweep: " + g_objRFE.SweepData.Count.ToString("D3") + " Peak: " + objSweep.GetFrequencyMHZ(nPeak).ToString("f3") + "MHz " + objSweep.GetAmplitudeDBM(nPeak).ToString("f1") + "dBm");
                if (!String.IsNullOrEmpty(g_sFileCSV))
                {
                    if (!g_bCountActive || (g_nTotalFiles > 0)) //if g_bCountActive generate files only if g_nTotalFiles is greater than 0
                    {
                        string sFile = g_sFileCSV + "_" + g_nSweepCounter.ToString("0000") + "_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss") + ".csv";
                        objSweep.SaveFileCSV(sFile, '\t', null);
                        Trace.WriteLine("Saved file " + sFile);
                        if (g_nTotalFiles > 0)
                            g_nTotalFiles--; //reduce pending files to generate
                    }
                }
                if (g_objRFE.SweepData.Count > 100)
                {
                    SaveRFEFile();
                    g_objRFE.SweepData.CleanAll();
                }
            }
        }

        /// <summary>
        /// Update frequency range according to "StartRangeMHZ" (first value) and "StopRangeMHZ" (next value)
        /// then check if "StopRangeMHZ" i greater than "StartRangeMHZ" this means that it is a valid range
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static bool UpdateFrequencyRange(string[] args)
        {
            bool bOk = true;
            try
            {
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
                            Trace.WriteLine("ERROR: Stop frequency lower or equal than Start frequency");
                        break; //finish after 2nd value found
                    }

                    //if errors, no need to go on anymore
                    if (!bOk)
                        break;
                }

                //Check if values were initialized
                bOk = bOk && (fStartMHZ > 0.0f) && (fStopMHZ > 0.0f);
                if (!bOk)
                    Trace.WriteLine("ERROR: Start/Stop frequency values missing or out of range");

                if (bOk)
                {
                    g_fStartMHZ = fStartMHZ; //change flag value to recognize when new configuration is already in place
                    g_fStopMHZ = fStopMHZ;
                    g_bIgnoreSweeps = true;
                }
            }
            catch (Exception objExc)
            {
                bOk = false;
                Trace.WriteLine("ERROR: Frequency - " + objExc.ToString());
            }
            return bOk;
        }

        /// <summary>
        /// Sets the frequency range updating configuration of the device
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static bool SetFrequencyRange(string[] args)
        {
            bool bOk = true;
            UpdateSweepPoints(args);
            WaitAndProcess(1, false);
            //Check device connection, when device baudrate is different than COM port speed
            if (g_objRFE.MainBoardModel == RFECommunicator.eModel.MODEL_NONE)
            {
                Trace.WriteLine("ERROR: Unknown connection, please, review RF Explorer device.");
                return false;
            }
            //Check frequency value is valid
            Trace.WriteLine("Start/Stop: " + g_fStartMHZ.ToString() + "/" + g_fStopMHZ.ToString());
            Trace.WriteLine("Start_min/Stop_max: " + g_objRFE.MinFreqMHZ.ToString() + "/" + g_objRFE.MaxFreqMHZ.ToString());
            bOk = g_fStartMHZ >= g_objRFE.MinFreqMHZ;
            bOk = bOk && g_fStartMHZ < g_objRFE.MaxFreqMHZ;
            if (!bOk)
                Trace.WriteLine("ERROR: Start frequency is outside range!");
            else
            {
                bOk = bOk && g_fStopMHZ <= g_objRFE.MaxFreqMHZ;
                if (!bOk)
                    Trace.WriteLine("ERROR: Stop frequency is outside range!");
            }

            //Check if allowed span
            if (bOk)
            {
                bOk = bOk && (g_objRFE.MinSpanMHZ - (g_fStopMHZ - g_fStartMHZ) <= 0.001);
                if (!bOk)
                    Trace.WriteLine("ERROR: Span is lower than Minimum Span allowed: " + g_objRFE.MinSpanMHZ.ToString("f3"));
                else
                {

                    bOk = bOk && ((g_fStopMHZ - g_fStartMHZ) - g_objRFE.MaxSpanMHZ <= 0.001);
                    if (!bOk)
                        Trace.WriteLine("ERROR: Span is higher than Maximum Span allowed: " + g_objRFE.MaxSpanMHZ.ToString("f3"));
                }
            }

            if (bOk)
                g_objRFE.UpdateDeviceConfig(g_fStartMHZ, g_fStopMHZ);

            return bOk;
        }

        /// <summary>
        /// Checks if "/res:" is an argument and after ":" should be [high|normal|low] in each case, g_nResolutionPoints takes a different value
        /// </summary>
        /// <param name="args"></param>
        static void UpdateSweepPoints(string[] args)
        {
            string sRes = FindOptionValue(args, "/res:");
            if (!String.IsNullOrEmpty(sRes))
            {   //Sweep data points resolution devices allowed (not SA 3G)
                if (g_objRFE.IsHighResAvailable && !g_objRFE.ExpansionBoardActive || g_bIoTBoard)
                {
                    switch (sRes)
                    {
                        case "high": g_nResolutionPoints = 4096; break;
                        case "normal": g_nResolutionPoints = 1024; break;
                        case "low": g_nResolutionPoints = 112; break;
                        default:
                            if (g_bIoTBoard)
                                g_nResolutionPoints = 1024;
                            else
                                g_nResolutionPoints = 112;
                            break;
                    }
                }
            }
            Trace.WriteLine("Resolution sweep points: " + g_nResolutionPoints);
            g_objRFE.SendCommand_SweepDataPoints(g_nResolutionPoints);
        }

        /// <summary>
        /// Checks if "/count:" is an argument, if it is then checks if the number after ":" is greater than 0 and notnull or empty, then puts g_bCountActive=true and saves the number in g_nTotalFiles
        /// </summary>
        /// <param name="args"></param>
        static void UpdateNumberOfCsv(string[] args)
        {
            string sCount = FindOptionValue(args, "/count:");
            if (!String.IsNullOrEmpty(sCount) && Convert.ToInt32(sCount) > 0)
            {
                g_bCountActive = true;
                g_nTotalFiles = Convert.ToInt32(sCount);
                Trace.WriteLine("Count option: " + g_nTotalFiles + " files");
            }
        }

        /// <summary>
        /// Checks if "/time:" is an argument. If it is, saves the number after ":" in g_nTotalSeconds and puts g_bTimeActive = true; 
        /// if is not an argument, g_nTotalSeconds has 3600 as default value and g_bTimeActive = false
        /// </summary>
        /// <param name="args"></param>
        static void UpdateSweepTime(string[] args)
        {
            string sTime = FindOptionValue(args, "/time:");
            if (!String.IsNullOrEmpty(sTime))
            {
                g_bTimeActive = true;
                g_nTotalSeconds = Convert.ToInt32(sTime);
                Trace.WriteLine("Time option: " + g_nTotalSeconds + " seconds");
            }
        }

        /// <summary>
        /// Checks if "/s:" is an argument, if after ":" it's written low, g_nBaudrate = 2400 bps, else g_nBaudrate = 500000 bps
        /// </summary>
        /// <param name="args"></param>
        static void UpdateBaudrate(string[] args)
        {
            string sBaudrate = FindOptionValue(args, "/s:");
            if (!String.IsNullOrEmpty(sBaudrate))
            {
                if (sBaudrate == "low")
                {
                    g_nBaudrate = 2400;
                    g_bHighSpeed = false;
                }
                else //when it is not low but not high, it is deafault (high)
                {
                    g_nBaudrate = 500000;
                    g_bHighSpeed = true;
                }
                Trace.WriteLine("Baudrate speed: " + g_nBaudrate);
            }
        }

        /// <summary>
        /// Checks if "/csv:" and "/rfe:" are found as arguments, then saves the new name/path indicated after ":"
        /// </summary>
        /// <param name="args"></param>
        static void UpdateFileNames(string[] args)
        {
            string sFile = FindOptionValue(args, "/csv:");
            if (!String.IsNullOrEmpty(sFile))
            {
                g_sFileCSV = sFile;
                Trace.WriteLine("CSV file path: " + g_sFileCSV);
            }

            sFile = FindOptionValue(args, "/rfe:");
            if (!String.IsNullOrEmpty(sFile))
            {
                g_sFileRFE = sFile;
                Trace.WriteLine("RFE file path: " + g_sFileRFE);
            }
        }

        /// <summary>
        /// find option value after a string "sOption" in an array of string (arguments)
        /// </summary>
        /// <param name="args"></param>
        /// <param name="sOption"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Function that checks if one of the arguments is /IOT
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static bool IsIOT(string[] args)
        {
            bool bIsIoT = false;
            if (args.Contains("/IOT", StringComparer.OrdinalIgnoreCase))
            {
                //This is a Raspberry Pi with a RF Explorer IoT Hat
                g_bIoTBoard = (RFECommunicator.IsRaspberryPlatform());
                Trace.WriteLine("Working in IoT - Raspberry Pi mode");
                if (!g_bIoTBoard)
                {
                    Trace.WriteLine("ERROR IoT: Unrecognized Raspberry Pi platform");
                    bIsIoT = false;
                }
                bIsIoT = true;
            }
            return bIsIoT;
        }

        /// <summary>
        /// Connect to specified serial port
        /// It could be a IOTBoard, connect automatically if it is an device connected by USB or select a specified port from command line
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static bool ConnectPort(string[] args)
        {
            //Connect to the right UART port (may be USB in Windows/Unix/Mac or a Raspberry Mainboard)
            if (g_bIoTBoard)
            {
                try
                {
                    //Define pins to control baudrate (GPIO2 on Pin21) and force a HW reset of the MWSUB3G (Pin12)
                    OutputPinConfiguration pinGPIO2 = ConnectorPin.P1Pin21.Output();
                    m_pinConnection = new GpioConnection(pinGPIO2);
                    OutputPinConfiguration pinRESET = ConnectorPin.P1Pin12.Output();
                    m_pinConnection.Add(pinRESET);

                    //Reset sequence
                    m_pinConnection[pinRESET] = false;
                    Thread.Sleep(100);
                    m_pinConnection[pinGPIO2] = g_bHighSpeed; //true for 500Kbps, change to false for 2400bps low speed
                    m_pinConnection[pinRESET] = true;
                    Thread.Sleep(2500); //wait for initialization firmware code to finish startup

                    //Open COM port from Raspberry mainboard
                    string sCOMPort = "/dev/ttyAMA0";
                    g_objRFE.ConnectPort(sCOMPort, g_nBaudrate, true);
                    Trace.WriteLine("Connected to port " + sCOMPort);
                    Thread.Sleep(500);
                }
                catch
                {
                    Trace.WriteLine("ERROR: Unable to connect IoT device");
                }
            }
            else if (args.Contains("/p:AUTO", StringComparer.OrdinalIgnoreCase))    //Accept /p:auto or /p:AUTO
            {
                //This is any non-IoT platform with a single device connected to USB
                if (g_objRFE.GetConnectedPorts())
                {
                    if (g_objRFE.ValidCP2101Ports.Length == 1)
                    {
                        bool bForceBaudrate = (RFECommunicator.IsUnixLike() && !RFECommunicator.IsMacOSPlatform());
                        g_objRFE.ConnectPort(g_objRFE.ValidCP2101Ports[0], g_nBaudrate, RFECommunicator.IsUnixLike(), bForceBaudrate);
                    }
                }
                if (g_objRFE.PortConnected)
                    Trace.WriteLine("Connected to port " + g_objRFE.ValidCP2101Ports[0]);
                else
                {
                    Trace.WriteLine("ERROR: No port available or several connections detected." + Environment.NewLine + "Please, review your RF Explorer connection");
                    return false;
                }
            }
            else
            {
                //Use specified port from command line
                int nPos = Array.FindIndex(args, x => x.StartsWith("/p:"));
                if (nPos >= 0)
                {
                    string sCOMPort = args[nPos].Replace("/p:", "");
                    Trace.WriteLine("Trying manual port: " + sCOMPort);
                    g_objRFE.ConnectPort(sCOMPort, g_nBaudrate, RFECommunicator.IsUnixLike(), RFECommunicator.IsUnixLike() && !RFECommunicator.IsMacOSPlatform());
                    Trace.WriteLine("Connected to port " + sCOMPort);
                }
                else
                    Trace.WriteLine("ERROR: Please, insert /IOT or /p:PORT parameter");
            }
            return g_objRFE.PortConnected;
        }

        static void OnRFE_ReportLog(object sender, EventArgs e)
        {
            EventReportInfo objArg = (EventReportInfo)e;
            string sLine = objArg.Data;
            if (!sLine.StartsWith("::DBG"))
            {
                Trace.WriteLine(sLine);
            }

        }

        /// <summary>
        /// Select input stage device
        /// </summary>
        /// <param name="args"></param>
        static void DeviceInputStage(string[] args)
        {
            try
            {
                string sInputStage = string.Empty;
                if (!String.IsNullOrEmpty(sInputStage = FindOptionValue(args, "/input:")))
                {
                    WaitAndProcess(1, true, g_bCountActive);//Must wait a maximum of 1 second for that RFECommunicator update data before changing input stage. 


                    if (g_objRFE.IsInputStageAvailable && !g_objRFE.ExpansionBoardActive || g_bIoTBoard)
                    {
                        switch (sInputStage)
                        {
                            case "att":
                                if (g_objRFE.InputStage != RFECommunicator.eInputStage.Attenuator_30dB)
                                    g_objRFE.InputStage = RFECommunicator.eInputStage.Attenuator_30dB;
                                break;
                            case "lna":
                                if (g_objRFE.InputStage != RFECommunicator.eInputStage.LNA_25dB)
                                    g_objRFE.InputStage = RFECommunicator.eInputStage.LNA_25dB;
                                break;
                            case "dir":
                                if (g_objRFE.InputStage != RFECommunicator.eInputStage.Direct)
                                    g_objRFE.InputStage = RFECommunicator.eInputStage.Direct;
                                break;
                            default:
                                Trace.WriteLine("ERROR: Value incorrect in Input Stage");
                                break;
                        }
                    }
                    else
                        Trace.WriteLine("ERROR: Input Stage not supported");
                }
            }
            catch (Exception objEx)
            {
                Trace.WriteLine("ERROR: Set Input Stage - " + objEx.Message);
            }
        }
    }
}
