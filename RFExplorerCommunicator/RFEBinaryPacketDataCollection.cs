//============================================================================
//RF Explorer for Windows - A Handheld Spectrum Analyzer for everyone!
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

//#define TEST_SNIFFER //used for T0028, this trace enables shows PT2264 decoded sequence 

using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Data;
using System.Linq;
using System.Xml;
using System.Globalization;

namespace RFExplorerCommunicator
{
    /// <summary>
    /// The main goal of this class is to provide support for modulated RAW packet data, regardless modulation type, which is used by Sniffer
    /// </summary>
    public class RFEBinaryPacketData
    {
        #region constant
        public const UInt16 MAX_RAW_SAMPLE = 4096 * 8;     //max acceptable size for RAW data sample
        private const UInt16 m_nPeriodMultiplier = 4;  //Used as hardcoded period, but can be changed in future version to accommodate user preferences
        public const string _SnifferAddin_RelativePath = "\\sniffer_addin\\";  //Relative path used to locate all sniffer addin files
        private const char cCSVDelimiter_t = '\t';    //Delimiter by default. Used to separate each value

        public enum eProtocol { PT2264, HT12, EXTERNAL = 250, NONE = 255 } //define a kind of protocol
        enum ePulseWidth { NARROW_1, NARROW_0, WIDE_1, WIDE_0 };  //width of zeros and ones which is used to decoded a bit
        public enum eDecodedBit { LOW = 0, HIGH = 1, FLOATING = 2, OK = 254, FAIL = 255 };  //Hardecoded data. bit0 = LOW, bit1 = HIGH, bitF = FLOATIN, bitSync = OK/FAIL

        private const UInt16 _SYNC_BYTE_STATUS = 150; //Length of sync polygon (contain words "OK" or "FAIL")
        private const UInt16 _FIRST_BIT_POS = 2;    //Position of array from there are real data
        private const UInt16 _NEXT_BIT_POS = 8;     //Length of positions that real bit takes up    
        #endregion
        eProtocol m_enumProtocol = eProtocol.NONE;
        internal byte[] m_arrSamples; //RAW samples received from RF device    
        internal byte[] m_arrDecodedBits; //Decoded bits stream   
        internal UInt32[] m_arrDecodedBitsPos; //Decoded bits stream
        internal string[] m_arrDecodedWordText; //Decoded word text stream
        internal UInt32[] m_arrDecodedWordTextPos; //Decoded word text stream
        List<VisualObject> m_listDefinedTextUser = new List<VisualObject>();
        internal VisualObject[] m_arrVisualObj = null;

        static RFEBinaryPacketDataCollection m_ParentCollection = null;
        public static RFEBinaryPacketDataCollection ParentCollection
        {
            set { m_ParentCollection = value; }
            get { return m_ParentCollection; }
        }

        internal DateTime m_Time;
        /// <summary>
        /// The time when this data capture was created, it should match as much as possible the real data capture
        /// </summary>
        public DateTime CaptureTime
        {
            get { return m_Time; }
        }

        double m_fFrequencyMHZ;
        /// <summary>
        /// Center capture frequency in MHZ
        /// </summary>
        public double FrequencyMHZ
        {
            get { return m_fFrequencyMHZ; }
        }

        UInt16 m_nTotalSamples;
        /// <summary>
        /// Total number of data samples received
        /// </summary>
        public UInt16 Count
        {
            get { return m_nTotalSamples; }
        }

        /// <summary>
        /// Total number of samples filtered and trimmed
        /// </summary>
        public UInt16 FilteredCount
        {
            get
            {
                if (m_arrFilteredPos != null)
                    return (UInt16)m_arrFilteredPos.Length;
                else
                    return 0;
            }
        }

        private double m_fRBWKhz;
        /// <summary>
        /// Resolution BandWidth for capture in KHz
        /// </summary>
        public double RBWKhz
        {
            get
            {
                return m_fRBWKhz;
            }
        }

        private float m_fThresholdDBM;
        /// <summary>
        /// Power level threshold to capture input signal
        /// </summary>
        public float ThresholdDBM
        {
            get
            {
                return m_fThresholdDBM;
            }
        }

        private UInt32 m_nSampleRate;
        /// <summary>
        /// Sample rate in samples per second
        /// </summary>
        public uint SampleRate
        {
            get
            {
                return m_nSampleRate;
            }
        }

        string m_sFilenameCSV = null;
        /// <summary>
        /// Name of CSV file
        /// </summary>
        public string FilenameCSV
        {
            get { return m_sFilenameCSV; }
        }

        string m_sChartTitle = null;
        /// <summary>
        /// User defined chart title
        /// </summary>
        public string ChartTitle
        {
            get
            {
                return m_sChartTitle;
            }

            set
            {
                m_sChartTitle = value;
            }
        }

        /// <summary>
        /// Get information about user defined text object
        /// </summary>
        public VisualObject GetVisualObject(int nPos)
        {
            if (m_arrVisualObj != null && m_arrVisualObj.Length > 0 && nPos < m_arrVisualObj.Length)
                return m_arrVisualObj[nPos];
            else
                return null;
        }

        /// <summary>
        /// Set information about user defined text object
        /// </summary>
        /// <param name="fX"></param>
        /// <param name="fY"></param>
        /// <param name="sText"></param>
        public void SetVisualObject(float fX, float fY, string sText)
        {
            VisualObject objVisual = new VisualObject(fX, fY, sText);
            m_listDefinedTextUser.Add(objVisual);

            m_arrVisualObj = m_listDefinedTextUser.ToArray();
        }

        /// <summary>
        /// Get the number user defined text object
        /// </summary>
        /// <returns>integer with count of visualobject</returns>
        public int VisualObjectCount()
        {
            if (m_arrVisualObj != null && m_arrVisualObj.Length > 0)
                return m_arrVisualObj.Length;
            else
                return 0;
        }

        /// <summary>
        /// Create a RAW binary packet data without arguments. It is used to create a static Packet data collection
        /// Initialize the parameters by default
        /// </summary>
        public RFEBinaryPacketData()
        {
            m_Time = DateTime.Now;
            m_nTotalSamples = 0;
            m_fFrequencyMHZ = 0;
            m_arrSamples = null;
            m_fRBWKhz = 0;
            m_nSampleRate = 0;
            m_fThresholdDBM = 0;
        }

        /// <summary>
        /// Create a RAW binary packet data with center frequency, RBW, initial sample number, sample rate and threshold
        /// </summary>
        /// <param name="fFrequencyMHZ">center frequency in MHZ</param>
        /// <param name="fRBW">RBW used for capture</param>
        /// <param name="nTotalSamples">Total samples captured</param>
        /// <param name="nSampleRate">Baud rate in samples per seconds</param>
        /// <param name="fThresholdDBM">Threshold of captured in dBm</param>
        public RFEBinaryPacketData(double fFrequencyMHZ, double fRBW, UInt16 nTotalSamples, UInt32 nSampleRate, float fThresholdDBM)
        {
            m_Time = DateTime.Now;
            m_nTotalSamples = nTotalSamples;
            m_fFrequencyMHZ = fFrequencyMHZ;
            m_arrSamples = new byte[m_nTotalSamples];
            m_fRBWKhz = fRBW;
            m_nSampleRate = nSampleRate;
            m_fThresholdDBM = fThresholdDBM;

            for (int nInd = 0; nInd < m_nTotalSamples; nInd++)
                m_arrSamples[nInd] = 0;

#if TEST_SNIFFER
                Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
#endif
        }

        /// <summary>
        /// Process received string from RFECommunicator object
        /// </summary>
        /// <param name="sLine"></param>
        public void LoadRAWSnifferString(string sLine)
        {
            int nSize = sLine.Length - 2;
            for (int nInd = 0; nInd < nSize; nInd++)
            {
                Byte nVal = Convert.ToByte(sLine[nInd]);
                for (int nBitInd = 7; nBitInd >= 0; nBitInd--)
                {
                    m_arrSamples[nInd * 8 + (7 - nBitInd)] = (Byte)((nVal >> nBitInd) & 0x01);
                }
            }
        }

        /// <summary>
        /// Get unfiltered sample value
        /// </summary>
        /// <param name="nSampleInd"></param>
        /// <returns></returns>
        public byte GetSampleValue(UInt16 nSampleInd)
        {
            return m_arrSamples[nSampleInd];
        }

