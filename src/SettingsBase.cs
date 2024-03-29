﻿using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Diagnostics;
using OwlCore.Storage;

namespace OwlCore.ComponentModel
{
    /// <summary>
    /// A base class for getting and setting Settings values as properties. Fast access in memory, with data persistence in a file system.
    /// </summary>
    public abstract class SettingsBase : INotifyPropertyChanged
    {
        /// <summary>
        /// A constant value that represents the suffix of the type settings file.
        /// </summary>
        public const string TypeFileSuffix = ".Type";

        private readonly IAsyncSerializer<Stream> _settingSerializer;
        private readonly SemaphoreSlim _storageSemaphore = new(1, 1);
        private readonly ConcurrentDictionary<string, SettingValue> _runtimeStorage = new();

        /// <summary>
        /// Creates a new instance of <see cref="SettingsBase"/>.
        /// </summary>
        /// <param name="folder">The folder where settings are stored.</param>
        /// <param name="settingSerializer">The serializer used to serialize and deserialize settings to and from disk.</param>
        protected SettingsBase(IModifiableFolder folder, IAsyncSerializer<Stream> settingSerializer)
        {
            _settingSerializer = settingSerializer;
            Folder = folder;
        }

        /// <summary>
        /// Gets or sets the property that determines whether to flush default values to disk.
        /// Setting to false is recommended if your default values never change.
        /// </summary>
        protected bool FlushDefaultValues { get; set; } = true;

        /// <summary>
        /// Gets or sets a value that determines whether to flush settings that are unchanged in memory.
        /// Setting to true is recommended if you don't expect others to modify the settings files.
        /// </summary>
        protected bool FlushOnlyChangedValues { get; set; }

        /// <summary>
        /// Gets a value indicating whether changes have been made to properties which have not yet been saved to disk.
        /// </summary>
        public bool HasUnsavedChanges => _runtimeStorage.Values.Any(x => x.IsDirty);

        /// <summary>
        /// A folder abstraction where the settings are stored and persisted.
        /// </summary>
        public IModifiableFolder Folder { get; }

        /// <summary>
        /// Stores a settings value.
        /// </summary>
        /// <param name="value">The value to store.</param>
        /// <param name="key">A unique identifier for this setting.</param>
        /// <typeparam name="T">The type of the stored value.</typeparam>
        protected virtual void SetSetting<T>(T value, [CallerMemberName] string key = "")
        {
            var hadUnsavedChanges = HasUnsavedChanges;

            if (value is null)
            {
                _runtimeStorage.TryRemove(key, out _);
            }
            else
            {
                if (_runtimeStorage.ContainsKey(key))
                {
                    _runtimeStorage[key].Data = value;
                    _runtimeStorage[key].IsDirty = true;
                }
                else
                {
                    _runtimeStorage[key] = new(typeof(T), value, IsDirty: true);
                }
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));

