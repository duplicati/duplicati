using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace DeviceId.Formatters;

/// <summary>
/// An implementation of <see cref="IDeviceIdFormatter"/> that combines the components into an XML string.
/// </summary>
public class XmlDeviceIdFormatter : IDeviceIdFormatter
{
    /// <summary>
    /// The <see cref="IDeviceIdComponentEncoder"/> instance to use to encode individual components.
    /// </summary>
    private readonly IDeviceIdComponentEncoder _encoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDeviceIdFormatter"/> class.
    /// </summary>
    /// <param name="encoder">The <see cref="IDeviceIdComponentEncoder"/> instance to use to encode individual components.</param>
    public XmlDeviceIdFormatter(IDeviceIdComponentEncoder encoder)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
    }

    /// <summary>
    /// Returns the device identifier string created by combining the specified components.
    /// </summary>
    /// <param name="components">A dictionary containing the components.</param>
    /// <returns>The device identifier string.</returns>
    public string GetDeviceId(IDictionary<string, IDeviceIdComponent> components)
    {
        if (components == null)
        {
            throw new ArgumentNullException(nameof(components));
        }

        var document = new XDocument(GetElement(components));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Returns an XML element representing the specified components.
    /// </summary>
    /// <param name="components">A dictionary containing the components.</param>
    /// <returns>An XML element representing the specified component values.</returns>
    private XElement GetElement(IDictionary<string, IDeviceIdComponent> components)
    {
        var elements = components
            .OrderBy(x => x.Key)
            .Select(x => GetElement(x.Key, x.Value));

        return new XElement("DeviceId", elements);
    }

    /// <summary>
    /// Returns an XML element representing the specified component.
    /// </summary>
    /// <param name="name">The component name.</param>
    /// <param name="value">The component.</param>
    /// <returns>An XML element representing the specified component.</returns>
    private XElement GetElement(string name, IDeviceIdComponent value)
    {
        return new XElement("Component",
            new XAttribute("Name", name),
            new XAttribute("Value", _encoder.Encode(value)));
    }
}
