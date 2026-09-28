using System;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.SharpZipLib.Zip;

namespace Bujiaban.PointCloud.Immersal
{
    internal static class ImmersalMapArchiveReader
    {
        private const int BufferSize = 81920;

        internal sealed class MapData
        {
            internal MapData(string name, byte[] bytes)
            {
                Name = name;
                Bytes = bytes;
            }

            internal string Name { get; }
            internal byte[] Bytes { get; private set; }

            internal void ClearBytes()
            {
                Bytes = null;
            }
        }

        internal static MapData Read(
            Stream mapZip,
            CancellationToken cancellationToken)
        {
            if (mapZip == null)
                throw new ArgumentNullException(nameof(mapZip));
            if (!mapZip.CanRead)
                throw new InvalidDataException("Map ZIP stream is not readable.");

            MapData result = default;
            int mapEntryCount = 0;

            using (ZipInputStream zip = new ZipInputStream(mapZip))
            {
                zip.IsStreamOwner = false;
                ZipEntry entry;
                while ((entry = zip.GetNextEntry()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.IsDirectory || !IsMapFile(entry.Name))
                        continue;

                    mapEntryCount++;
                    if (mapEntryCount > 1)
                    {
                        throw new InvalidDataException(
                            "Immersal map ZIP must contain exactly one .byte or .bytes file.");
                    }

                    using (MemoryStream mapBytes = new MemoryStream())
                    {
                        CopyTo(zip, mapBytes, cancellationToken);
                        result = new MapData(
                            Path.GetFileNameWithoutExtension(entry.Name),
                            mapBytes.ToArray());
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (mapEntryCount == 0)
            {
                throw new InvalidDataException(
                    "Immersal map ZIP does not contain a .byte or .bytes file.");
            }

            if (result?.Bytes == null || result.Bytes.Length == 0)
                throw new InvalidDataException("Immersal map file is empty.");

            return result;
        }

        private static bool IsMapFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || IsAppleMetadata(name))
                return false;

            string extension = Path.GetExtension(name);
            return string.Equals(
                       extension,
                       ".byte",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       extension,
                       ".bytes",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAppleMetadata(string name)
        {
            string[] segments = name.Replace('\\', '/').Split('/');
            return segments.Any(segment =>
                string.Equals(segment, "__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                segment.StartsWith("._", StringComparison.Ordinal));
        }

        private static void CopyTo(
            Stream source,
            Stream destination,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[BufferSize];
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, count);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
