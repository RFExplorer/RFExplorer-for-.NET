//============================================================================
//RF Explorer for Windows - A Handheld Spectrum Analyzer for everyone!
//Copyright © 2010-16 Ariel Rocholl, www.rf-explorer.com
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
using System.Text;

namespace RFExplorerCommunicator
{
    public partial class RFECommunicator : IDisposable
    {
        private class RFEConfiguration
        {
            public double fStartMHZ; //it is also used as RefFrequency for sniffer and other modes
            public double fStepMHZ;
            public double fAmplitudeTopDBM;
            public double fAmplitudeBottomDBM;
            public UInt16 nFreqSpectrumSteps;
            public bool bExpansionBoardActive;
            public eMode eMode;
            public eModulation eModulation;
            public double fMinFreqMHZ;
            public double fMaxFreqMHZ;
            public double fMaxSpanMHZ;
            public double fRBWKHZ;
            public float fOffset_dB;
            public string sLineString;
            public eCalculator eCalculator;
            public UInt32 nBaudrate;
            public float fThresholdDBM;

            public bool bRFEGenHighPowerSwitch;
            public byte nRFEGenPowerLevel;
            public double fRFEGenCWFreqMHZ;
            public UInt16 nRFEGenSweepWaitMS;
            public bool bRFEGenPowerON;

            public bool bRFEGenStartHighPowerSwitch;
            public bool bRFEGenStopHighPowerSwitch;
            public byte nRFEGenStartPowerLevel;
            public byte nRFEGenStopPowerLevel;
            public UInt16 nRFGenSweepPowerSteps;

            public RFEConfiguration()
            {
                fStartMHZ = 0.0;
                fStepMHZ = 0.0;
                fAmplitudeTopDBM = 0.0;
                fAmplitudeBottomDBM = 0.0;
                nFreqSpectrumSteps = 0;
                bExpansionBoardActive = false;
                eMode = eMode.MODE_NONE;
                fMinFreqMHZ = 0.0;
                fMaxFreqMHZ = 0.0;
                fMaxSpanMHZ = 0.0;
                fRBWKHZ = 0.0;
                fOffset_dB = 0.0f;
                nBaudrate = 0;
                eModulation = eModulation.MODULATION_NONE;
                fThresholdDBM = 0.0f;

                nRFEGenSweepWaitMS = 0;
                bRFEGenHighPowerSwitch=false;
                nRFEGenPowerLevel = 0;
                fRFEGenCWFreqMHZ = 0.0;
                bRFEGenPowerON = false;

                bRFEGenStartHighPowerSwitch = false;
                bRFEGenStopHighPowerSwitch = false;
                nRFEGenStartPowerLevel = 0;
                nRFEGenStopPowerLevel = 1;
                nRFGenSweepPowerSteps = 0;

                eCalculator = eCalculator.UNKNOWN;
            }

            public RFEConfiguration(RFEConfiguration objSource)
            {
                fStartMHZ = objSource.fStartMHZ;
                fStepMHZ = objSource.fStepMHZ;
                fAmplitudeTopDBM = objSource.fAmplitudeTopDBM;
                fAmplitudeBottomDBM = objSource.fAmplitudeBottomDBM;
                nFreqSpectrumSteps = objSource.nFreqSpectrumSteps;
                bExpansionBoardActive = objSource.bExpansionBoardActive;
                eMode = objSource.eMode;
                fMinFreqMHZ = objSource.fMinFreqMHZ;
                fMaxFreqMHZ = objSource.fMaxFreqMHZ;
                fMaxSpanMHZ = objSource.fMaxSpanMHZ;
                fRBWKHZ = objSource.fRBWKHZ;
                fOffset_dB = objSource.fOffset_dB;
                eCalculator = objSource.eCalculator;
                nBaudrate = objSource.nBaudrate;
                eModulation = objSource.eModulation;
                fThresholdDBM = objSource.fThresholdDBM;

                bRFEGenHighPowerSwitch = objSource.bRFEGenHighPowerSwitch;
                nRFEGenPowerLevel = objSource.nRFEGenPowerLevel;
                fRFEGenCWFreqMHZ = objSource.fRFEGenCWFreqMHZ;
                nRFEGenSweepWaitMS = objSource.nRFEGenSweepWaitMS;
                bRFEGenPowerON = objSource.bRFEGenPowerON;

                bRFEGenStartHighPowerSwitch = objSource.bRFEGenStartHighPowerSwitch;
                bRFEGenStopHighPowerSwitch = objSource.bRFEGenStopHighPowerSwitch;
                nRFEGenStartPowerLevel = objSource.nRFEGenStartPowerLevel;
                nRFEGenStopPowerLevel = objSource.nRFEGenStopPowerLevel;
                nRFGenSweepPowerSteps = objSource.nRFGenSweepPowerSteps;
            }