        /// <summary>
        /// Get filtered sample value
        /// </summary>
        /// <param name="nInd">Filtered sample index</param>
        /// <param name="nX">Value in sample interval of the state change for this sample</param>
        /// <param name="nValue">Logic value of this sample</param>
        public void GetFilteredValue(UInt16 nInd, out UInt16 nX, out byte nValue)
        {
            if (nInd >= m_arrFilteredPos.Length)
            {
                nX = 0;
                nValue = 0;
            }
            else
            {
                nX = m_arrFilteredPos[nInd];
                nValue = m_arrFilteredSamples[nInd];
            }
        }

        /// <summary>
        /// Get Decoded bit value
        /// </summary>
        /// <param name="nBitInd"></param>
        /// <returns>bit0 = 0, bit1 = 1, bitF = 2, Ok=254(sync) and FAIL=255(sync)</returns>
        public byte GetDecodedBitValue(int nBitInd)
        {
            if (m_arrDecodedBits != null && nBitInd < m_arrDecodedBits.Length)
                return m_arrDecodedBits[nBitInd];
            else
                return (byte)eDecodedBit.FAIL;
        }

        /// <summary>
        /// Get Decoded word value
        /// </summary>
        /// <param name="nWordInd"></param>
        /// <returns>address:xxxxxxx, data:xxxx, "OK"/"FAIL" (sync).Although it will depend of protocol</returns>
        public string GetDecodedWordText(int nWordInd)
        {
            if (m_arrDecodedWordText != null && nWordInd < m_arrDecodedWordText.Length) 
                return m_arrDecodedWordText[nWordInd];
            else
                return eDecodedBit.FAIL.ToString();
        }

        /// <summary>
        /// Get Decoded bit position
        /// </summary>
        /// <param name="nBitInd"></param>
        /// <returns>position in samples or -1 if something was wrong</returns>
        public int GetDecodedBitsValuePos(int nPosInd)
        {
            if (m_arrDecodedBitsPos != null && nPosInd < m_arrDecodedBitsPos.Length)
                return (int)m_arrDecodedBitsPos[nPosInd];
            else
                return -1;
        }

        /// <summary>
        /// Get Decoded word text position
        /// </summary>
        /// <param name="nWordInd"></param>
        /// <returns>position in samples or -1 if something was wrong</returns>
        public int GetDecodedWordTextdPos(int nWordPosInd)
        {
            if (m_arrDecodedWordTextPos != null && nWordPosInd < m_arrDecodedWordTextPos.Length)
                return (int)m_arrDecodedWordTextPos[nWordPosInd];
            else
                return -1;
        }

        /// <summary>
        /// Get total number of bits 
        /// </summary>
        /// <returns></returns>
        public int GetDecodedBitCount()
        {
            return m_arrDecodedBits.Length;
        }

        /// <summary>
        /// Get total number of word texts 
        /// </summary>
        /// <returns></returns>
        public int GetDecodedWordTextCount()
        {
            return m_arrDecodedWordText.Length;
        }

        /// <summary>
        /// Returns the largest period of samples where the value is the same, useful to detect clean areas and fix the filtering based on that
        /// This should be done in data already trimmed, otherwise start/stop areas may confuse the algorithm
        /// </summary>
        /// <param name="nValue">byte value, usually 0, also valid for 1</param>
        /// <returns></returns>
        public UInt16 GetLargestPeriod(byte nValue)
        {
            UInt16 nMaxPeriod = 0;
            UInt16 nCurrentPeriod = 0;
            for (int nInd = 0; nInd < m_arrSamples.Length; nInd++)
            {
                if (m_arrSamples[nInd] == nValue)
                    nCurrentPeriod++;
                else
                {
                    if (nCurrentPeriod > nMaxPeriod)
                        nMaxPeriod = nCurrentPeriod;
                    nCurrentPeriod = 0;
                }
            }

            return nMaxPeriod;
        }

        /// <summary>
        /// Get shortest time period, may not be the same as carrier due to noise. 
        /// </summary>
        /// <param name="nValue">byte value, usually 0, also valid for 1</param>
        /// <returns></returns>
        public UInt16 GetShortestPeriod(byte nValue)
        {
            UInt16 nMinPeriod = (UInt16)(m_arrSamples.Length - 1);
            UInt16 nCurrentPeriod = 0;
            for (int nInd = 0; nInd < m_arrSamples.Length; nInd++)
            {
                if (m_arrSamples[nInd] == nValue)
                    nCurrentPeriod++;
                else
                {
                    if (nCurrentPeriod > 0)
                    {
                        if (nCurrentPeriod < nMinPeriod)
                            nMinPeriod = nCurrentPeriod;
                        nCurrentPeriod = 0;
                    }
                }
            }

            return nMinPeriod;
        }

        /// <summary>
        /// Get carrier period in samples. This is calculated  using average over the available samples, counting high level signal periods.
        /// </summary>
        /// <returns>carrier period in samples</returns>
        private float GetCarrierPeriodSample()
        {
            float fCarrierPeriod = 0;
            UInt16 nCurrentPeriod = 0;
            UInt16 nTotalValuePeriod = 0;
            float fNumPeriod = 0;
            int nPreviousSample = 0;
            int nZeroValueSample = 0;

            for (int nInd = 0; nInd < m_arrSamples.Length; nInd++)
            {
                int nCurrentSample = m_arrSamples[nInd];

                if (nCurrentSample < nPreviousSample)
                    fNumPeriod++;

                if (nCurrentSample == 0)
                    nZeroValueSample++;
                else
                {
                    nCurrentPeriod++;
                    nZeroValueSample = 0;
                }
                if (nZeroValueSample != 0)
                {
                    nTotalValuePeriod += nCurrentPeriod;
                    nCurrentPeriod = 0;
                }
                if (nZeroValueSample > nCurrentPeriod * 4)
                {
                    nCurrentPeriod = 0;
                }
                nPreviousSample = nCurrentSample;

            }
            fCarrierPeriod = ((nTotalValuePeriod) / fNumPeriod) * 2;

            return fCarrierPeriod;
        }

        /// <summary>
        /// Get carrier period value in seconds
        /// </summary>
        /// <returns>carrier period in seconds</returns>
        private float GetCarrierPeriodSeconds()
        {
            float fSamplePeriod = (float)Math.Pow(m_nSampleRate, -1);
            float fCarrierPeriodSeconds = (GetCarrierPeriodSample() * fSamplePeriod);

            return fCarrierPeriodSeconds;
        }

        /// <summary>
        /// Get Carrier Frequency in Hz
        /// </summary>
        /// <returns>carrier frequency in Hertz</returns>
        public float GetCarrierFrequency()
        {
            return (float)Math.Pow(GetCarrierPeriodSeconds(), -1);
        }

        /// <summary>
        /// Get decoder clock value in samples. In a future, it is possible a varible hardcoded periodo (now is  4)
        /// </summary>
        /// <returns>decoder clock in samples</returns>
        private float GetDecoderClock()
        {
            return (GetCarrierPeriodSample() * m_nPeriodMultiplier);
        }

        /// <summary>
        /// Returns true if the sample data was already filtered and trimmed
        /// </summary>
        public bool Filtered
        {
            get { return m_arrFilteredSamples != null; }
        }

        /// <summary>
        /// Set protocol to use
        /// </summary>
        /// <param name="protocol">enum with especific protocol</param>
        public void SetDecodedProtocol(eProtocol protocol)
        {
            m_enumProtocol = protocol;
        }

        /// <summary>
        /// Get protocol to use
        /// </summary>
        /// <returns>enum with especific protocol</returns>
        public eProtocol GetDecodedProtocol()
        {
            return m_enumProtocol;
        }

        internal byte[] m_arrFilteredSamples = null; //this is Y axis filtered values, paired with m_arrFilteredPos
        internal UInt16[] m_arrFilteredPos = null; //this is X axis filtered values for every signal change

