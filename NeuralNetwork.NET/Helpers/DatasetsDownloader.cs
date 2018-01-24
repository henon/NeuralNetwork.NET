﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using JetBrains.Annotations;

namespace NeuralNetworkNET.Helpers
{
    /// <summary>
    /// A static class that handles in-app resources downloaded from the web
    /// </summary>
    internal static class DatasetsDownloader
    {
        #region Fields and properties

        // The default file extension for local resource files
        private const String FileExtension = ".bin";

        /// <summary>
        /// Gets the default datasets path to use to store and load fdata files
        /// </summary>
        [NotNull]
        private static String DatasetsPath
        {
            get
            {
                String
                    code = Assembly.GetExecutingAssembly().Location,
                    dll = Path.GetFullPath(code),
                    root = Path.GetDirectoryName(dll),
                    path = Path.Combine(root, "Datasets");
                return path;
            }
        }

        // Local lazy instance of the singleton HttpClient in use
        [NotNull]
        private static readonly Lazy<HttpClient> _Client = new Lazy<HttpClient>(() => new HttpClient());

        /// <summary>
        /// Gets the singleton <see cref="HttpClient"/> to use, since it is reentrant and thread-safe, see <a href="https://docs.microsoft.com/it-it/dotnet/api/system.net.http.httpclient">docs.microsoft.com/it-it/dotnet/api/system.net.http.httpclient</a>
        /// </summary>
        [NotNull]
        private static HttpClient Client
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                lock (_Client)
                    return _Client.Value;
            }
        }

        #endregion

        #region APIs

        /// <summary>
        /// Gets a <see cref="Func{TResult}"/> instance returning a <see cref="Stream"/> with the contents of the input URL
        /// </summary>
        /// <param name="url">The target URL to use to download the resources</param>
        /// <param name="token">A cancellation token for the operation</param>
        [MustUseReturnValue, ItemCanBeNull]
        public static async Task<Func<Stream>> GetFileAsync([NotNull] String url, CancellationToken token)
        {
            // Get the target filename
            String
                filename = $"{GetFilename(url)}{FileExtension}",
                path = Path.Combine(DatasetsPath, filename);
            Directory.CreateDirectory(DatasetsPath);

            // Check if the target resource already exists
            if (!File.Exists(path))
            {
                try
                {
                    // Download from the input URL
                    HttpResponseMessage result = await Client.GetAsync(url, token);
                    if (!result.IsSuccessStatusCode || token.IsCancellationRequested) return null;
                    byte[] data = await result.Content.ReadAsByteArrayAsync();

                    // Write the HTTP content
                    using (FileStream stream = File.OpenWrite(path))
                        await stream.WriteAsync(data, 0, data.Length, default); // Ensure the whole content is written to disk
                }
                catch
                {
                    // Connection error or operation canceled by the user
                    return null;
                }
            }
            return () => File.OpenRead(path);
        }

        /// <summary>
        /// Gets an <see cref="IDictionary{TKey,TValue}"/> with a collection of <see cref="Func{TResult}"/> instances for each file in the tar.gz archive pointed by the input URL
        /// </summary>
        /// <param name="url">The target URL to use to download the archive</param>
        /// <param name="token">A cancellation token for the operation</param>
        [MustUseReturnValue, ItemCanBeNull]
        public static async Task<IReadOnlyDictionary<String, Func<Stream>>> GetArchiveAsync([NotNull] String url, CancellationToken token)
        {
            // Check if the archive is already present
            String folder = Path.Combine(DatasetsPath, GetFilename(url));
            if (!Directory.Exists(folder))
            {
                {
                    try
                    {
                        // Download from the input URL
                        HttpResponseMessage result = await Client.GetAsync(url, token);
                        if (!result.IsSuccessStatusCode || token.IsCancellationRequested) return null;
                        
                        // Extract the .tar.gz archive
                        using (Stream stream = await result.Content.ReadAsStreamAsync())
                        using (GZipInputStream gzip = new GZipInputStream(stream))
                        using (TarArchive tar = TarArchive.CreateInputTarArchive(gzip))
                        {
                            // Extract into the target dir (this will create a subfolder in this position)
                            Directory.CreateDirectory(folder);
                            tar.ExtractContents(folder);

                            // Move all the contents in the root directory
                            foreach (String path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                                File.Move(path, Path.Combine(folder, Path.GetFileName(path)));

                            // Delete the subfolders
                            foreach (String subdir in Directory.GetDirectories(folder))
                                Directory.Delete(subdir);
                        }
                    }
                    catch
                    {
                        // Connection error or operation canceled by the user
                        return null;
                    }
                }
            }

            // Parse the files
            return Directory.EnumerateFiles(folder).ToDictionary<String, String, Func<Stream>>(Path.GetFileName, file => () => File.OpenRead(file));
        }

        #endregion

        #region Tools

        /// <summary>
        /// Gets a unique filename from the input URL
        /// </summary>
        /// <param name="url">The URL to convert to filename</param>
        [Pure, NotNull]
        private static String GetFilename([NotNull] String url)
        {
            using (MD5 md5 = MD5.Create())
            {
                // Hash and compress the url
                byte[]
                    bytes = Encoding.UTF8.GetBytes(url),
                    hash = md5.ComputeHash(bytes),
                    reduced = Enumerable.Range(0, hash.Length / 2).Select(i => (byte)(hash[i] * 23 + hash[i + 1])).ToArray(); // Shorten by half

                // To base16
                return reduced.Aggregate(new StringBuilder(), (builder, b) =>
                {
                    builder.Append($"{b:x2}");
                    return builder;
                }).ToString();
            }
        }

        #endregion
    }
}
