using System.Collections;
using System.Globalization;
using System.Text;
using System.Xml;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Mapping;

namespace DotnetGlpiAgent.Protocol.Serialization;

public static class LegacyXmlSerializer
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = true,
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        OmitXmlDeclaration = false,
    };

    public static byte[] SerializeInventory(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProtocolContent content = InventoryContentMapper.Map(snapshot);
        return WriteDocument(writer =>
        {
            writer.WriteStartElement("REQUEST");
            writer.WriteElementString("DEVICEID", snapshot.Identity.DeviceId);
            writer.WriteElementString("QUERY", "INVENTORY");
            writer.WriteStartElement("CONTENT");
            foreach ((string name, object value) in content.Values)
            {
                WriteValue(writer, name, value);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        });
    }

    public static byte[] SerializeProlog(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return WriteDocument(writer =>
        {
            writer.WriteStartElement("REQUEST");
            writer.WriteElementString("DEVICEID", deviceId);
            writer.WriteElementString("QUERY", "PROLOG");
            writer.WriteEndElement();
        });
    }

    private static byte[] WriteDocument(Action<XmlWriter> write)
    {
        using var stream = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(stream, WriterSettings))
        {
            writer.WriteStartDocument();
            write(writer);
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static void WriteValue(XmlWriter writer, string name, object value)
    {
        if (value is string text)
        {
            writer.WriteElementString(XmlName(name), SanitizeXmlText(text));
            return;
        }

        if (value is IEnumerable sequence and not IReadOnlyDictionary<string, object?> and not IReadOnlyDictionary<string, object>)
        {
            foreach (object? item in sequence)
            {
                if (item is not null)
                {
                    WriteValue(writer, name, item);
                }
            }

            return;
        }

        writer.WriteStartElement(XmlName(name));
        if (value is IReadOnlyDictionary<string, object?> nullableValues)
        {
            foreach ((string childName, object? childValue) in nullableValues)
            {
                if (childValue is not null)
                {
                    WriteValue(writer, childName, childValue);
                }
            }
        }
        else if (value is IReadOnlyDictionary<string, object> values)
        {
            foreach ((string childName, object childValue) in values)
            {
                WriteValue(writer, childName, childValue);
            }
        }
        else
        {
            writer.WriteString(SanitizeXmlText(FormatScalar(value)));
        }

        writer.WriteEndElement();
    }

    private static string FormatScalar(object value)
    {
        return value switch
        {
            bool boolean => boolean ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Maps native JSON content keys to OCS/FusionInventory XML section names used by
    /// the official Perl agent and historical comparison tooling.
    /// </summary>
    private static string XmlName(string name)
    {
        string upper = name.ToUpperInvariant();
        return upper switch
        {
            "FIREWALLS" => "FIREWALL",
            "ANTIVIRUS" => "ANTIVIRUS",
            _ => upper,
        };
    }

    /// <summary>
    /// Removes characters that are illegal in XML 1.0 text nodes (e.g. 0x00-0x08, 0x0B-0x0C, 0x0E-0x1F).
    /// Inventory sources occasionally emit control characters that would crash XmlWriter.
    /// </summary>
    public static string SanitizeXmlText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (IsLegalXmlChar(value[i]))
            {
                continue;
            }

            var builder = new StringBuilder(value.Length);
            for (var j = 0; j < value.Length; j++)
            {
                if (IsLegalXmlChar(value[j]))
                {
                    builder.Append(value[j]);
                }
            }

            return builder.ToString();
        }

        return value;
    }

    private static bool IsLegalXmlChar(char ch)
    {
        // XML 1.0: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD]
        return ch is '\t' or '\n' or '\r'
            or (>= '\u0020' and <= '\uD7FF')
            or (>= '\uE000' and <= '\uFFFD');
    }
}