            if (!hadUnsavedChanges && HasUnsavedChanges)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnsavedChanges)));
        }

        /// <summary>
        /// Gets a settings value.
        /// </summary>
        /// <param name="defaultValue">A <see cref="Func{TResult}"/> that returns a fallback value to use when the setting is retrieved but was no value was ever stored.</param>
        /// <param name="key">A unique identifier for this setting.</param>
        /// <typeparam name="T">The type of the stored value.</typeparam>
        protected virtual T GetSetting<T>(Func<T> defaultValue, [CallerMemberName] string key = "")
        {
            if (_runtimeStorage.TryGetValue(key, out var value))
                return (T)value.Data;

            var fallbackValue = defaultValue();

            // Null values are never stored in runtime or persistent storage.
            if (fallbackValue is not null)
                _runtimeStorage[key] = new(typeof(T), fallbackValue, FlushDefaultValues);

            return fallbackValue;
        }

        /// <summary>
        /// Sets a settings value to its default.
        /// </summary>
        /// <param name="key">A unique identifier for the setting.</param>
        public virtual void ResetSetting(string key)
        {
            var hadUnsavedChanges = HasUnsavedChanges;

            _runtimeStorage.TryRemove(key, out _);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));

            if ((!hadUnsavedChanges && HasUnsavedChanges) || (hadUnsavedChanges && !HasUnsavedChanges))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnsavedChanges)));
        }

        /// <summary>
        /// Sets all settings values to their default.
        /// </summary>
        public virtual void ResetAllSettings()
        {
            foreach (string key in _runtimeStorage.Keys)
                ResetSetting(key);
        }

        /// <summary>
        /// Persists all settings from memory onto disk.
        /// </summary>
        /// <remarks>
        /// If any exceptions are thrown while saving a setting, the exception will be swallowed and emitted via <see cref="SaveFailed"/>, and the setting that failed will be excluded from being persisted.
        /// </remarks>
        public virtual async Task SaveAsync(CancellationToken? cancellationToken = null)
        {
            var token = cancellationToken ?? CancellationToken.None;

            await _storageSemaphore.WaitAsync(token);

            foreach (var kvp in _runtimeStorage.ToArray())
            {
                var hadUnsavedChanges = HasUnsavedChanges;

                // Keeping storage of metadata (e.g. original type) separate from actual data allows us to
                // pass the file stream to the serializer directly, without loading the whole thing into memory
                // for modification. 
                // This allows the serializer to load as little or as much data into memory as it needs at a time.
                try
                {
                    // Don't save settings whose value didn't change
                    if (FlushOnlyChangedValues && !kvp.Value.IsDirty)
                        continue;

                    var dataFile = await Folder.CreateFileAsync(kvp.Key, cancellationToken: token);
                    var typeFile = await Folder.CreateFileAsync($"{kvp.Key}{TypeFileSuffix}", cancellationToken: token);

                    if (token.IsCancellationRequested)
                        return;

                    using var serializedRawDataStream = await _settingSerializer.SerializeAsync(kvp.Value.Type, kvp.Value.Data, token);

                    if (token.IsCancellationRequested)
                        return;

                    using var dataFileStream = await dataFile.OpenStreamAsync(FileAccess.ReadWrite, token);

                    if (token.IsCancellationRequested)
                        return;

                    dataFileStream.SetLength(serializedRawDataStream.Length);

                    dataFileStream.Seek(0, SeekOrigin.Begin);
                    serializedRawDataStream.Seek(0, SeekOrigin.Begin);

                    await serializedRawDataStream.CopyToAsync(dataFileStream, bufferSize: 81920, token);

                    if (token.IsCancellationRequested)
                        return;

                    Guard.IsNotNullOrWhiteSpace(kvp.Value.Type.FullName);

                    // Store the known type for later deserialization. Serializer cannot be relied on for this.
                    var typeContentBytes = Encoding.UTF8.GetBytes(kvp.Value.Type.AssemblyQualifiedName ?? kvp.Value.Type.FullName);
                    using var typeFileStream = await typeFile.OpenStreamAsync(FileAccess.ReadWrite, token);
                    typeFileStream.Seek(0, SeekOrigin.Begin);
                    typeFileStream.SetLength(typeContentBytes.Length);

                    await typeFileStream.WriteAsync(typeContentBytes, 0, typeContentBytes.Length, token);

                    // Setting saved, set IsDirty to false
                    kvp.Value.IsDirty = false;

                    if ((!hadUnsavedChanges && HasUnsavedChanges) || (hadUnsavedChanges && !HasUnsavedChanges))
                        OnPropertyChanged(nameof(HasUnsavedChanges));
                }
                catch (Exception ex)
                {
                    // Ignore any errors when saving, writing or serializing.
                    // Setting will not be saved.
                    SaveFailed?.Invoke(this, new SettingPersistFailedEventArgs(kvp.Key, ex));
                }
            };

            _storageSemaphore.Release();
        }

        /// <summary>
        /// Loads all settings from disk into memory.
        /// </summary>
        /// <remarks>
        /// If any exceptions are thrown while loading a setting, the exception will be swallowed and emitted via <see cref="LoadFailed"/>, and the current value in memory will be untouched.
        /// </remarks>
        public virtual async Task LoadAsync(CancellationToken? cancellationToken = null)
        {
            var token = cancellationToken ?? CancellationToken.None;

            await _storageSemaphore.WaitAsync(token);

            var files = await Folder.GetFilesAsync(cancellationToken: token).ToListAsync(cancellationToken: token);

            // Remove unpersisted values.
            var unpersistedSettings = _runtimeStorage.Where(x => files.All(y => y.Name != x.Key)).ToArray();
            foreach (var setting in unpersistedSettings)
                _runtimeStorage.TryRemove(setting.Key, out _);

            // Filter out non Type files, so only raw data files remain.
            var nonTypeFiles = files.Where(x => !x.Name.Contains(TypeFileSuffix));

            // Load persisted values.
            foreach (var settingDataFile in nonTypeFiles)
            {
                var typeFile = files.FirstOrDefault(x => x.Name == $"{settingDataFile.Name}{TypeFileSuffix}");
                if (typeFile is null)
                    continue; // Type file may be missing or deleted.

                var hadUnsavedChanges = HasUnsavedChanges;

                try
                {
                    using var settingDataStream = await settingDataFile.OpenReadAsync(cancellationToken: token);
                    settingDataStream.Position = 0;

                    var typeFileContentString = await ReadFileAsStringAsync(typeFile, token);
                    if (string.IsNullOrWhiteSpace(typeFileContentString))
                        continue;

                    var originalType = Type.GetType(typeFileContentString);
                    if (originalType is null)
                        continue;

                    // Deserialize data as original type.
                    var settingData = await _settingSerializer.DeserializeAsync(originalType, settingDataStream, token);

                    _runtimeStorage[settingDataFile.Name] = new(originalType, settingData, false); // Data doesn't need to be saved
                    
                    OnPropertyChanged(settingDataFile.Name);

                    if ((!hadUnsavedChanges && HasUnsavedChanges) || (hadUnsavedChanges && !HasUnsavedChanges))
                        OnPropertyChanged(nameof(HasUnsavedChanges));
                }
                catch (Exception ex)
                {
                    // Ignore any errors when loading, reading or deserializing
                    // Setting will not be loaded into memory.
                    LoadFailed?.Invoke(this, new SettingPersistFailedEventArgs(settingDataFile.Name, ex));
                }
            }

            _storageSemaphore.Release();
        }

        private static async Task<string> ReadFileAsStringAsync(IFile file, CancellationToken token)
        {
            // Load file
            using var typeFileStream = await file.OpenReadAsync(cancellationToken: token);
            typeFileStream.Seek(0, SeekOrigin.Begin);

            // Convert to raw bytes
            var typeFileBytes = new byte[typeFileStream.Length];
            var read = await typeFileStream.ReadAsync(typeFileBytes, 0, typeFileBytes.Length, token);

            // Check amount read
            Guard.HasSizeEqualTo(typeFileBytes, read);

            // Read bytes as string
            return Encoding.UTF8.GetString(typeFileBytes);
        }

        /// <inheritdoc />
        public virtual event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raised when an exception is thrown during <see cref="LoadAsync(CancellationToken?)"/>.
        /// </summary>
        public virtual event EventHandler<SettingPersistFailedEventArgs>? LoadFailed;

        /// <summary>
        /// Raised when an exception is thrown during <see cref="SaveAsync(CancellationToken?)"/>.
        /// </summary>
        public virtual event EventHandler<SettingPersistFailedEventArgs>? SaveFailed;

        /// <summary>
        /// Raises the PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The name of the property to raise the event for.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// A wrapper that holds settings data.
        /// </summary>
        /// <param name="Type">The type of setting.</param>
        /// <param name="Data">The value of the setting.</param>
        /// <param name="IsDirty">Determines whether the <paramref name="Data"/> was modified or not.</param>
        public record SettingValue(Type Type, object Data, bool IsDirty)
        {
            /// <summary>
            /// Gets or sets the value of the setting.
            /// </summary>
            public object Data { get; set; } = Data;

            /// <summary>
            /// Gets or sets the value that determines whether the <see cref="Data"/> was modified or not.
            /// </summary>
            public bool IsDirty { get; set; } = IsDirty;
        }
    }

    /// <summary>
    /// Event arguments about a failed persistent save or load in <see cref="SettingsBase"/>.
    /// </summary>
    public class SettingPersistFailedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance of <see cref="SettingPersistFailedEventArgs"/>.
        /// </summary>
        public SettingPersistFailedEventArgs(string settingName, Exception exception)
        {
            SettingName = settingName;
            Exception = exception;
        }

        /// <summary>
        /// The exception that was raised when attempting to save or load a setting.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// The setting which failed to persist.
        /// </summary>
        public string SettingName { get; }
    }
}