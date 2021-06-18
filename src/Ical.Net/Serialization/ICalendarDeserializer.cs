using Ical.Net.CalendarComponents;

using System.Collections.Generic;
using System.IO;

namespace Ical.Net.Serialization
{
    public interface ICalendarDeserializer
    {
        IEnumerable<ICalendarComponent> Deserialize(TextReader reader);
    }
}