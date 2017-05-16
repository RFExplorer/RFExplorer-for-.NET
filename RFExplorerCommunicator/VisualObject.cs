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

using System;

namespace RFExplorerCommunicator
{
    /// <summary>
    /// This class is used to store information about objects which are used on Zedgraph to draw text (in a future other things)
    /// </summary>
    public class VisualObject : Object
    {
        float m_fX, m_fY;
        string m_sText;

        /// <summary>
        /// Set or Get X axis coordinate of user defined text
        /// </summary>
        public float X
        {
            get
            {
                return m_fX;
            }

            set
            {
                m_fX = value;
            }
        }

        /// <summary>
        /// Set or Get Y axis coordinate of user defined text
        /// </summary>
        public float Y
        {
            get
            {
                return m_fY;
            }

            set
            {
                m_fY = value;
            }
        }

        /// <summary>
        /// Get or Set user defined text
        /// </summary>
        public string Text
        {
            get
            {
                return m_sText;
            }

            set
            {
                m_sText = value;
            }
        }

        enum eVisualObjectType {TEXT_OBJ, NONE }

        /// <summary>
        /// Store chart position and text defined by a user
        /// </summary>
        /// <param name="fX">X axis of the user defined text position</param>
        /// <param name="fY">Y axis of the user defined text position</param>
        /// <param name="sText">user defined text</param>
        public VisualObject(float fX, float fY, string sText)
        {
            X = fX; 
            m_fY = fY;
            m_sText = sText;
        }
    }
}