        /// <summary>
        /// Filter input sample data so carrier envelope is found and filled in internal containers
        /// </summary>
        /// <param name="nPeriodMultiplier">Value used to discriminate and low pass filter the carrier. Ideally should be >=2*CarrierPeriod in samples</param>
        public void FilterValues()
        {
            UInt16 nFilterPeriod = (UInt16)Math.Round(GetDecoderClock());
            byte[] arrFilteredSamples = new byte[m_arrSamples.Length];

            //First filter by value
            byte nPrevValue = 0;
            for (int nInd = 0; nInd < m_arrSamples.Length; nInd++)
            {
                byte nValue = m_arrSamples[nInd];
                int nInd2 = nInd;
                if ((nValue == 0) && (nPrevValue == 1))
                {
                    //check if we should filter this as 1 or actually 0
                    for (; ((nInd2 <= (nInd + nFilterPeriod)) && (nInd2 < m_arrSamples.Length)); nInd2++)
                    {
                        if (m_arrSamples[nInd2] == 1)
                        {
                            //found a 1 again before reaching nFilterPeriod limit, so fill it with 1
                            for (int nInd3 = nInd; nInd3 <= nInd2; nInd3++)
                                arrFilteredSamples[nInd3] = 1;
                            break;
                        }
                    }
                }
                else
                {
                    //nothing to do, we just reuse the received value
                    arrFilteredSamples[nInd] = m_arrSamples[nInd];
                }
                if (nInd2 < m_arrSamples.Length)
                {
                    nInd = nInd2;
                    nPrevValue = m_arrSamples[nInd];
                }
            }

            //create data by timing position
            List<byte> listY = new List<byte>();
            List<UInt16> listX = new List<ushort>();

            byte nLastByteVal = 255;
            for (UInt16 nInd = 0; nInd < arrFilteredSamples.Length; nInd++)
            {
                if (nInd == 0)
                {
                    nLastByteVal = arrFilteredSamples[0];
                    listY.Add(nLastByteVal);
                    listX.Add(0);
                }
                else
                {
                    byte nVal = arrFilteredSamples[nInd];
                    if ((nVal != nLastByteVal) || (nInd == arrFilteredSamples.Length - 1))
                    {
                        listY.Add(nLastByteVal);
                        listX.Add((UInt16)(nInd - 1));
                        listY.Add(nVal);
                        listX.Add((UInt16)(nInd - 1));
                        nLastByteVal = nVal;
                    }
                }
            }

            m_arrFilteredSamples = listY.ToArray();
            m_arrFilteredPos = listX.ToArray();
        }

        /// <summary>
        /// Decode sequence of pulse width of zeros and ones
        /// </summary>
        /// <returns>Array of enum with all data about narrow and wide pulse width of zeros and ones</returns>
        private ePulseWidth[] DecodePulseWidth()
        {
            int nNumState_Narrow0 = 0, nTotal_Narrow0 = 0, nAverage_Narrow0 = 0;
            int nNumState_Narrow1 = 0, nTotal_Narrow1 = 0, nAverage_Narrow1 = 0;
            int nNumState_Wide0 = 0, nTotalWidth_Wide0 = 0, nAverage_Wide0 = 0;
            int nNumState_Wide1 = 0, nTotalWidth_Wide1 = 0, nAverage_Wide1 = 0;
            ePulseWidth[] arrTempDecoderBit = null;
            List<int> listWidth0 = new List<int>();
            List<int> listWidth1 = new List<int>();
            List<ePulseWidth> listDecodedState = new List<ePulseWidth>();

            for (int nPos = 0; nPos < m_arrFilteredPos.Length; nPos++)
            {
                if ((nPos + 1) < m_arrFilteredPos.Length)
                {
                    if (m_arrFilteredPos[nPos + 1] != m_arrFilteredPos[nPos])
                    {
                        if (m_arrFilteredSamples[nPos] == 0)
                            listWidth0.Add(m_arrFilteredPos[nPos + 1] - m_arrFilteredPos[nPos]);
                        else
                            listWidth1.Add(m_arrFilteredPos[nPos + 1] - m_arrFilteredPos[nPos]);
                    }
                }
            }
            listWidth0.Sort();
            listWidth1.Sort();

            //Find average to 0 pulse width 
            int nMinWidth0 = 0;
            if (listWidth0.Count > 0)
                nMinWidth0 = listWidth0[0];
            foreach (int nCurrentWidth in listWidth0)
            {
                if (nCurrentWidth <= nMinWidth0 * 2)
                {
                    nTotal_Narrow0 += nCurrentWidth;
                    nNumState_Narrow0++;
                }
                else
                {
                    nTotalWidth_Wide0 += nCurrentWidth;
                    nNumState_Wide0++;
                }
            }
            if (nNumState_Narrow0 != 0 && nNumState_Wide0 != 0)
            {
                nAverage_Narrow0 = nTotal_Narrow0 / nNumState_Narrow0;
                nAverage_Wide0 = nTotalWidth_Wide0 / nNumState_Wide0;
            }

            //Find average to 1 pulse width 
            int nMinWidth1 = 0;
            if (listWidth1.Count - 1 >= 3)
            {
                if (listWidth1[0] * 2 >= listWidth1[3])
                    nMinWidth1 = listWidth1[0];
                else
                    nMinWidth1 = listWidth1[3];
            }
            foreach (int nCurrentWidth in listWidth1)
            {
                if (nCurrentWidth <= nMinWidth1 * 2)
                {
                    nTotal_Narrow1 += nCurrentWidth;
                    nNumState_Narrow1++;
                }
                else
                {
                    nTotalWidth_Wide1 += nCurrentWidth;
                    nNumState_Wide1++;
                }
            }
            if (nNumState_Narrow1 != 0 && nNumState_Wide1 != 0)
            {
                nAverage_Narrow1 = nTotal_Narrow1 / nNumState_Narrow1;
                nAverage_Wide1 = nTotalWidth_Wide1 / nNumState_Wide1;
            }

            //Protocol is decoded looking for the pulse width
            for (int nInd = 0; nInd < m_arrFilteredPos.Length; nInd++)
            {
                if ((nInd + 1) < m_arrFilteredPos.Length)
                {
                    if (m_arrFilteredPos[nInd + 1] != m_arrFilteredPos[nInd])
                    {
                        int nPulseWidth = m_arrFilteredPos[nInd + 1] - m_arrFilteredPos[nInd];

                        if (m_arrFilteredSamples[nInd] == 0)
                        {
                            if (Math.Abs(nAverage_Narrow0 - nPulseWidth) <= Math.Abs(nAverage_Wide0 - nPulseWidth))
                                listDecodedState.Add(ePulseWidth.NARROW_0);
                            else
                                listDecodedState.Add(ePulseWidth.WIDE_0);
                        }
                        else
                        {
                            if (Math.Abs(nAverage_Narrow1 - nPulseWidth) <= Math.Abs(nAverage_Wide1 - nPulseWidth))
                                listDecodedState.Add(ePulseWidth.NARROW_1);
                            else
                                listDecodedState.Add(ePulseWidth.WIDE_1);
                        }
                    }
                }
            }

            arrTempDecoderBit = listDecodedState.ToArray();   //store narrow or/and wide two pulse

            return arrTempDecoderBit;
        }

