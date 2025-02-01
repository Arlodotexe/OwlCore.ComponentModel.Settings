# OwlCore.ComponentModel.Settings [![Version](https://img.shields.io/nuget/v/OwlCore.ComponentModel.Settings.svg)](https://www.nuget.org/packages/OwlCore.ComponentModel.Settings)

Components and models for handling settings within your application

## Featuring:

- **Fast Access in Memory**: Settings values are stored in memory for quick access.
- **Data Persistence**: Settings values are persisted in a storage abstraction, ensuring data is not lost between sessions.
- **Customizable Storage and Serialization**: The constructor of `SettingsBase` allows you to specify the folder where settings are stored and the serializer used to serialize and deserialize settings to and from disk.
- **Settings Reset**: The `ResetAllSettings` method allows you to reset all settings values to their default.
- **Property Change Notification**: `SettingsBase` implements `INotifyPropertyChanged`, allowing it to notify subscribers when a property changes.

## Install

Published releases are available on [NuGet](https://www.nuget.org/packages/OwlCore.ComponentModel.Settings). To install, run the following command in the [Package Manager Console](https://docs.nuget.org/docs/start-here/using-the-package-manager-console).

    PM> Install-Package OwlCore.ComponentModel.Settings
    
Or using [dotnet](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet)

    > dotnet add package OwlCore.ComponentModel.Settings

## Usage

### SettingsBase

This example uses source generators from System.Text.Json for serialization.

```cs
// Basic usage. Can be any OwlCore.Storage.IModifiableFolder, such as MemoryFolder, WindowsStorageFolder, ArchiveFolder, MfsFolder, etc.
var settingsPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var settings = new MySettings(new SystemFolder(settingsPath));

await settings.LoadAsync();

if (settings.HasSeenOOBE)
    settings.Interval = TimeSpan.FromSeconds(0);

await settings.SaveAsync();

// Implementation
public class MySettings : SettingsBase
{
    public MySettings(IModifiableFolder settingsFolder)
        : base(settingsFolder, FilesCoreSettingsSerializer.Singleton)
    {
    }
    
    public bool HasSeenOOBE
    {
        get => GetSetting(() => false);
        set => SetSetting(value);
    }
    
    public TimeSpan Interval
    {
        get => GetSetting(() => TimeSpan.FromSeconds(5));
        set => SetSetting(value);
    }
}

// Source-generator based serializer
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(TimeSpan))]
internal partial class MySettingsSerializerContext : JsonSerializerContext
{
}

public class MySettingsSerializer : IAsyncSerializer<Stream>
{
    public static MySettingsSerializer Singleton { get; } = new();
    
    /// <inheritdoc />
    public async Task<Stream> SerializeAsync<T>(T data, CancellationToken? cancellationToken = null)
    {
        var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, data, typeof(T), context: MySettingsSerializerContext.Default, cancellationToken: cancellationToken ?? CancellationToken.None);
        return stream;
    }

    /// <inheritdoc />
    public async Task<Stream> SerializeAsync(Type inputType, object data, CancellationToken? cancellationToken = null)
    {
        var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, data, inputType, context: MySettingsSerializerContext.Default, cancellationToken: cancellationToken ?? CancellationToken.None);
        return stream;
    }

    /// <inheritdoc />
    public async Task<TResult> DeserializeAsync<TResult>(Stream serialized, CancellationToken? cancellationToken = null)
    {
        var result = await JsonSerializer.DeserializeAsync(serialized, typeof(TResult), MySettingsSerializerContext.Default);
        Guard.IsNotNull(result);
        return (TResult)result;
    }

    /// <inheritdoc />
    public async Task<object> DeserializeAsync(Type returnType, Stream serialized, CancellationToken? cancellationToken = null)
    {
        var result = await JsonSerializer.DeserializeAsync(serialized, returnType, MySettingsSerializerContext.Default);
        Guard.IsNotNull(result);
        return result;
    }
}
```

## Financing

We accept donations [here](https://github.com/sponsors/Arlodotexe) and [here](https://www.patreon.com/arlodotexe), and we do not have any active bug bounties.

## Versioning

Version numbering follows the Semantic versioning approach. However, if the major version is `0`, the code is considered alpha and breaking changes may occur as a minor update.

## License

All OwlCore code is licensed under the MIT License. OwlCore is licensed under the MIT License. See the [LICENSE](./src/LICENSE.txt) file for more details.
