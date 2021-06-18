using System;
using System.IO;

namespace Ical.Net.Utility
{
    public static class TextReaderExtension
    {
        public static char ReadChar(this TextReader reader)
        {
            var c = reader.Read();
            if (c > -1)
                return Convert.ToChar(c);
            else
                return '\0';
        }

        public static char PeekChar(this TextReader reader)
        {
            var c = reader.Peek();
            if (c > -1)
                return Convert.ToChar(c);
            else
                return '\0';
        }
    }
}