        /// <summary>
        /// Decode PT2264 protocol. Data bit: 0 = bit0, 1 = bit1, 2 = bitF, Sync: 3 = OK and 4 = FAIL 
        /// </summary>
        /// <returns>true if is decoded ok, otherwise false</returns>
        public bool DecodePT2264()
        {
            bool bOk = true;
            List<byte> listDecodedBits = new List<byte>();
            List<UInt32> listDecodedBitsPos = new List<UInt32>();
            List<string> listDecodedWords = new List<string>();
            List<UInt32> listDecodedWordsPos = new List<UInt32>();
            ePulseWidth[] arrTempDecoderBit = null;
            int nPos = 0;

            ushort nBitIndexPos = _FIRST_BIT_POS;
            ushort nX = 0;

            byte nFilteredValue = 0;

            arrTempDecoderBit = DecodePulseWidth();

            int nArrayLength = arrTempDecoderBit.Length;
            while ((nPos < nArrayLength) && bOk)
            {
                if ((nPos + 3) < nArrayLength)
                {
                    if (listDecodedBits.Count <= 11)
                    {
                        if (arrTempDecoderBit[nPos].Equals(ePulseWidth.NARROW_1) && arrTempDecoderBit[nPos + 1].Equals(ePulseWidth.WIDE_0) && arrTempDecoderBit[nPos + 2].Equals(ePulseWidth.NARROW_1) && arrTempDecoderBit[nPos + 3].Equals(ePulseWidth.WIDE_0))
                            listDecodedBits.Add((byte)eDecodedBit.LOW);
                        else if (arrTempDecoderBit[nPos].Equals(ePulseWidth.WIDE_1) && arrTempDecoderBit[nPos + 1].Equals(ePulseWidth.NARROW_0) && arrTempDecoderBit[nPos + 2].Equals(ePulseWidth.WIDE_1) && arrTempDecoderBit[nPos + 3].Equals(ePulseWidth.NARROW_0))
                            listDecodedBits.Add((byte)eDecodedBit.HIGH);
                        else if (arrTempDecoderBit[nPos].Equals(ePulseWidth.NARROW_1) && arrTempDecoderBit[nPos + 1].Equals(ePulseWidth.WIDE_0) && arrTempDecoderBit[nPos + 2].Equals(ePulseWidth.WIDE_1) && arrTempDecoderBit[nPos + 3].Equals(ePulseWidth.NARROW_0))
                            listDecodedBits.Add((byte)eDecodedBit.FLOATING);
                        else
                        {
                            bOk = false;
                        }
                    }
                    else
                        bOk = false;
                }
                nPos += 4;
            }

            if (!bOk)
                listDecodedBits.Add((byte)eDecodedBit.FAIL);
            else if (arrTempDecoderBit[nArrayLength - 1].Equals(ePulseWidth.NARROW_1) && nArrayLength == 49)
                listDecodedBits.Add((byte)eDecodedBit.OK);
            else
                listDecodedBits.Add((byte)eDecodedBit.FAIL);

            m_arrDecodedBits = listDecodedBits.ToArray();

            // Decoded bit position
            for (int nInd = 0; nInd < GetDecodedBitCount(); nInd++)
            {
                if (nBitIndexPos <= FilteredCount)
                {
                    GetFilteredValue(nBitIndexPos, out nX, out nFilteredValue);
                    nBitIndexPos += _NEXT_BIT_POS;
                }
                else
                {
                    GetFilteredValue((UInt16)(FilteredCount - 1), out nX, out nFilteredValue);
                }
                listDecodedBitsPos.Add(nX);
            }
            m_arrDecodedBitsPos = listDecodedBitsPos.ToArray();
            //


            //Information of hardcoded signal. Address = XXXX XXXX bits (position[0] to[7]), 
            //Data = XXXX(position[8] to[11]) bits and Sync = OK / FAIL bit(positon[12]).
            string sTextBit = "";
            int nNextX = 0, nXData = 0; 
            UInt16 nLength = 0, nLengthAddress = 0, nLengthData = 0; 
            string sTextAddressBit = "Address: ", sTextDataBit = "Data: ";

            for (int nInd = 0; nInd < GetDecodedBitCount(); nInd++)
            {
                byte nBitValue = GetDecodedBitValue(nInd);
                nX = (UInt16)GetDecodedBitsValuePos(nInd);
                nNextX = GetDecodedBitsValuePos(nInd + 1);
                if (nNextX != -1)
                    nLength = (UInt16)(nNextX - nX);

                if (nBitValue == (byte)eDecodedBit.FLOATING)
                    sTextBit = "F";
                else
                    sTextBit = nBitValue.ToString();
                if (nInd < 8)
                {
                    nLengthAddress +=  nLength;
                    if (nBitValue == (byte)eDecodedBit.FAIL)
                    {
                        sTextAddressBit = eDecodedBit.FAIL.ToString();
                        listDecodedWords.Add(sTextAddressBit);
                        listDecodedWordsPos.Add(nX);
                    }
                    else
                        sTextAddressBit += sTextBit;
                }
                else if (nInd >= 8 && nInd < 12)
                {
                    if (nInd == 8)
                        nXData = nX;
                    nLengthData += nLength;
                    if (nBitValue == (byte)eDecodedBit.FAIL)
                    {
                        sTextDataBit = eDecodedBit.FAIL.ToString();
                        listDecodedWords.Add(sTextDataBit);
                        listDecodedWordsPos.Add(nX);
                    }
                    else
                        sTextDataBit += sTextBit;
                }
                if (nInd == 7)   //store address
                {
                    listDecodedWords.Add(sTextAddressBit);
                    listDecodedWordsPos.Add(0);
                }
                if (nInd == 11)  //store data
                {
                    listDecodedWords.Add(sTextDataBit);
                    listDecodedWordsPos.Add((UInt32)nXData);
                }
                if (nInd == 12)  //store sync
                {
                    if (nBitValue == (byte)RFEBinaryPacketData.eDecodedBit.OK)
                        sTextBit = RFEBinaryPacketData.eDecodedBit.OK.ToString();
                    else if (nBitValue == (byte)RFEBinaryPacketData.eDecodedBit.FAIL)
                        sTextBit = RFEBinaryPacketData.eDecodedBit.FAIL.ToString();
                    else
                        sTextBit = nBitValue.ToString();
                    listDecodedWords.Add(sTextBit);
                    listDecodedWordsPos.Add(nX);
                }
            }
            m_arrDecodedWordText = listDecodedWords.ToArray();
            m_arrDecodedWordTextPos = listDecodedWordsPos.ToArray();

            return bOk;

#if TRACE && TEST_SNIFFER
                //This is for debug only -> List all decoded bit in DecodePT2264 function
                Trace.WriteLine("\n--------------------------" + "\nDecodePT2264\n" + "--------------------------");
                foreach (byte bit in m_arrDecoderBits)
                    //Trace.WriteLine("bit " + listDecodedBits.IndexOf(bit) + "-> " + bit.ToString());
                    Trace.Write(bit.ToString()+ ",");
                Trace.WriteLine("\n--------------------------\n");
#endif
        }

        /// <summary>
        /// Decode HT12 protocol. Data bit: 0 = bit0, 1 = bit1, Sync: 2 = OK and 3 = FAIL 
        /// </summary>
        /// <returns>Array with decoded sequence. 0 = bit 0, 1 = bit 1, 2 = OK and 3 = FAIL</returns>
        public byte[] DecodeHT12() //TODO T0028: new ht12 function
        {
            List<byte> listDecodedBits = new List<byte>();
            ePulseWidth[] arrTempDecoderBit = null;
            int nPos = 0;
            bool bFailDedecoded = false;

            arrTempDecoderBit = DecodePulseWidth();

            int nArrayLength = arrTempDecoderBit.Length;

            if (nArrayLength > 0)
            {
                if (arrTempDecoderBit[0].Equals(ePulseWidth.NARROW_1) && nArrayLength == 25) //TODO T0028: check this number 25
                    listDecodedBits.Add((byte)eDecodedBit.OK);
                else
                    listDecodedBits.Add((byte)eDecodedBit.FAIL);
            }

            while ((nPos < nArrayLength) && !bFailDedecoded)
            {
                if ((nPos + 1) < nArrayLength)
                {
                    if (listDecodedBits.Count <= 11)
                    {
                        if (arrTempDecoderBit[nPos].Equals(ePulseWidth.WIDE_0) && arrTempDecoderBit[nPos + 1].Equals(ePulseWidth.NARROW_1))
                            listDecodedBits.Add((byte)eDecodedBit.LOW);
                        else if (arrTempDecoderBit[nPos].Equals(ePulseWidth.NARROW_0) && arrTempDecoderBit[nPos + 1].Equals(ePulseWidth.WIDE_1))
                            listDecodedBits.Add((byte)eDecodedBit.HIGH);
                        else
                        {
                            bFailDedecoded = true;
                            listDecodedBits[0] = (byte)eDecodedBit.FAIL;
                        }
                    }
                    else
                    {
                        bFailDedecoded = true;
                        listDecodedBits[0] = (byte)eDecodedBit.FAIL;
                    }

                }
                nPos += 2;
            }

            m_arrDecodedBits = listDecodedBits.ToArray();


            return m_arrDecodedBits;

#if TRACE && TEST_SNIFFER
                //This is for debug only -> List all decoded bit in DecodePT2264 function
                Trace.WriteLine("\n--------------------------" + "\nDecodePT2264\n" + "--------------------------");
                foreach (byte bit in m_arrDecoderBits)
                    //Trace.WriteLine("bit " + listDecodedBits.IndexOf(bit) + "-> " + bit.ToString());
                    Trace.Write(bit.ToString()+ ",");
                Trace.WriteLine("\n--------------------------\n");
#endif
        }

