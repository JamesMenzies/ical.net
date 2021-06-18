using System;
using System.Collections.Generic;
using System.Text;

namespace Ical.Net.DataTypes
{
    public class QuotedPrintableString : IEncodableDataType
    {
        public string Encoding { get; set; }

        public string Charset { get; set; }

        public string Value { get; set; }
    }
}
