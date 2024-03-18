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

```cs
var test = new Thing();
```

## Financing

We accept donations [here](https://github.com/sponsors/Arlodotexe) and [here](https://www.patreon.com/arlodotexe), and we do not have any active bug bounties.

## Versioning

Version numbering follows the Semantic versioning approach. However, if the major version is `0`, the code is considered alpha and breaking changes may occur as a minor update.

## License

All OwlCore code is licensed under the MIT License. OwlCore is licensed under the MIT License. See the [LICENSE](./src/LICENSE.txt) file for more details.