        /// <summary>
        /// Trim input samples between two index values. This does not trim filter data, but cleans it if was previously set.
        /// Note: after this is completed, trimed out data is lost for good.
        /// </summary>
        /// <param name="nFirstValidSample"></param>
        /// <param name="nLastValidSample"></param>
        public void TrimValues(UInt16 nFirstValidSample, UInt16 nLastValidSample)
        {
            m_arrFilteredPos = null;
            m_arrFilteredSamples = null;

            byte[] arrOldSamples = m_arrSamples;

            if (nLastValidSample > m_arrSamples.Length - 1)
                nLastValidSample = (UInt16)(m_arrSamples.Length - 1);

            while (m_arrSamples[nFirstValidSample] == 0)
                nFirstValidSample++;
            while (m_arrSamples[nLastValidSample] == 0)
                nLastValidSample--;

            if (nFirstValidSample > 0)
                nFirstValidSample--;
            if (nLastValidSample < (UInt16)(m_arrSamples.Length - 1))
                nLastValidSample++;

            m_nTotalSamples = (UInt16)(nLastValidSample - nFirstValidSample + 1);
            m_arrSamples = new byte[m_nTotalSamples];
            Array.Copy(arrOldSamples, nFirstValidSample, m_arrSamples, 0, m_nTotalSamples);

            FilterValues();
        }

        /// <summary>
        /// CSV filtered simple file version
        /// </summary>
        /// <returns></returns>
        internal static string CSVFiltered_FileHeaderVersioned_001()
        {
            return "RF Explorer RAW sniffer file: RFExplorer PC Client - Format v001";
        }

        /// <summary>
        /// Save a simple CSV file with coordinates of filtered samples
        /// </summary>
        /// <param name="sFilename">filename and path to use</param>
        /// <param name="cCSVDelimiter">delimiter to separate data</param>
        /// <returns>true if it was possible save file or false otherwise</returns>
        public bool SaveFilteredCSV(string sFilename, char cCSVDelimiter)
        {
            bool bOKSaveCSV = true;

            if (m_nTotalSamples <= 0 || m_arrFilteredSamples == null || m_arrFilteredPos == null)
            {
                bOKSaveCSV = false;
                return bOKSaveCSV;
            }
            try
            {
                if (GetDecodedProtocol() == eProtocol.EXTERNAL)
                {
                    sFilename = Environment.GetEnvironmentVariable("TEMP") + "\\" + sFilename;
                }
                m_sFilenameCSV = sFilename;

                using (StreamWriter myFile = new StreamWriter(sFilename, false))
                {
                    myFile.WriteLine(RFEBinaryPacketData.CSVFiltered_FileHeaderVersioned_001());
                    if (GetDecodedProtocol() != eProtocol.EXTERNAL)
                    {
                        myFile.WriteLine("Date: " + CaptureTime.ToShortDateString());
                        myFile.WriteLine("Time: " + CaptureTime.ToString("HH:mm:ss"));
                        //From october 2017 we always save data with "en-US" settings
                        myFile.WriteLine("Freq(MHZ):" + FrequencyMHZ.ToString("f3", CultureInfo.InvariantCulture));
                        myFile.WriteLine("Sample rate: " + m_nSampleRate.ToString());
                        myFile.WriteLine("RBW(KHZ): " + m_fRBWKhz.ToString("f3", CultureInfo.InvariantCulture));
                        myFile.WriteLine("Threshold(dBm): " + m_fThresholdDBM.ToString("f1", CultureInfo.InvariantCulture));
                        myFile.WriteLine("Data values: Sample_Index, Sample_Value");
                    }

                    //we want to save value for 0, and then only odd numbers as otherwise we are repeating positions used for display but bad for data
                    for (UInt16 nInd = 0; nInd < m_arrFilteredSamples.Length; nInd += 2)
                    {
                        byte nVal = m_arrFilteredSamples[nInd];
                        UInt16 nPos = m_arrFilteredPos[nInd];
                        myFile.WriteLine(nPos.ToString() + cCSVDelimiter + nVal.ToString());
                    }
                }
            }
            catch (Exception obEx)
            {
                m_ParentCollection.ReportLog(obEx.Message.ToString());
                bOKSaveCSV = false;
            }
            return bOKSaveCSV;
        }

        /// <summary>
        /// Load a simple file with decoded samples
        /// </summary>
        /// <param name="sFilename">Decoded csv file from Addin</param>
        /// <returns>true if it was possible read file or false otherwise</returns>
        public bool ReadFileCSV(string sFilename)
        {
            bool bOk = true;
            List<UInt32> listX = new List<UInt32>();
            List<byte> listY = new List<byte>();
            List<UInt32> listXToDraw = new List<UInt32>();
            List<byte> listYToDraw = new List<byte>();
            List<UInt32> listDecodeWordsTextPos = new List<UInt32>();
            List<string> listDecodeWordsText = new List<string>();
            try
            {
                using (var objReader = new StreamReader(sFilename))
                {
                    if (objReader != null)
                    {
                        //Read 1st line: header and version
                        string sLine = objReader.ReadLine();
                        bOk = !string.IsNullOrEmpty(sLine);
                        if (bOk)
                        {
                            bOk = (sLine.Contains(RFEBinaryPacketData.CSVFiltered_FileHeaderVersioned_001()));
                            if (!bOk)
                                m_ParentCollection.ReportLog("Error: in ReadFileCSV(). Header format is wrong" + sLine);
                        }
                        else
                        {
                            m_ParentCollection.ReportLog("Error: in ReadFileCSV(). First line with header line is missing");
                        }
                        if (bOk)
                        {
                            //Read 2nd line: data decode ok is 0, if some error then !=0
                            sLine = objReader.ReadLine();
                            bOk = !string.IsNullOrEmpty(sLine);
                        }
                        if (bOk)
                        {
                            bool bDecodedOk = (sLine == "0"); //use this to display a message
                            if (!bDecodedOk)
                                m_ParentCollection.ReportLog("Error: in ReadFileCSV().Add-in returns error code");
                        }
                        else
                        {
                            m_ParentCollection.ReportLog("Error: in ReadFileCSV().Second line with confirmation about status of decoding is missing");
                        }
                        if (bOk)
                        {
                            //3rd line: position and value of decoded signal
                            sLine = objReader.ReadLine();
                            bOk = !string.IsNullOrEmpty(sLine);
                        }
                        if (bOk)
                        {
                            string[] arrLine = sLine.Split(cCSVDelimiter_t);
                            for (int nInd = 0; nInd < arrLine.Length; nInd++)
                            {
                                if (nInd % 2 == 0)
                                    listX.Add(Convert.ToUInt32(arrLine[nInd]));
                                else
                                    listY.Add(Convert.ToByte(arrLine[nInd]));
                            }
                            m_arrDecodedBits = listY.ToArray();
                            m_arrDecodedBitsPos = listX.ToArray();

                        }
                        else
                        {
                            m_ParentCollection.ReportLog("Error: in ReadFileCSV().Third line with position and value of the decoded signal is missing");
                        }
                        if (bOk)
                        {
                            //4th line: position and word texts  decoded signal
                            sLine = objReader.ReadLine();
                            bOk = !string.IsNullOrEmpty(sLine);
                        }
                        if (bOk)
                        {
                            string[] arrLine = sLine.Split(cCSVDelimiter_t);
                            for (int nInd3 = 0; nInd3 < arrLine.Length; nInd3++)
                            {
                                if (nInd3 % 2 == 0)
                                    listDecodeWordsTextPos.Add(Convert.ToUInt32(arrLine[nInd3]));
                                else
                                    listDecodeWordsText.Add(arrLine[nInd3]);
                            }
                            m_arrDecodedWordText = listDecodeWordsText.ToArray();
                            m_arrDecodedWordTextPos = listDecodeWordsTextPos.ToArray();
                        }
                        else
                        {
                            m_ParentCollection.ReportLog("Error: in ReadFileCSV().Fourth line with position and word text of the decoded signal is missing");
                        }
                    }
                }
            }
            catch (Exception obEx)
            {
                m_ParentCollection.ReportLog("Error: not found CSV output file"+obEx.Message.ToString());
                bOk = false;
            }
            return bOk;
        }
    }

    /// <summary>
    /// A collection of RFEBinaryPacketData objects
    /// </summary>
    public class RFEBinaryPacketDataCollection
    {
        #region Constants
        public const UInt16 MAX_PACKETS = 10 * 1024;

        private const string _PacketData = "PacketData";
        private const string _CaptureTime = "CaptureTime";
        private const string _TotalSamples = "TotalSamples";
        private const string _FrequencyMHZ = "FrequencyMHZ";
        private const string _Samples = "Sample";
        private const string _RBWKhz = "RBWKhz";
        private const string _SampleRate = "SampleRate";
        private const string _ThresholdDBM = "ThresholdDBM";
        private const string _VisualObjectRef = "VisualObjectRef";
        private const string _FilteredPos = "FilteredPos";
        private const string _RAWSamples = "RAWSamples";
        private const string _FilteredSamples = "FilteredSamples";
        private const string _DecodedBits = "DecodedBits";
        private const string _Protocol = "Protocol";

