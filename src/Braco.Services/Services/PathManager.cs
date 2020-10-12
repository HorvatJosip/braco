﻿using Braco.Services.Abstractions;
using Braco.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Braco.Services
{
	/// <summary>
	/// Default implementation of <see cref="IPathManager"/>.
	/// </summary>
    public class PathManager : IPathManager
    {
        private readonly Dictionary<string, DirectoryInfo> _directories = new Dictionary<string, DirectoryInfo>();
        private readonly Dictionary<string, FileInfo> _files = new Dictionary<string, FileInfo>();

		/// <inheritdoc/>
        public DirectoryInfo AppDirectory => _directories[IPathManager.AppDirectoryKey];

		/// <inheritdoc/>
		public DirectoryInfo AppDataDirectory => _directories[IPathManager.AppDataDirectoryKey];

		/// <summary>
		/// Creates an instance for managing paths.
		/// </summary>
		/// <param name="appName">Name of the application. If left null,
		/// calling assembly's name will be used.</param>
        public PathManager(string appName = null)
        {
            var ass = Assembly.GetCallingAssembly();

            appName ??= ass.GetName().Name;

            _directories[IPathManager.AppDirectoryKey] = new DirectoryInfo(Path.GetDirectoryName(ass.Location));

            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _directories[IPathManager.AppDataDirectoryKey] = new DirectoryInfo(Path.Combine(appDataPath, appName));
        }

		/// <inheritdoc/>
		public FileInfo GetFile(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            _files.TryGetValue(key, out var fileInfo);

            return fileInfo;
        }

		/// <inheritdoc/>
		public DirectoryInfo GetDirectory(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            _directories.TryGetValue(key, out var directoryInfo);

            return directoryInfo;
        }

		/// <inheritdoc/>
		public FileInfo AddFileToDirectory(string key, DirectoryInfo directory, string fileName, bool removeInvalidChars = true)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));

            if (!removeInvalidChars && PathUtilities.FileNameContainsInvalidChars(fileName)) return null;

            fileName = PathUtilities.GetFileNameWithoutInvalidChars(fileName);

            return AddFile(key, Path.Combine(directory.FullName, fileName), removeInvalidChars);
        }

		/// <inheritdoc/>
		public FileInfo AddFile(string key, string path, bool removeInvalidChars = true)
        {
            var result = Add(_files, key, path, removeInvalidChars) as FileInfo;

            if (result != null)
            {
                if (!result.Directory.Exists)
                    result.Directory.Create();

                if (!result.Exists)
                    result.Create().Close();
            }

            return result;
        }

		/// <inheritdoc/>
		public DirectoryInfo AddDirectory(string key, string path, bool removeInvalidChars = true)
        {
            var result = Add(_directories, key, path, removeInvalidChars) as DirectoryInfo;

            if (result?.Exists == false)
                result.Create();

            return result;
        }

        private object Add(IDictionary dictionary, string key, string path, bool removeInvalidChars = true)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (path == null) throw new ArgumentNullException(nameof(path));

            if (dictionary.Contains(key)) return dictionary[key];

            if (!removeInvalidChars && PathUtilities.PathContainsInvalidChars(path)) return null;

            var dicValueType = dictionary.GetType().GetGenericArguments()[1];

            var result = Activator.CreateInstance(dicValueType, PathUtilities.GetPathWithoutInvalidChars(path));
            dictionary[key] = result;

            return result;
        }
    }
}
