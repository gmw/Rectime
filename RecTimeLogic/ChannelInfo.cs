﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace RecTimeLogic
{
    [XmlRoot("tv")]
    public class ChannelInfo
    {
        [XmlElement("programme")]
        public List<ProgramInfo> Programs { get; set; }

        public ChannelInfo()
        {
            Programs = new List<ProgramInfo>();
        }
    }

    public struct ProgramInfo
    {
        [XmlElement("title")]
        public string Title { get; set; }
        [XmlElement("desc")]
        public string Description { get; set; }
        [XmlElement("category")]
        public string Category { get; set; }
        [XmlAttribute("start")]
        public string Start { get; set; }
        [XmlAttribute("stop")]
        public string Stop { get; set; }

        public SourceType Type { get; set; }

        private const string DatePattern = "yyyyMMddHHmmss zzz";
        public DateTime StartTime => DateTimeOffset.ParseExact(Start, DatePattern, CultureInfo.InvariantCulture).DateTime;
        public DateTime StopTime => DateTimeOffset.ParseExact(Stop, DatePattern, CultureInfo.InvariantCulture).DateTime;
        
    }
}