        private const string _VisualObject = "VisualObject";
        private const string _ObjectID = "ObjectID";
        private const string _ObjectType = "ObjectType";
        private const string _X = "X";
        private const string _Y = "Y";
        private const string _Text = "Text";
        private const string _Params = "Params";

        private const string _Version = "Version";  
        private const string _Title = "Title";
        private const string _DecodedBitsPos = "DecodedBitsPos";
        private const string _DecodedWords = "DecodedWords";
        private const string _DecodedWordsPos = "DecodedWordsPos";

        private const string _MODE_SNIFFER_NONE = "NONE";
        #endregion

        /// <summary>
        /// Construnctor used to clone this object and do so static
        /// </summary>
        public RFEBinaryPacketDataCollection()
        {
            RFEBinaryPacketData.ParentCollection = this;
        }

        public  event EventHandler ReportInfoEventPacketDataCollection;
        /// <summary>
        /// Use this event to receive error or info notifications
        /// </summary>
        private  void OnReportInfo(EventReportInfo eventArgs)
        {
            if (ReportInfoEventPacketDataCollection != null)
            {
                ReportInfoEventPacketDataCollection(this, eventArgs);
            }
        }

        RFEBinaryPacketData[] m_arrData = new RFEBinaryPacketData[MAX_PACKETS];
        DataSet m_XMLSnifferData = new DataSet("RF_Explorer_Sniffer_Data_Structure"); //Data collection

        int m_nUpperBound = -1;  //Max value for index with available data
        /// <summary>
        /// Returns the total of elements with actual data allocated.
        /// </summary>
        public UInt16 Count
        {
            get { return ((UInt16)(m_nUpperBound + 1)); }
            set { value = (UInt16)m_nUpperBound; }
        }

        /// <summary>
        /// CSV RAW complete file version
        /// </summary>
        /// <returns></returns>
        internal static string CSVComplete_FileHeaderVersioned_001()
        {
            return "RF Explorer RAW sniffer file: RFExplorer PC Client - Format v001";
        }
        /// <summary>
        /// XML file version 
        /// </summary>
        /// <returns></returns>
        internal static string XML_FileHeaderVersioned_002()
        {
            return "RF Explorer XML Sniffer v002";
        }
   
        /// <summary>
        /// Create XML Schema used by rfsniffer file
        /// </summary>
        private void CreateXMLSchema()
        {
            DataTable objTablePacketData = null;
            DataTable objTableVisualObject = null;

            objTablePacketData = m_XMLSnifferData.Tables.Add(_PacketData);

            objTablePacketData.Columns.Add(new DataColumn(_Version, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_Title, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_CaptureTime, System.Type.GetType("System.DateTime")));
            objTablePacketData.Columns.Add(new DataColumn(_TotalSamples, System.Type.GetType("System.UInt16")));
            objTablePacketData.Columns.Add(new DataColumn(_FrequencyMHZ, System.Type.GetType("System.Double")));
            objTablePacketData.Columns.Add(new DataColumn(_RBWKhz, System.Type.GetType("System.Double")));
            objTablePacketData.Columns.Add(new DataColumn(_SampleRate, System.Type.GetType("System.UInt32")));
            objTablePacketData.Columns.Add(new DataColumn(_ThresholdDBM, System.Type.GetType("System.Single")));
            objTablePacketData.Columns.Add(new DataColumn(_VisualObjectRef, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_RAWSamples, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_FilteredPos, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_FilteredSamples, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_DecodedBits, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_DecodedBitsPos, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_DecodedWords, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_DecodedWordsPos, System.Type.GetType("System.String")));
            objTablePacketData.Columns.Add(new DataColumn(_Protocol, System.Type.GetType("System.String")));

            if (!m_XMLSnifferData.Tables.Contains(_VisualObject))
            {
                objTableVisualObject = m_XMLSnifferData.Tables.Add(_VisualObject);

                objTableVisualObject.Columns.Add(new DataColumn(_ObjectID, System.Type.GetType("System.UInt16")));
                objTableVisualObject.Columns.Add(new DataColumn(_ObjectType, System.Type.GetType("System.String")));
                objTableVisualObject.Columns.Add(new DataColumn(_X, System.Type.GetType("System.Single")));
                objTableVisualObject.Columns.Add(new DataColumn(_Y, System.Type.GetType("System.Single")));
                objTableVisualObject.Columns.Add(new DataColumn(_Text, System.Type.GetType("System.String")));
                objTableVisualObject.Columns.Add(new DataColumn(_Params, System.Type.GetType("System.String")));
            }
        }

        /// <summary>
        /// Will write large multi-scan CSV file, unfiltered, including all values
        /// </summary>
        /// <param name="sFilename">filename and path to use</param>
        /// <param name="cCSVDelimiter">delimiter to separate data</param>
        /// <returns>true if it was possible save file or false otherwise</returns>
        public bool SaveFileCSV(string sFilename, char cCSVDelimiter)
        {
            bool bOk = true;

            if (m_nUpperBound <= 0)
            {
                return false;
            }

            RFEBinaryPacketData objFirst = m_arrData[0];
            try
            {
                using (StreamWriter myFile = new StreamWriter(sFilename, false))
                {
                    myFile.WriteLine("RF Explorer CSV data file: " + CSVComplete_FileHeaderVersioned_001());
                    myFile.WriteLine("Date" + cCSVDelimiter + "Time" + cCSVDelimiter + "Freq(MHZ)" + cCSVDelimiter + "SampleRate" + cCSVDelimiter + "RBW(KHZ)" + cCSVDelimiter + "Threshold(dBm)");
                    foreach (RFEBinaryPacketData objData in m_arrData)
                    {
                        if (objData == null)
                            break;
                        //From october 2017 we always save data with "en-US" settings
                        myFile.WriteLine(objData.CaptureTime.ToShortDateString() + cCSVDelimiter + objData.CaptureTime.ToString("HH:mm:ss") + cCSVDelimiter +
                            objData.FrequencyMHZ.ToString("f3", CultureInfo.InvariantCulture) + cCSVDelimiter + objData.SampleRate.ToString() + cCSVDelimiter +
                            objData.RBWKhz.ToString("f3", CultureInfo.InvariantCulture) + cCSVDelimiter + objData.ThresholdDBM.ToString("f1", CultureInfo.InvariantCulture));

                        string sLine = "";
                        for (UInt16 nIndex = 0; nIndex < objData.Count; nIndex++)
                        {
                            if (nIndex != 0)
                                sLine += cCSVDelimiter;
                            sLine += objData.GetSampleValue(nIndex).ToString();
                        }
                        myFile.WriteLine(sLine);
                    }
                }
            }
            catch (Exception obEx)
            {
                ReportLog(obEx.Message.ToString());
                bOk = false;
            }
            return bOk;
        }

