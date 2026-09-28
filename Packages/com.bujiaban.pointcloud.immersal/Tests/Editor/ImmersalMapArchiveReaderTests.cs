using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalMapArchiveReaderTests
    {
        [TestCase("map.byte")]
        [TestCase("map.bytes")]
        [TestCase("folder/MAP.BYTES")]
        public void ReadsExactlyOneMapAndIgnoresGlb(string mapEntryName)
        {
            using MemoryStream zip = CreateZip(
                (mapEntryName, new byte[] { 1, 2, 3 }),
                ("display.glb", new byte[] { 9, 9 }));

            ImmersalMapArchiveReader.MapData result =
                ImmersalMapArchiveReader.Read(zip, CancellationToken.None);

            Assert.That(result.Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(result.Name, Is.EqualTo("map").IgnoreCase);
            Assert.That(zip.CanRead, Is.True);
        }

        [Test]
        public void MissingMapIsRejected()
        {
            using MemoryStream zip = CreateZip(
                ("display.glb", new byte[] { 9 }));

            Assert.Throws<InvalidDataException>(() =>
                ImmersalMapArchiveReader.Read(zip, CancellationToken.None));
        }

        [Test]
        public void MultipleMapsAreRejected()
        {
            using MemoryStream zip = CreateZip(
                ("one.byte", new byte[] { 1 }),
                ("two.bytes", new byte[] { 2 }),
                ("display.glb", new byte[] { 9 }));

            Assert.Throws<InvalidDataException>(() =>
                ImmersalMapArchiveReader.Read(zip, CancellationToken.None));
        }

        [Test]
        public void AppleDoubleMapMetadataIsIgnored()
        {
            using MemoryStream zip = CreateZip(
                ("150733-Door.bytes", new byte[] { 1, 2, 3 }),
                ("__MACOSX/._150733-Door.bytes", new byte[] { 9 }),
                ("150733-Door-tex.glb", new byte[] { 4 }),
                ("__MACOSX/._150733-Door-tex.glb", new byte[] { 9 }));

            ImmersalMapArchiveReader.MapData result =
                ImmersalMapArchiveReader.Read(zip, CancellationToken.None);

            Assert.That(result.Name, Is.EqualTo("150733-Door"));
            Assert.That(result.Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void CancellationIsKeptSeparate()
        {
            using MemoryStream zip = CreateZip(
                ("map.bytes", Encoding.UTF8.GetBytes("map")));
            using CancellationTokenSource cancellation =
                new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                ImmersalMapArchiveReader.Read(zip, cancellation.Token));
        }

        private static MemoryStream CreateZip(
            params (string name, byte[] bytes)[] entries)
        {
            MemoryStream stream = new MemoryStream();
            using (ZipArchive zip = new ZipArchive(
                       stream,
                       ZipArchiveMode.Create,
                       leaveOpen: true))
            {
                foreach ((string name, byte[] bytes) in entries)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(name);
                    using Stream destination = entry.Open();
                    destination.Write(bytes, 0, bytes.Length);
                }
            }

            stream.Position = 0;
            return stream;
        }
    }
}