            public bool ProcessReceivedString(string sLine)
            {
                bool bOk = true;

                try
                {
                    sLineString = sLine;

                    if ( (sLine.Length >= 60) && ((sLine.StartsWith("#C2-F:")) || (sLine.StartsWith("#C2-f:"))) )
                    {
                        //Spectrum Analyzer mode, can be C2-F for steps < 10000 or C2-f for steps >=10000
                        fStartMHZ = Convert.ToInt32(sLine.Substring(6, 7)) / 1000.0; //note it comes in KHZ
                        fStepMHZ = Convert.ToInt32(sLine.Substring(14, 7)) / 1000000.0;  //Note it comes in HZ
                        fAmplitudeTopDBM = Convert.ToInt32(sLine.Substring(22, 4));
                        fAmplitudeBottomDBM = Convert.ToInt32(sLine.Substring(27, 4));
                        int nPos = 32;
                        if (sLine.StartsWith("#C2-f:"))
                        {
                            nFreqSpectrumSteps = Convert.ToUInt16(sLine.Substring(nPos, 5));
                            nPos++;
                        }
                        else
                            nFreqSpectrumSteps = Convert.ToUInt16(sLine.Substring(nPos, 4));
                        nPos += 4; //we use this variable to keep state for long step number

                        bExpansionBoardActive = (sLine[nPos + 1] == '1');
                        eMode = (eMode)Convert.ToUInt16(sLine.Substring(nPos + 3, 3));
                        fMinFreqMHZ = Convert.ToInt32(sLine.Substring(nPos + 7, 7)) / 1000.0;
                        fMaxFreqMHZ = Convert.ToInt32(sLine.Substring(nPos + 15, 7)) / 1000.0;
                        fMaxSpanMHZ = Convert.ToInt32(sLine.Substring(nPos + 23, 7)) / 1000.0;

                        if (sLine.Length > nPos + 30)
                        {
                            fRBWKHZ = Convert.ToInt32(sLine.Substring(nPos + 31, 5));
                        }
                        if (sLine.Length > nPos + 36)
                        {
                            fOffset_dB = Convert.ToInt32(sLine.Substring(nPos + 37, 4));
                        }
                        if (sLine.Length > nPos + 41)
                        {
                            eCalculator = (eCalculator)Convert.ToUInt16(sLine.Substring(nPos + 42, 3));
                        }
                    }
                    else if ((sLine.Length >= 29) && (sLine.Substring(0, 4) == "#C3-"))
                    {
                        //Signal generator CW, SweepFreq and SweepAmp modes
                        switch (sLine[4])
                        {
                            case '*':
                                {
                                    fStartMHZ = Convert.ToInt32(sLine.Substring(6, 7)) / 1000.0; //note it comes in KHZ
                                    fRFEGenCWFreqMHZ = Convert.ToInt32(sLine.Substring(14, 7)) / 1000.0;  //Note it comes in KHZ
                                    nFreqSpectrumSteps = Convert.ToUInt16(sLine.Substring(22, 4));
                                    fStepMHZ = Convert.ToInt32(sLine.Substring(27, 7)) / 1000.0;  //Note it comes in KHZ
                                    bRFEGenHighPowerSwitch = (sLine[35] == '1');
                                    nRFEGenPowerLevel = Convert.ToByte(sLine[37] - 0x30);
                                    nRFGenSweepPowerSteps = Convert.ToUInt16(sLine.Substring(39, 4));
                                    bRFEGenStartHighPowerSwitch = (sLine[44] == '1');
                                    nRFEGenStartPowerLevel = Convert.ToByte(sLine[46] - 0x30);
                                    bRFEGenStopHighPowerSwitch = (sLine[48] == '1');
                                    nRFEGenStopPowerLevel = Convert.ToByte(sLine[50] - 0x30);
                                    bRFEGenPowerON = (sLine[52] == '1');
                                    nRFEGenSweepWaitMS = Convert.ToUInt16(sLine.Substring(54, 5));
                                    eMode = eMode.MODE_NONE;
                                    break;
                                }
                            case 'A':
                                {
                                    //Sweep Amplitude mode
                                    fRFEGenCWFreqMHZ = Convert.ToInt32(sLine.Substring(6, 7)) / 1000.0; //note it comes in KHZ
                                    nRFGenSweepPowerSteps = Convert.ToUInt16(sLine.Substring(14, 4));
                                    bRFEGenStartHighPowerSwitch = (sLine[19] == '1');
                                    nRFEGenStartPowerLevel = Convert.ToByte(sLine[21] - 0x30);
                                    bRFEGenStopHighPowerSwitch = (sLine[23] == '1');
                                    nRFEGenStopPowerLevel = Convert.ToByte(sLine[25] - 0x30);
                                    bRFEGenPowerON = (sLine[27] == '1');
                                    nRFEGenSweepWaitMS = Convert.ToUInt16(sLine.Substring(29, 5));
                                    eMode = RFECommunicator.eMode.MODE_GEN_SWEEP_AMP;
                                    break;
                                }
                            case 'F':
                                {
                                    //Sweep Frequency mode
                                    fStartMHZ = Convert.ToInt32(sLine.Substring(6, 7)) / 1000.0; //note it comes in KHZ
                                    nFreqSpectrumSteps = Convert.ToUInt16(sLine.Substring(14, 4));
                                    fStepMHZ = Convert.ToInt32(sLine.Substring(19, 7)) / 1000.0;  //Note it comes in KHZ
                                    bRFEGenHighPowerSwitch = (sLine[27] == '1');
                                    nRFEGenPowerLevel = Convert.ToByte(sLine[29] - 0x30);
                                    bRFEGenPowerON = (sLine[31] == '1');
                                    nRFEGenSweepWaitMS = Convert.ToUInt16(sLine.Substring(33, 5));
                                    eMode = RFECommunicator.eMode.MODE_GEN_SWEEP_FREQ;
                                    break;
                                }
                            case 'G':
                                {
                                    //Normal CW mode
                                    fRFEGenCWFreqMHZ = Convert.ToInt32(sLine.Substring(14, 7)) / 1000.0;  //Note it comes in KHZ
                                    nFreqSpectrumSteps = Convert.ToUInt16(sLine.Substring(22, 4));
                                    fStepMHZ = Convert.ToInt32(sLine.Substring(27, 7)) / 1000.0;  //Note it comes in KHZ
                                    bRFEGenHighPowerSwitch = (sLine[35] == '1');
                                    nRFEGenPowerLevel = Convert.ToByte(sLine[37] - 0x30);
                                    bRFEGenPowerON = (sLine[39] == '1');
                                    eMode = RFECommunicator.eMode.MODE_GEN_CW;
                                    break;
                                }
                            default:
                                eMode = eMode.MODE_NONE;
                                bOk = false;
                                break;
                        }
                    }
                    else if ((sLine.Length >= 10) && (sLine.StartsWith("#C4-F:")))
                    {
                        //Sniffer mode
                        fStartMHZ = Convert.ToInt32(sLine.Substring(6, 7)) / 1000.0; //note it comes in KHZ
                        bExpansionBoardActive = (sLine[14] == '1');
                        eMode = (eMode)Convert.ToUInt16(sLine.Substring(16, 3));
                        int nDelay = Convert.ToInt16(sLine.Substring(20, 5));
                        nBaudrate = (UInt32)Math.Round(Convert.ToDouble(RFECommunicator.FCY_CLOCK) / nDelay);
                        eModulation = (eModulation)Convert.ToUInt16(sLine.Substring(26, 1));
                        fRBWKHZ = Convert.ToInt32(sLine.Substring(28, 5));
                        fThresholdDBM = (float)(-0.5 * Convert.ToDouble(sLine.Substring(34, 3)));
                    }
                    else
                        bOk = false;
                }
                catch (Exception)
                {
                    bOk = false;
                }

                return bOk;
            }
        }

    }
}