        /// <summary>
        /// Save in XML file all data 
        /// </summary>
        /// <param name="sFilename">User selected path</param>
        /// <returns>true if it was possible save file or false otherwise</returns>
        public bool SaveXML(string sFilename)
        {
            bool bOKReadXML = true;

            m_XMLSnifferData.Clear();

            if (m_XMLSnifferData.Tables.Count == 0)
                CreateXMLSchema();
            
            try
            {
                int nIDTotal = 0;
                int nIDCapture = 0;
                for (int nPos = 0; (nPos < m_arrData.Length) && (m_arrData[nPos] != null); nPos++)
                {
                    string sTempLine = "";
                    DataRow objRowPacketData = m_XMLSnifferData.Tables[_PacketData].NewRow();
                    objRowPacketData[_Version] = XML_FileHeaderVersioned_002();
                    if (m_arrData[nPos].ChartTitle!=null)
                        objRowPacketData[_Title] = m_arrData[nPos].ChartTitle;
                    else
                        objRowPacketData[_Title] = "";
                    objRowPacketData[_CaptureTime] = m_arrData[nPos].CaptureTime;
                    objRowPacketData[_TotalSamples] = m_arrData[nPos].Count;
                    objRowPacketData[_FrequencyMHZ] = m_arrData[nPos].FrequencyMHZ;
                    objRowPacketData[_RBWKhz] = m_arrData[nPos].RBWKhz;
                    objRowPacketData[_SampleRate] = m_arrData[nPos].SampleRate;
                    objRowPacketData[_ThresholdDBM] = m_arrData[nPos].ThresholdDBM;

                    if (m_arrData[nPos].VisualObjectCount() > 0)
                    {
                        for (int nInd = 0; nInd < m_arrData[nPos].VisualObjectCount(); nInd++)
                        {
                            if (!String.IsNullOrEmpty(sTempLine))
                                sTempLine += ";" + nIDTotal;
                            else
                                sTempLine += nIDTotal;
                            nIDTotal++;
                        }
                        objRowPacketData[_VisualObjectRef] = sTempLine; //rows indixes of visualobject
                    }

                    sTempLine = "";
                    for (int nInd = 0; nInd < m_arrData[nPos].m_arrSamples.Length; nInd++)
                    {
                        if (!String.IsNullOrEmpty(sTempLine))
                            sTempLine += ";" + m_arrData[nPos].m_arrSamples[nInd];
                        else
                            sTempLine += m_arrData[nPos].m_arrSamples[nInd];
                    }
                    objRowPacketData[_RAWSamples] = sTempLine;

                    sTempLine = "";
                    if (m_arrData[nPos].m_arrFilteredPos != null)
                    {
                        for (int nInd = 0; nInd < m_arrData[nPos].m_arrFilteredPos.Length; nInd++)
                        {
                            if (!String.IsNullOrEmpty(sTempLine))
                                sTempLine += ";" + m_arrData[nPos].m_arrFilteredPos[nInd];
                            else
                                sTempLine += m_arrData[nPos].m_arrFilteredPos[nInd];
                        }
                        objRowPacketData[_FilteredPos] = sTempLine;
                    }

                    sTempLine = "";
                    if (m_arrData[nPos].m_arrFilteredSamples != null)
                    {
                        for (int nInd = 0; nInd < m_arrData[nPos].m_arrFilteredSamples.Length; nInd++)
                        {
                            if (!String.IsNullOrEmpty(sTempLine))
                                sTempLine += ";" + m_arrData[nPos].m_arrFilteredSamples[nInd];
                            else
                                sTempLine += m_arrData[nPos].m_arrFilteredSamples[nInd];
                        }
                        objRowPacketData[_FilteredSamples] = sTempLine;
                    }

                    sTempLine = "";
                    if (m_arrData[nPos].m_arrDecodedBits != null)
                    {
                        for (int nInd = 0; nInd < m_arrData[nPos].m_arrDecodedBits.Length; nInd++)
                        {
                            if (!String.IsNullOrEmpty(sTempLine))
                                sTempLine += ";" + m_arrData[nPos].m_arrDecodedBits[nInd];
                            else
                                sTempLine += m_arrData[nPos].m_arrDecodedBits[nInd];
                        }
                        objRowPacketData[_DecodedBits] = sTempLine;
                    }

                    sTempLine = "";
                    if (m_arrData[nPos].m_arrDecodedBitsPos != null)
                    {
                        for (int nInd = 0; nInd < m_arrData[nPos].m_arrDecodedBitsPos.Length; nInd++)
                        {
                            if (!String.IsNullOrEmpty(sTempLine))
                                sTempLine += ";" + m_arrData[nPos].m_arrDecodedBitsPos[nInd];
                            else
                                sTempLine += m_arrData[nPos].m_arrDecodedBitsPos[nInd];
                        }
                        objRowPacketData[_DecodedBitsPos] = sTempLine;
                    }

                    sTempLine = "";
                    if (m_arrData[nPos].m_arrDecodedWordText != null)
                    {
                        for (int nInd = 0; nInd < m_arrData[nPos].m_arrDecodedWordText.Length; nInd++)
                        {
                            if (!String.IsNullOrEmpty(sTempLine))
                                sTempLine += ";" + m_arrData[nPos].m_arrDecodedWordText[nInd];
                            else
                                sTempLine += m_arrData[nPos].m_arrDecodedWordText[nInd];
                        }
                        objRowPacketData[_DecodedWords] = sTempLine;
                    }

                    sTempLine = "";
                    if (m_arrData[nPos].m_arrDecodedWordTextPos != null)
                    {
                        for (int nInd = 0; nInd < m_arrData[nPos].m_arrDecodedWordTextPos.Length; nInd++)
                        {
                            if (!String.IsNullOrEmpty(sTempLine))
                                sTempLine += ";" + m_arrData[nPos].m_arrDecodedWordTextPos[nInd];
                            else
                                sTempLine += m_arrData[nPos].m_arrDecodedWordTextPos[nInd];
                        }
                        objRowPacketData[_DecodedWordsPos] = sTempLine;
                    }

                    if (RFEBinaryPacketData.eProtocol.PT2264 == m_arrData[nPos].GetDecodedProtocol())
                        objRowPacketData[_Protocol] = RFEBinaryPacketData.eProtocol.PT2264.ToString();
                    else if (RFEBinaryPacketData.eProtocol.HT12 == m_arrData[nPos].GetDecodedProtocol())
                        objRowPacketData[_Protocol] = RFEBinaryPacketData.eProtocol.HT12.ToString();
                    else if (RFEBinaryPacketData.eProtocol.EXTERNAL == m_arrData[nPos].GetDecodedProtocol())
                        objRowPacketData[_Protocol] = RFEBinaryPacketData.eProtocol.EXTERNAL.ToString();
                    else if (RFEBinaryPacketData.eProtocol.NONE == m_arrData[nPos].GetDecodedProtocol())
                        objRowPacketData[_Protocol] = RFEBinaryPacketData.eProtocol.NONE.ToString();

                    m_XMLSnifferData.Tables[_PacketData].Rows.Add(objRowPacketData);

                    if (m_arrData[nPos].VisualObjectCount() > 0)
                    {
                        for (int nInt = 0; nInt < m_arrData[nPos].VisualObjectCount(); nInt++)
                        {
                            DataRow objRowVisualObject = m_XMLSnifferData.Tables[_VisualObject].NewRow();
                            objRowVisualObject[_ObjectID] = nIDCapture;
                            objRowVisualObject[_ObjectType] = "ObjText";
                            objRowVisualObject[_X] = m_arrData[nPos].m_arrVisualObj[nInt].X;
                            objRowVisualObject[_Y] = m_arrData[nPos].m_arrVisualObj[nInt].Y;
                            objRowVisualObject[_Text] = m_arrData[nPos].m_arrVisualObj[nInt].Text;
                            objRowVisualObject[_Params] = "";

                            m_XMLSnifferData.Tables[_VisualObject].Rows.Add(objRowVisualObject);
                            nIDCapture++;
                        }
                    }
                }

                m_XMLSnifferData.WriteXml(sFilename, XmlWriteMode.WriteSchema);
            }
            catch (XmlException obXmlEx)
            {
                ReportLog("ERROR saving XML file:" + obXmlEx.Message.ToString());
                bOKReadXML = false;
            }
            catch (Exception obEx)
            {
                ReportLog(obEx.Message.ToString());
                bOKReadXML = false;
            }
            return bOKReadXML;
        }

