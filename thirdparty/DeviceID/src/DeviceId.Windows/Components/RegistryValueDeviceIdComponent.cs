using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace DeviceId.Windows.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that retrieves its value from the Windows registry.
/// </summary>
public class RegistryValueDeviceIdComponent : IDeviceIdComponent
{
#if !NET35
    /// <summary>
    /// The registry views.
    /// </summary>
    private readonly RegistryView[] _registryViews;

    /// <summary>
    /// The registry hive.
    /// </summary>
    private readonly RegistryHive _registryHive;
#endif

    /// <summary>
    /// The name of the registry key.
    /// </summary>
    private readonly string _keyName;

    /// <summary>
    /// The name of the registry value.
    /// </summary>
    private readonly string _valueName;

    /// <summary>
    /// An optional function to use to format the value before returning it.
    /// </summary>
    private readonly Func<string, string> _formatter;

#if NET35
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryValueDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="keyName">The name of the registry key.</param>
    /// <param name="valueName">The name of the registry value.</param>
    public RegistryValueDeviceIdComponent(string keyName, string valueName)
        : this(keyName, valueName, null) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryValueDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="keyName">The name of the registry key.</param>
    /// <param name="valueName">The name of the registry value.</param>
    /// <param name="formatter">An optional function to use to format the value before returning it.</param>
    public RegistryValueDeviceIdComponent(string keyName, string valueName, Func<string, string> formatter)
    {
        _keyName = keyName;
        _valueName = valueName;
        _formatter = formatter;
    }
#else
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryValueDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="registryView">The registry view.</param>
    /// <param name="registryHive">The registry hive.</param>
    /// <param name="keyName">The name of the registry key.</param>
    /// <param name="valueName">The name of the registry value.</param>
    public RegistryValueDeviceIdComponent(RegistryView registryView, RegistryHive registryHive, string keyName, string valueName)
        : this(registryView, registryHive, keyName, valueName, null) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryValueDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="registryView">The registry view.</param>
    /// <param name="registryHive">The registry hive.</param>
    /// <param name="keyName">The name of the registry key.</param>
    /// <param name="valueName">The name of the registry value.</param>
    /// <param name="formatter">An optional function to use to format the value before returning it.</param>
    public RegistryValueDeviceIdComponent(RegistryView registryView, RegistryHive registryHive, string keyName, string valueName, Func<string, string> formatter)
        : this(new RegistryView[] { registryView }, registryHive, keyName, valueName, formatter) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryValueDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="registryViews">The registry views.</param>
    /// <param name="registryHive">The registry hive.</param>
    /// <param name="keyName">The name of the registry key.</param>
    /// <param name="valueName">The name of the registry value.</param>
    /// <param name="formatter">An optional function to use to format the value before returning it.</param>
    public RegistryValueDeviceIdComponent(IEnumerable<RegistryView> registryViews, RegistryHive registryHive, string keyName, string valueName, Func<string, string> formatter)
    {
        _registryViews = registryViews.ToArray();
        _registryHive = registryHive;
        _keyName = keyName;
        _valueName = valueName;
        _formatter = formatter;
    }
#endif

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
#if NET35
        // In .NET 3.5, it's not possible to specify the registry view.
        // Technically I could write some native API calls to do it properly,
        // but we're going to drop support for .NET 3.5 soon anyway, so I don't really want to bother.

        try
        {
            var value = Registry.GetValue(_keyName, _valueName, null);
            var valueAsString = value?.ToString();
            if (valueAsString is null)
            {
                return null;
            }

            return _formatter?.Invoke(valueAsString) ?? valueAsString;
        }
        catch { }
#else
        foreach (var registryView in _registryViews)
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(_registryHive, registryView);
                using var subKey = registry.OpenSubKey(_keyName);
                if (subKey != null)
                {
                    var value = subKey.GetValue(_valueName);
                    var valueAsString = value?.ToString();
                    if (valueAsString != null)
                    {
                        return _formatter?.Invoke(valueAsString) ?? valueAsString;
                    }
                }
            }
            catch { }
        }
#endif

        return null;
    }
}
