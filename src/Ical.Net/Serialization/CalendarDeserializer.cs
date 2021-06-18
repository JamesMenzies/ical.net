using Ical.Net.CalendarComponents;
using Ical.Net.Utility;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace Ical.Net.Serialization
{
    internal class CalendarDeserializer : IEnumerable<ICalendarComponent>
    {
        private TextReader _reader { get; }

        private int _currentLine;

        private readonly DataTypeMapper _dataTypeMapper;
        private readonly ISerializerFactory _serializerFactory;
        private readonly CalendarComponentFactory _componentFactory;

        internal CalendarDeserializer(TextReader reader)
            : this(reader,
            new DataTypeMapper(),
            new SerializerFactory(),
            new CalendarComponentFactory()) { }

        internal CalendarDeserializer(TextReader reader, 
            DataTypeMapper dataTypeMapper,
            ISerializerFactory serializerFactory,
            CalendarComponentFactory componentFactory)
        {
            _reader = reader;
            _dataTypeMapper = dataTypeMapper;
            _serializerFactory = serializerFactory;
            _componentFactory = componentFactory;
        }

        private IEnumerable<ICalendarComponent> GetComponents()
        {
            var context = new SerializationContext();
            var stack = new Stack<ICalendarComponent>();
            var current = default(ICalendarComponent);
            foreach (var property in GetProperty(context))
            {
                context.Push(property);
                if (string.Equals(property.Name, "BEGIN", StringComparison.OrdinalIgnoreCase))
                {
                    stack.Push(current);
                    current = _componentFactory.Build((string)property.Value);
                    SerializationUtil.OnDeserializing(current);
                }
                else
                {
                    if (current == null)
                    {
                        throw new SerializationException($"Expected 'BEGIN' at line {_currentLine}, found '{property.Name}'");
                    }
                    if (string.Equals(property.Name, "END", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.Equals((string)property.Value, current.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new SerializationException($"Expected 'END:{current.Name}' at line {_currentLine}, found 'END:{property.Value}'");
                        }
                        SerializationUtil.OnDeserialized(current);
                        var finished = current;
                        current = stack.Pop();
                        if (current == null)
                        {
                            yield return finished;
                        }
                        else
                        {
                            current.Children.Add(finished);
                        }
                    }
                    else
                    {
                        current.Properties.Add(property);
                    }
                }
                context.Pop();
            }
        }

        private IEnumerable<ICalendarProperty> GetProperty(SerializationContext context)
        {
            while (_reader.Peek() > 0)
            {
                var (fieldname, hasProperties) = ReadFieldName();
                var property = new CalendarProperty(fieldname.ToUpperInvariant());
                context.Push(property);
                if (hasProperties)
                {
                    foreach (var parameter in ReadFieldParameters())
                    {
                        property.AddParameter(parameter);
                    }
                }
                AddPropertyValue(property, context);
                context.Pop();
                yield return property;
            }
        }

        private (string fieldname, bool hasProperties) ReadFieldName()
        {
            var sb = new StringBuilder();
            while (true)
            {
                var c = Convert.ToChar(_reader.Read());
                switch (c)
                {
                    case ':':
                    case ';':
                        return ( sb.ToString() , c == ';' );
                    case '\t':
                    case ' ':
                    case '\r':
                    case '\n':
                        throw new SerializationException($"Unexpected Whitespace in Field name at line {_currentLine}");
                    default:
                        sb.Append(c);
                        break;
                }
            }
            throw new SerializationException($"Expected ':' or ';' while reading Field name at line {_currentLine}");
        }

        private IEnumerable<CalendarParameter> ReadFieldParameters()
        {
            var sb = new StringBuilder();
            bool foundColon = false;
            bool quoted = false;
            var currentparameter = default(CalendarParameter);
            while (!foundColon)
            {
                var c = _reader.ReadChar();
                switch (c)
                {
                    case '=' when !quoted:
                        currentparameter = new CalendarParameter(sb.ToString());
                        sb.Clear();
                        break;
                    case ',' when !quoted:
                        currentparameter.AddValue(sb.ToString());
                        sb.Clear();
                        break;
                    case ';' when !quoted:
                    case ':' when !quoted:
                        currentparameter.AddValue(sb.ToString());
                        yield return currentparameter;
                        currentparameter = default(CalendarParameter);
                        sb.Clear();
                        foundColon = c == ':';
                        break;
                    case '\r':
                        var pc1 = _reader.PeekChar();
                        if (pc1 != '\n')
                        {
                            throw new SerializationException($"Unexpected NewLine in Field Parameter at line {_currentLine}");
                        }
                        break;
                    case '\n':
                        _currentLine++;
                        var pc2 = _reader.PeekChar();
                        if (pc2 == ' ' || pc2 == '\t')
                        {
                            _reader.Read();
                        }
                        break;
                    case '"':
                        quoted = !quoted;
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
        }

        private void AddPropertyValue(CalendarProperty property, SerializationContext context)
        {
            var fieldValueString = GetValueReader(property)(_reader);

            var type = _dataTypeMapper.GetPropertyMapping(property) ?? typeof(string);
            var serializer = (SerializerBase)_serializerFactory.Build(type, context);
            using (var valueReader = new StringReader(fieldValueString))
            {
                var propertyValue = serializer.Deserialize(valueReader);
                var propertyValues = propertyValue as IEnumerable<string>;
                if (propertyValues != null)
                {
                    foreach (var singlePropertyValue in propertyValues)
                    {
                        property.AddValue(singlePropertyValue);
                    }
                }
                else
                {
                    property.AddValue(propertyValue);
                }
            }
        }

        private Func<TextReader, string> GetValueReader(CalendarProperty calendarProperty)
        {
            if (calendarProperty.Parameters.ContainsKey("ENCODING")
                && calendarProperty.Parameters.Get("ENCODING").Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            {
                return QuotedPrintableFieldReader;
            }
            else
            {
                return BasicFieldValueReader;
            }
        }

        private string BasicFieldValueReader(TextReader reader)
        {
            var reading = true;
            var value = new StringBuilder();
            while (reading)
            {
                var c = reader.ReadChar();
                var nc = reader.PeekChar();
                switch (c)
                {
                    case '\0':
                        reading = false;
                        break;
                    case '\r':
                        if (nc != '\n')
                        {
                            throw new SerializationException($"Unexpected Carriage  Return in Field Value at line {_currentLine}");
                        }
                        break;
                    case '\n':
                        reading = char.IsWhiteSpace(nc);
                        if (nc == ' ' || nc == '\t')
                        {
                            _reader.Read();
                        }
                        break;
                    default:
                        value.Append(c);
                        break;
                }
            }
            return value.ToString();
        }

        private string QuotedPrintableFieldReader(TextReader reader)
        {
            var reading = true;
            var value = new StringBuilder();
            while (reading)
            {
                var c = reader.ReadChar();
                var nc = reader.PeekChar();
                switch (c)
                {
                    case '=' when nc == '\r' || nc == '\n': //if we find a = followed by a newline then we eat it all and append it.
                        value.Append(c);
                        reader.Read();
                        value.Append(nc);
                        var pnc = reader.PeekChar();
                        if (nc == '\r' && pnc == '\n')
                        {
                            value.Append(pnc);
                            reader.Read();
                        }
                        break;
                    case '\0':
                        reading = false;
                        break;
                    case '\r':
                        value.Append(c);
                        if (nc == '\n')
                        {
                            reader.Read();
                        }
                        reading = char.IsWhiteSpace(reader.PeekChar());
                        break;
                    case '\n':
                        reading = char.IsWhiteSpace(reader.PeekChar());
                        value.Append(c);
                        break;
                    default:
                        value.Append(c);
                        break;
                }
            }
            return value.ToString();
        }

        public IEnumerator<ICalendarComponent> GetEnumerator() => GetComponents().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}