        /// <summary>
        /// Load all data from XML file
        /// </summary>
        /// <param name="sFilename">Specific path to get data from xml file</param>
        /// <returns>true if it was possible read file or false otherwise</returns>
        public bool LoadXML(string sFilename)
        {
            bool bOKReadXML = true;
            DateTime Time;
            ushort nTotalSamples = 0;
            double fFrequencyMHZ = 0;
            double fRBWKhz = 0;
            uint nSampleRate = 0;
            float fThresholdDBM = 0;
            byte[] arrSamples = null;
            string sLine = null;
            string sProtocol = null;
            string sTitle = null;
            try
            {
                CleanAll();
                List<RFEBinaryPacketData> listTempData = new List<RFEBinaryPacketData>();
                List<UInt16[]> listVisualObjectRef = new List<UInt16[]>();
                //open new settings file
                m_XMLSnifferData = new DataSet("RF_Explorer_Sniffer_Data_Structure");
                if (!File.Exists(sFilename))
                {
                    CreateXMLSchema();
                }
                //Load custom name saved properties
                if (m_XMLSnifferData.Tables.Count == 0)
                {
                    //do not load it twice or records will be repeated
                    m_XMLSnifferData.ReadXml(sFilename);
                }

                DataRow[] objRowTable1 = m_XMLSnifferData.Tables[_PacketData].Select();
                if (objRowTable1.Length > 0)
                {
                    for (int nPos = 0; nPos < objRowTable1.Length; nPos++)
                    {
                        DataRow objRowPacketData = objRowTable1[nPos];
                        UInt16[] arrFilteredPos = null;
                        byte[] arrFilteredSamples = null;
                        byte[] arrDecodedBits = null;
                        UInt32[] arrDecodedBitsPos = null; 
                        string[] arrDecodedWordText = null;
                        UInt32[] arrDecodedWordTextPos = null;

                        //Version 001
                        {
                            Time = (DateTime)objRowPacketData[_CaptureTime];
                            nTotalSamples = (ushort)objRowPacketData[_TotalSamples];
                            fFrequencyMHZ = (double)objRowPacketData[_FrequencyMHZ];
                            fRBWKhz = (double)objRowPacketData[_RBWKhz];
                            nSampleRate = (uint)objRowPacketData[_SampleRate];
                            fThresholdDBM = (float)objRowPacketData[_ThresholdDBM];
                            sLine = null;

                            if (objRowPacketData[_FilteredPos] != DBNull.Value)
                            {
                                sLine = (string)objRowPacketData[_FilteredPos];
                                arrFilteredPos = Array.ConvertAll(sLine.Split(';'), UInt16.Parse);
                            }

                            sLine = (string)objRowPacketData[_RAWSamples];
                            arrSamples = Array.ConvertAll(sLine.Split(';'), Byte.Parse);

                            if (objRowPacketData[_FilteredSamples] != DBNull.Value)
                            {
                                sLine = (string)objRowPacketData[_FilteredSamples];
                                arrFilteredSamples = Array.ConvertAll(sLine.Split(';'), Byte.Parse);
                            }

                            if (objRowPacketData[_DecodedBits] != DBNull.Value)
                            {
                                sLine = (string)objRowPacketData[_DecodedBits];
                                arrDecodedBits = Array.ConvertAll(sLine.Split(';'), Byte.Parse);
                            }

                            if (objRowPacketData[_VisualObjectRef] != DBNull.Value)
                            {
                                sLine = (string)objRowPacketData[_VisualObjectRef];
                                UInt16[] arrVisualObjectID = Array.ConvertAll(sLine.Split(';'), UInt16.Parse);
                                listVisualObjectRef.Add(arrVisualObjectID);
                            }

                            sProtocol = _MODE_SNIFFER_NONE;
                            if (objRowPacketData[_Protocol] != DBNull.Value)
                                sProtocol = (string)objRowPacketData[_Protocol];
                        }

                        //version 002
                        {
							bool bOkV2=true;
                            if (objRowPacketData[_Version] != DBNull.Value)
                            {
                                sLine = (string)objRowPacketData[_Version];
                            }
                            if (sLine == XML_FileHeaderVersioned_002())
                            {
                                if (objRowPacketData[_Title] != DBNull.Value)
                                {
                                    sTitle = (string)objRowPacketData[_Title];
                                }
                                if ((sProtocol != _MODE_SNIFFER_NONE) && (objRowPacketData[_DecodedBitsPos] == DBNull.Value))
                                    bOkV2 = false;

                                if (bOkV2 && objRowPacketData[_DecodedBitsPos] != DBNull.Value)
                                {
                                    if (sProtocol == _MODE_SNIFFER_NONE)
                                        bOkV2 = false;
                                    if (bOkV2)
                                    {
                                        sLine = (string)objRowPacketData[_DecodedBitsPos];
                                        arrDecodedBitsPos = Array.ConvertAll(sLine.Split(';'), UInt32.Parse);
                                    }
                                    if (bOkV2 && objRowPacketData[_DecodedWords] != DBNull.Value)
                                    {
                                        sLine = (string)objRowPacketData[_DecodedWords];
                                        arrDecodedWordText = sLine.Split(';');
                                    }
                                    else
                                        bOkV2 = false;
                                    if (bOkV2 && objRowPacketData[_DecodedWordsPos] != DBNull.Value)
                                    {
                                        sLine = (string)objRowPacketData[_DecodedWordsPos];
                                        arrDecodedWordTextPos = Array.ConvertAll(sLine.Split(';'), UInt32.Parse);
                                    }
                                    else
                                        bOkV2 = false;
                                }
                            }
                            else
                                bOkV2 = false;
                            if (!bOkV2)
                                ReportLog("Error: in LoadXML(). Some version 2 data are missing or incorrected: " + Environment.NewLine + sLine);
                        }

                        //version 003
                        {
                        }

                        RFEBinaryPacketData RFEPacketData = new RFEBinaryPacketData(fFrequencyMHZ, fRBWKhz, nTotalSamples, nSampleRate, fThresholdDBM);
                        RFEPacketData.m_Time = Time;
                        if (sProtocol == RFEBinaryPacketData.eProtocol.PT2264.ToString())
                            RFEPacketData.SetDecodedProtocol(RFEBinaryPacketData.eProtocol.PT2264);
                        else if (sProtocol == RFEBinaryPacketData.eProtocol.HT12.ToString())
                            RFEPacketData.SetDecodedProtocol(RFEBinaryPacketData.eProtocol.HT12);
                        else if (sProtocol == RFEBinaryPacketData.eProtocol.EXTERNAL.ToString())
                            RFEPacketData.SetDecodedProtocol(RFEBinaryPacketData.eProtocol.EXTERNAL);
                        else if (sProtocol == RFEBinaryPacketData.eProtocol.NONE.ToString())
                            RFEPacketData.SetDecodedProtocol(RFEBinaryPacketData.eProtocol.NONE);
                        if (arrFilteredPos != null)
                            RFEPacketData.m_arrFilteredPos = arrFilteredPos;
                        RFEPacketData.m_arrSamples = arrSamples;
                        RFEPacketData.m_arrFilteredSamples = arrFilteredSamples;
                        RFEPacketData.m_arrDecodedBits = arrDecodedBits;
                        RFEPacketData.m_arrDecodedBitsPos = arrDecodedBitsPos;
                        RFEPacketData.m_arrDecodedWordText = arrDecodedWordText;
                        RFEPacketData.m_arrDecodedWordTextPos = arrDecodedWordTextPos;
                        RFEPacketData.ChartTitle = sTitle;

                        Add(RFEPacketData);
                    }
                }

                DataRow[] objRowTable2 = m_XMLSnifferData.Tables[_VisualObject].Select();
                if (objRowTable2.Length > 0)
                {
                    for (int nInd = 0; nInd < listVisualObjectRef.Count; nInd++)
                    {
                        for (int nPos = 0; nPos < listVisualObjectRef[nInd].Length; nPos++)
                        {
                            DataRow objRowVisualObject = objRowTable2[listVisualObjectRef[nInd][nPos]];
                            m_arrData[nInd].SetVisualObject((float)objRowVisualObject[_X], (float)objRowVisualObject[_Y], (string)objRowVisualObject[_Text]);
                        }
                    }
                }
            }
            catch (XmlException obXmlEx)
            {
                ReportLog("ERROR reading XML file:" + obXmlEx.Message.ToString());
                bOKReadXML = false;
            }
            catch (Exception obEx)
            {
                ReportLog(obEx.Message.ToString());
                bOKReadXML = false; 
            }
            return bOKReadXML;
        }

        /// <summary>
        /// Return the data pointed by the zero-starting index
        /// </summary>
        /// <param name="nIndex"></param>
        /// <returns>returns null if no data is available with this index</returns>
        public RFEBinaryPacketData GetData(UInt16 nIndex)
        {
            if (nIndex <= m_nUpperBound)
            {
                return m_arrData[nIndex];
            }
            else
                return null;
        }

        /// <summary>
        /// True when the absolute maximum of allowed elements in the container is allocated
        /// </summary>
        /// <returns></returns>
        public bool IsFull()
        {
            return (m_nUpperBound >= MAX_PACKETS);
        }

        /// <summary>
        /// Remove all existing data
        /// </summary>
        public void CleanAll()
        {
            if (m_arrData != null)
            {
                Array.Clear(m_arrData, 0, m_arrData.Length);
            }
            m_arrData = new RFEBinaryPacketData[MAX_PACKETS];
            m_nUpperBound = -1;
        }

        /// <summary>
        /// Add a packet data object
        /// </summary>
        /// <param name="PacketData"></param>
        /// <returns></returns>
        public bool Add(RFEBinaryPacketData PacketData)
        {
            try
            {
                if (IsFull())
                    return false;

                m_nUpperBound++;
                m_arrData[m_nUpperBound] = PacketData;
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Generate an event with error text which is sent to mainform
        /// </summary>
        /// <param name="sLine">error text</param>
        public void ReportLog(string sLine)
        {
            OnReportInfo(new EventReportInfo(sLine));
        }
    }
}
