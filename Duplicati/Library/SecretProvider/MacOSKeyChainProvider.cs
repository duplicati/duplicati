// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Web;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Implementation of a secret provider that reads secrets from the MacOS keychain
/// </summary>
[SupportedOSPlatform("macos")]
public class MacOSKeyChainProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "keychain";

    /// <inheritdoc />
    public string DisplayName => Strings.MacOSKeyChainProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.MacOSKeyChainProvider.Description;

    /// <inheritdoc />
    public Task<bool> IsSupported(CancellationToken cancellationToken) => Task.FromResult(OperatingSystem.IsMacOS());

    /// <inheritdoc />
    public bool IsSetSupported => true;

    /// <summary>
    /// The type of password to get
    /// </summary>
    private enum PasswordType
    {
        /// <summary>
        /// A generic password
        /// </summary>
        Generic,
        /// <summary>
        /// An internet password
        /// </summary>
        Internet
    }

    /// <summary>
    /// The settings for the keychain
    /// </summary>
    private class KeyChainSettings : ICommandLineArgumentMapper
    {
        /// <summary>
        /// The default service name
        /// </summary>
        private const string ServiceDefault = "com.duplicati.secrets";

        /// <summary>
        /// The default account name
        /// </summary>
        private const string AccountDefault = "Duplicati";

        /// <summary>
        /// The service name
        /// </summary>
        public string? Service { get; set; } = ServiceDefault;
        /// <summary>
        /// The account name
        /// </summary>
        public string? Account { get; set; } = AccountDefault;

        /// <summary>
        /// Gets or sets a value indicating whether to use internet passwords as opposed to generic passwords
        /// </summary>
        public PasswordType Type { get; set; } = PasswordType.Generic;

        /// <summary>
        /// Gets the command line argument description for a member
        /// </summary>
        /// <param name="name">The name of the member</param>
        /// <returns>The command line argument description</returns>
        public static CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(string name)
            => name switch
            {
                nameof(Service) => new CommandLineArgumentDescriptionAttribute
                {
                    Name = "service",
                    ShortDescription = Strings.MacOSKeyChainProvider.ServiceDescriptionShort,
                    LongDescription = Strings.MacOSKeyChainProvider.ServiceDescriptionLong,
                    DefaultValue = ServiceDefault
                },
                nameof(Account) => new CommandLineArgumentDescriptionAttribute
                {
                    Name = "account",
                    ShortDescription = Strings.MacOSKeyChainProvider.AccountDescriptionShort,
                    LongDescription = Strings.MacOSKeyChainProvider.AccountDescriptionLong,
                    DefaultValue = AccountDefault
                },
                nameof(Type) => new CommandLineArgumentDescriptionAttribute
                {
                    Name = "type",
                    Type = CommandLineArgument.ArgumentType.Enumeration,
                    ShortDescription = Strings.MacOSKeyChainProvider.TypeDescriptionShort,
                    LongDescription = Strings.MacOSKeyChainProvider.TypeDescriptionLong
                },
                _ => null
            };

        /// <inheritdoc />
        CommandLineArgumentDescriptionAttribute? ICommandLineArgumentMapper.GetCommandLineArgumentDescription(MemberInfo mi)
            => GetCommandLineArgumentDescription(mi.Name);
    }

    /// <summary>
    /// The settings for the keychain; null if not initialized
    /// </summary>
    private KeyChainSettings? _settings;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new KeyChainSettings()).ToList();

    /// <inheritdoc />
    public Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The MacOSKeyChainProvider is only supported on macOS");

        var args = HttpUtility.ParseQueryString(config.Query);
        _settings = CommandLineArgumentMapper.ApplyArguments(new KeyChainSettings(), args);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_settings is null)
            throw new InvalidOperationException("The MacOSKeyChainProvider has not been initialized");

        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await GetStringAsync(key, _settings, cancellationToken).ConfigureAwait(false);
            result[key] = value;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string key, string value, bool overwrite, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The MacOSKeyChainProvider is only supported on MacOS");
        if (_settings is null)
            throw new InvalidOperationException("The MacOSKeyChainProvider has not been initialized");

        cancellationToken.ThrowIfCancellationRequested();
        await SetStringAsync(key, value, overwrite, _settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Native methods for accessing the MacOS keychain
    /// </summary>
    private static class KeychainNative
    {
        /// <summary>
        /// The path to the Security framework
        /// </summary>
        private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";
        /// <summary>
        /// The path to the CoreFoundation framework
        /// </summary>
        private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        /// <summary>
        /// Success status code
        /// </summary>
        public const int errSecSuccess = 0;
        /// <summary>
        /// Duplicate item status code
        /// </summary>
        public const int errSecDuplicateItem = -25299;

        // Classic exact-match APIs (fast path)
        /// <summary>
        /// Finds a generic password item in the keychain.
        /// </summary>
        /// <param name="keychain">The keychain to search, or null for default.</param>
        /// <param name="serviceNameLength">Length of the service name.</param>
        /// <param name="serviceName">The service name.</param>
        /// <param name="accountNameLength">Length of the account name.</param>
        /// <param name="accountName">The account name.</param>
        /// <param name="passwordLength">Output: length of the password data.</param>
        /// <param name="passwordData">Output: pointer to the password data.</param>
        /// <param name="itemRef">Output: reference to the keychain item.</param>
        /// <returns>Status code; 0 for success.</returns>
        [DllImport(SecurityLib)]
        internal static extern int SecKeychainFindGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            byte[] serviceName,
            uint accountNameLength,
            byte[] accountName,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        /// <summary>
        /// Finds an internet password item in the keychain.
        /// </summary>
        /// <param name="keychain">The keychain to search, or null for default.</param>
        /// <param name="serverNameLength">Length of the server name.</param>
        /// <param name="serverName">The server name.</param>
        /// <param name="securityDomainLength">Length of the security domain.</param>
        /// <param name="securityDomain">The security domain.</param>
        /// <param name="accountNameLength">Length of the account name.</param>
        /// <param name="accountName">The account name.</param>
        /// <param name="pathLength">Length of the path.</param>
        /// <param name="path">The path.</param>
        /// <param name="port">The port number.</param>
        /// <param name="protocol">The protocol type.</param>
        /// <param name="authType">The authentication type.</param>
        /// <param name="passwordLength">Output: length of the password data.</param>
        /// <param name="passwordData">Output: pointer to the password data.</param>
        /// <param name="itemRef">Output: reference to the keychain item.</param>
        /// <returns>Status code; 0 for success.</returns>
        [DllImport(SecurityLib)]
        internal static extern int SecKeychainFindInternetPassword(
            IntPtr keychain,
            uint serverNameLength,
            byte[] serverName,
            uint securityDomainLength,
            byte[] securityDomain,
            uint accountNameLength,
            byte[] accountName,
            uint pathLength,
            byte[] path,
            ushort port,
            int protocol,
            int authType,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        /// <summary>
        /// Modifies the attributes and data of a keychain item.
        /// </summary>
        /// <param name="itemRef">Reference to the keychain item.</param>
        /// <param name="attrList">List of attributes to modify, or null.</param>
        /// <param name="length">Length of the data.</param>
        /// <param name="data">The new data.</param>
        /// <returns>Status code; 0 for success.</returns>
        [DllImport(SecurityLib)]
        internal static extern int SecKeychainItemModifyAttributesAndData(
            IntPtr itemRef,
            IntPtr attrList,
            uint length,
            byte[] data);

        /// <summary>
        /// Frees the memory allocated for keychain item content.
        /// </summary>
        /// <param name="attrList">The attribute list to free, or null.</param>
        /// <param name="data">The data to free.</param>
        /// <returns>Status code; 0 for success.</returns>
        [DllImport(SecurityLib)]
        internal static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        // Modern SecItem* APIs (label-aware add/update + label-only lookup)
        /// <summary>
        /// Copies matching items from the keychain.
        /// </summary>
        /// <param name="query">Dictionary containing the query parameters.</param>
        /// <param name="result">Output: the matching item or items.</param>
        /// <returns>Status code; 0 for success.</returns>
        [DllImport(SecurityLib)]
        internal static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

        /// <summary>
        /// Adds an item to the keychain.
        /// </summary>
        /// <param name="attributes">Dictionary containing the item attributes.</param>
        /// <param name="result">Output: reference to the added item.</param>
        /// <returns>Status code; 0 for success.</returns>
        [DllImport(SecurityLib)]
        internal static extern int SecItemAdd(IntPtr attributes, out IntPtr result);

        /// <summary>
        /// Updates an item in the keychain.
        /// </summary>
        /// <param name="query">Dictionary identifying the item to update.</param>
        /// <param name="attributesToUpdate">Dictionary containing the attributes to update.</param>
        /// <returns>Status code; 0 for success.</returns>
        [DllImport(SecurityLib)]
        internal static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

        // CF helpers
        /// <summary>
        /// Creates a mutable CoreFoundation dictionary.
        /// </summary>
        /// <param name="allocator">The allocator to use, or null for default.</param>
        /// <param name="capacity">Initial capacity of the dictionary.</param>
        /// <param name="keyCallBacks">Callbacks for key operations.</param>
        /// <param name="valueCallBacks">Callbacks for value operations.</param>
        /// <returns>Pointer to the created dictionary.</returns>
        [DllImport(CoreFoundationLib)]
        internal static extern IntPtr CFDictionaryCreateMutable(
            IntPtr allocator,
            nint capacity,
            IntPtr keyCallBacks,
            IntPtr valueCallBacks);

        /// <summary>
        /// Sets a value in a CoreFoundation dictionary.
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        [DllImport(CoreFoundationLib)]
        internal static extern void CFDictionarySetValue(IntPtr dict, IntPtr key, IntPtr value);

        /// <summary>
        /// Creates a CoreFoundation string from a C string.
        /// </summary>
        /// <param name="alloc">The allocator to use, or null for default.</param>
        /// <param name="cStr">The C string bytes.</param>
        /// <param name="encoding">The string encoding.</param>
        /// <returns>Pointer to the created string.</returns>
        [DllImport(CoreFoundationLib)]
        internal static extern IntPtr CFStringCreateWithCString(
            IntPtr alloc,
            byte[] cStr,
            uint encoding);

        /// <summary>
        /// Creates CoreFoundation data from a byte array.
        /// </summary>
        /// <param name="allocator">The allocator to use, or null for default.</param>
        /// <param name="bytes">The byte array.</param>
        /// <param name="length">The length of the data.</param>
        /// <returns>Pointer to the created data.</returns>
        [DllImport(CoreFoundationLib)]
        internal static extern IntPtr CFDataCreate(
            IntPtr allocator,
            byte[] bytes,
            nint length);

        /// <summary>
        /// Gets the length of CoreFoundation data.
        /// </summary>
        /// <param name="data">The data object.</param>
        /// <returns>The length of the data.</returns>
        [DllImport(CoreFoundationLib)]
        internal static extern nint CFDataGetLength(IntPtr data);

        /// <summary>
        /// Gets a pointer to the bytes of CoreFoundation data.
        /// </summary>
        /// <param name="data">The data object.</param>
        /// <returns>Pointer to the byte data.</returns>
        [DllImport(CoreFoundationLib)]
        internal static extern IntPtr CFDataGetBytePtr(IntPtr data);

        /// <summary>
        /// Releases a CoreFoundation object.
        /// </summary>
        /// <param name="cf">The object to release.</param>
        [DllImport(CoreFoundationLib)]
        internal static extern void CFRelease(IntPtr cf);

        /// <summary>
        /// UTF-8 encoding constant for CoreFoundation strings.
        /// </summary>
        internal const uint kCFStringEncodingUTF8 = 0x08000100;

        // Constant pointers exported by Security/CoreFoundation, loaded via dlsym
        /// <summary>
        /// Path to libSystem (for dlopen/dlsym).
        /// </summary>
        private const string LibSystem = "/usr/lib/libSystem.B.dylib";

        /// <summary>
        /// Loads a dynamic library.
        /// </summary>
        /// <param name="path">The path to the library.</param>
        /// <param name="mode">The loading mode.</param>
        /// <returns>Handle to the loaded library.</returns>
        [DllImport(LibSystem, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dlopen(string path, int mode);

        /// <summary>
        /// Resolves a symbol from a dynamic library.
        /// </summary>
        /// <param name="handle">Handle to the loaded library.</param>
        /// <param name="symbol">The symbol to resolve.</param>
        /// <returns>Pointer to the resolved symbol.</returns>
        [DllImport(LibSystem, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        /// <summary>
        /// Closes a dynamic library.
        /// </summary>
        /// <param name="handle">Handle to the loaded library.</param>
        /// <returns>Zero on success.</returns>
        [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl)]
        private static extern int dlclose(IntPtr handle);

        private static readonly IntPtr _securityHandle;
        private static readonly IntPtr _coreFoundationHandle;

        /// <summary>
        /// Key for the class attribute in keychain queries.
        /// </summary>
        internal static readonly IntPtr SecClass;
        /// <summary>
        /// Value for generic password class.
        /// </summary>
        internal static readonly IntPtr SecClassGenericPassword;
        /// <summary>
        /// Value for internet password class.
        /// </summary>
        internal static readonly IntPtr SecClassInternetPassword;
        /// <summary>
        /// Key for the service attribute.
        /// </summary>
        internal static readonly IntPtr SecAttrService;
        /// <summary>
        /// Key for the server attribute.
        /// </summary>
        internal static readonly IntPtr SecAttrServer;
        /// <summary>
        /// Key for the account attribute.
        /// </summary>
        internal static readonly IntPtr SecAttrAccount;
        /// <summary>
        /// Key for the label attribute.
        /// </summary>
        internal static readonly IntPtr SecAttrLabel;
        /// <summary>
        /// Key for the value data.
        /// </summary>
        internal static readonly IntPtr SecValueData;
        /// <summary>
        /// Key to specify returning data in queries.
        /// </summary>
        internal static readonly IntPtr SecReturnData;
        /// <summary>
        /// Key for match limit in queries.
        /// </summary>
        internal static readonly IntPtr SecMatchLimit;
        /// <summary>
        /// Value for limiting matches to one.
        /// </summary>
        internal static readonly IntPtr SecMatchLimitOne;
        /// <summary>
        /// CoreFoundation true boolean value.
        /// </summary>
        internal static readonly IntPtr CFBooleanTrue;

        /// <summary>
        /// Static constructor to load native libraries and resolve constant symbols.
        /// </summary>
        static KeychainNative()
        {
            const int RTLD_LAZY = 0x1;

            _securityHandle = dlopen(SecurityLib, RTLD_LAZY);
            _coreFoundationHandle = dlopen(CoreFoundationLib, RTLD_LAZY);

            if (_securityHandle == IntPtr.Zero || _coreFoundationHandle == IntPtr.Zero)
                throw new Exception("Failed to load native Security/CoreFoundation frameworks");

            SecClass = GetSymbol(_securityHandle, "kSecClass");
            SecClassGenericPassword = GetSymbol(_securityHandle, "kSecClassGenericPassword");
            SecClassInternetPassword = GetSymbol(_securityHandle, "kSecClassInternetPassword");
            SecAttrService = GetSymbol(_securityHandle, "kSecAttrService");
            SecAttrServer = GetSymbol(_securityHandle, "kSecAttrServer");
            SecAttrAccount = GetSymbol(_securityHandle, "kSecAttrAccount");
            SecAttrLabel = GetSymbol(_securityHandle, "kSecAttrLabel");
            SecValueData = GetSymbol(_securityHandle, "kSecValueData");
            SecReturnData = GetSymbol(_securityHandle, "kSecReturnData");
            SecMatchLimit = GetSymbol(_securityHandle, "kSecMatchLimit");
            SecMatchLimitOne = GetSymbol(_securityHandle, "kSecMatchLimitOne");
            CFBooleanTrue = GetSymbol(_coreFoundationHandle, "kCFBooleanTrue");
        }

        /// <summary>
        /// Resolves a symbol and reads its pointer value.
        /// </summary>
        /// <param name="handle">Handle to the loaded library.</param>
        /// <param name="name">The symbol to resolve.</param>
        /// <returns>Pointer to the resolved symbol.</returns>
        private static IntPtr GetSymbol(IntPtr handle, string name)
        {
            var symbolPtr = dlsym(handle, name);
            if (symbolPtr == IntPtr.Zero)
                throw new Exception($"Failed to resolve native symbol '{name}'");

            var value = Marshal.ReadIntPtr(symbolPtr);
            if (value == IntPtr.Zero)
                throw new Exception($"Native symbol '{name}' is null");

            return value;
        }

        /// <summary>
        /// Creates a CoreFoundation string from a managed string.
        /// </summary>
        /// <param name="s">The input string.</param>
        /// <returns>Pointer to the CFString.</returns>
        internal static IntPtr CFString(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s + "\0");
            var cf = CFStringCreateWithCString(IntPtr.Zero, bytes, kCFStringEncodingUTF8);
            if (cf == IntPtr.Zero)
                throw new Exception("CFStringCreateWithCString failed");
            return cf;
        }

        /// <summary>
        /// Creates CoreFoundation data from a byte array.
        /// </summary>
        /// <param name="bytes">The byte array.</param>
        /// <returns>Pointer to the CFData.</returns>
        internal static IntPtr CFData(byte[] bytes)
        {
            var cf = CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
            if (cf == IntPtr.Zero)
                throw new Exception("CFDataCreate failed");
            return cf;
        }

        /// <summary>
        /// Creates a new mutable CoreFoundation dictionary.
        /// </summary>
        /// <returns>Pointer to the dictionary.</returns>
        internal static IntPtr NewMutableDict()
        {
            var dict = CFDictionaryCreateMutable(
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                IntPtr.Zero);

            if (dict == IntPtr.Zero)
                throw new Exception("CFDictionaryCreateMutable failed");

            return dict;
        }

        /// <summary>
        /// Sets a value in a CoreFoundation dictionary.
        /// </summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        internal static void DictSet(IntPtr dict, IntPtr key, IntPtr value)
            => CFDictionarySetValue(dict, key, value);
    }

    /// <summary>
    /// Stores a string value in the keychain asynchronously.
    /// </summary>
    /// <param name="name">The name/key for the secret.</param>
    /// <param name="secret">The secret value to store.</param>
    /// <param name="overwrite">Whether to overwrite an existing item.</param>
    /// <param name="settings">The keychain settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static Task SetStringAsync(string name, string secret, bool overwrite, KeyChainSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SetItem(name, secret, overwrite, settings, isInternet: settings.Type == PasswordType.Internet);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the service name to use for the given name and settings.
    /// </summary>
    /// <param name="name">The name of the secret.</param>
    /// <param name="settings">The keychain settings.</param>
    /// <returns>The service name to use.</returns>
    private static string GetServiceName(string name, KeyChainSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Service))
            return name;
        return $"{settings.Service}.{name}";
    }

    /// <summary>
    /// Returns the account name to use for the given name and settings.
    /// </summary>
    /// <param name="name">The name of the secret.</param>
    /// <param name="settings">The keychain settings.</param>
    /// <returns>The account name to use.</returns>
    private static string GetAccountName(string name, KeyChainSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Account))
            return name;
        return settings.Account;
    }

    /// <summary>
    /// Stores a generic password in the keychain.
    /// </summary>
    /// <param name="label">The label for the item.</param>
    /// <param name="secret">The secret value.</param>
    /// <param name="overwrite">Whether to overwrite if exists.</param>
    /// <param name="settings">The keychain settings.</param>
    private static void SetItem(string label, string secret, bool overwrite, KeyChainSettings settings, bool isInternet)
    {
        var serviceOrServer = GetServiceName(label, settings);
        var account = GetAccountName(label, settings);

        IntPtr attrs = IntPtr.Zero, q = IntPtr.Zero, upd = IntPtr.Zero;
        IntPtr cfServiceOrServer = IntPtr.Zero, cfAccount = IntPtr.Zero, cfLabel = IntPtr.Zero, cfSecret = IntPtr.Zero;

        try
        {
            cfServiceOrServer = KeychainNative.CFString(serviceOrServer);
            cfAccount = KeychainNative.CFString(account);
            cfLabel = KeychainNative.CFString(label);
            cfSecret = KeychainNative.CFData(Encoding.UTF8.GetBytes(secret));

            // Build attributes for add
            attrs = KeychainNative.NewMutableDict();
            KeychainNative.DictSet(attrs, KeychainNative.SecClass,
                isInternet ? KeychainNative.SecClassInternetPassword
                           : KeychainNative.SecClassGenericPassword);

            if (isInternet)
                KeychainNative.DictSet(attrs, KeychainNative.SecAttrServer, cfServiceOrServer);
            else
                KeychainNative.DictSet(attrs, KeychainNative.SecAttrService, cfServiceOrServer);

            KeychainNative.DictSet(attrs, KeychainNative.SecAttrAccount, cfAccount);
            KeychainNative.DictSet(attrs, KeychainNative.SecAttrLabel, cfLabel);
            KeychainNative.DictSet(attrs, KeychainNative.SecValueData, cfSecret);

            var status = KeychainNative.SecItemAdd(attrs, out var added);
            if (added != IntPtr.Zero) KeychainNative.CFRelease(added);

            if (status == KeychainNative.errSecSuccess)
                return;

            if (status != KeychainNative.errSecDuplicateItem)
                throw new UserInformationException(
                    $"Failed to store secret in keychain (status {status})",
                    "KeyChainInsertFailed");

            // Duplicate item
            if (!overwrite)
                throw new UserInformationException(
                    $"Item already exists in keychain: {label}",
                    "KeyChainInsertFailed");

            // Query for the existing item to update (exact key)
            q = KeychainNative.NewMutableDict();
            KeychainNative.DictSet(q, KeychainNative.SecClass,
                isInternet ? KeychainNative.SecClassInternetPassword
                           : KeychainNative.SecClassGenericPassword);

            if (isInternet)
                KeychainNative.DictSet(q, KeychainNative.SecAttrServer, cfServiceOrServer);
            else
                KeychainNative.DictSet(q, KeychainNative.SecAttrService, cfServiceOrServer);

            KeychainNative.DictSet(q, KeychainNative.SecAttrAccount, cfAccount);

            // Update dictionary
            upd = KeychainNative.NewMutableDict();
            KeychainNative.DictSet(upd, KeychainNative.SecValueData, cfSecret);
            KeychainNative.DictSet(upd, KeychainNative.SecAttrLabel, cfLabel);

            status = KeychainNative.SecItemUpdate(q, upd);
            if (status != KeychainNative.errSecSuccess)
                throw new UserInformationException(
                    $"Failed to update secret in keychain (status {status})",
                    "KeyChainInsertFailed");
        }
        finally
        {
            if (attrs != IntPtr.Zero) KeychainNative.CFRelease(attrs);
            if (q != IntPtr.Zero) KeychainNative.CFRelease(q);
            if (upd != IntPtr.Zero) KeychainNative.CFRelease(upd);

            if (cfServiceOrServer != IntPtr.Zero) KeychainNative.CFRelease(cfServiceOrServer);
            if (cfAccount != IntPtr.Zero) KeychainNative.CFRelease(cfAccount);
            if (cfLabel != IntPtr.Zero) KeychainNative.CFRelease(cfLabel);
            if (cfSecret != IntPtr.Zero) KeychainNative.CFRelease(cfSecret);
        }
    }

    /// <summary>
    /// Retrieves a string value from the keychain asynchronously.
    /// </summary>
    /// <param name="name">The name/key of the secret.</param>
    /// <param name="settings">The keychain settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The retrieved secret value.</returns>
    private static Task<string> GetStringAsync(string name, KeyChainSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // If service/account is set (either one), try exact match first.
        if (!string.IsNullOrEmpty(settings.Service) || !string.IsNullOrEmpty(settings.Account))
        {
            try
            {
                return Task.FromResult(GetByLabelCore(name, settings, settings.Type == PasswordType.Internet));
            }
            catch (UserInformationException ex) when (ex.HelpID == "KeyChainItemMissing")
            {
                // Fallback to label-only below.
            }
        }

        return Task.FromResult(GetByLabelCore(name, null, settings.Type == PasswordType.Internet));
    }

    /// <summary>
    /// Core method to retrieve a password by label.
    /// </summary>
    /// <param name="label">The label of the item.</param>
    /// <param name="isInternet">Whether it's an internet password.</param>
    /// <returns>The password value.</returns>
    private static string GetByLabelCore(string name, KeyChainSettings? settings, bool isInternet)
    {
        IntPtr q = IntPtr.Zero;
        IntPtr cfLabel = IntPtr.Zero;
        IntPtr cfService = IntPtr.Zero;
        IntPtr cfAccount = IntPtr.Zero;
        IntPtr result = IntPtr.Zero;

        try
        {
            cfLabel = KeychainNative.CFString(name);

            q = KeychainNative.NewMutableDict();
            KeychainNative.DictSet(q, KeychainNative.SecClass,
                isInternet ? KeychainNative.SecClassInternetPassword
                           : KeychainNative.SecClassGenericPassword);

            // Always constrain by label/name
            KeychainNative.DictSet(q, KeychainNative.SecAttrLabel, cfLabel);

            // Optionally constrain by service + account as well
            if (settings != null && (!string.IsNullOrWhiteSpace(settings.Service) || !string.IsNullOrWhiteSpace(settings.Account)))
            {
                var service = GetServiceName(name, settings);
                var account = GetAccountName(name, settings);

                cfService = KeychainNative.CFString(service);
                cfAccount = KeychainNative.CFString(account);

                KeychainNative.DictSet(q, KeychainNative.SecAttrService, cfService);
                KeychainNative.DictSet(q, KeychainNative.SecAttrAccount, cfAccount);
            }

            KeychainNative.DictSet(q, KeychainNative.SecReturnData, KeychainNative.CFBooleanTrue);
            KeychainNative.DictSet(q, KeychainNative.SecMatchLimit, KeychainNative.SecMatchLimitOne);

            var status = KeychainNative.SecItemCopyMatching(q, out result);

            if (status != 0 || result == IntPtr.Zero)
            {
                var msg = settings != null
                    ? $"Item not found in keychain: label={name}, service={GetServiceName(name, settings)}, account={GetAccountName(name, settings)}"
                    : $"Item not found in keychain by label: {name}";

                throw new UserInformationException(msg, "KeyChainItemMissing");
            }

            var len = (int)KeychainNative.CFDataGetLength(result);
            if (len <= 0)
                throw new UserInformationException($"The key '{name}' returned an empty value", "KeyChainItemEmpty");

            var ptr = KeychainNative.CFDataGetBytePtr(result);
            var managed = new byte[len];
            Marshal.Copy(ptr, managed, 0, len);

            var output = Encoding.UTF8.GetString(managed).Trim();
            ValidateOutput(name, output);
            return output;
        }
        finally
        {
            if (result != IntPtr.Zero) KeychainNative.CFRelease(result);
            if (q != IntPtr.Zero) KeychainNative.CFRelease(q);
            if (cfLabel != IntPtr.Zero) KeychainNative.CFRelease(cfLabel);
            if (cfService != IntPtr.Zero) KeychainNative.CFRelease(cfService);
            if (cfAccount != IntPtr.Zero) KeychainNative.CFRelease(cfAccount);
        }
    }

    /// <summary>
    /// Validates the retrieved output value.
    /// </summary>
    /// <param name="name">The name/key for error messages.</param>
    /// <param name="output">The output string to validate.</param>
    private static void ValidateOutput(string name, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            throw new UserInformationException($"The key '{name}' returned an empty value", "KeyChainItemEmpty");

        if (output.IndexOfAny(['\r', '\n']) >= 0)
            throw new UserInformationException($"The key '{name}' returned a multi-line value", "KeyChainItemMultiLine");
    }
}