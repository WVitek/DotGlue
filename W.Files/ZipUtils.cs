using System;
using System.Collections.Generic;
using ICSharpCode.SharpZipLib.Zip;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace W.Files
{
    public class FilesFromZip : IReadFiles, IDisposable
    {
        const char cZipSeparator = '/';

        private ZipFile zip;

        public FilesFromZip(ZipFile zip)
        { this.zip = zip; }

        Dictionary<string, byte[]> rewrittenFiles = new Dictionary<string, byte[]>();

        public static string GetZipEntryName(string fileName) { return fileName.Replace(Path.DirectorySeparatorChar, cZipSeparator); }
        public static string GetFileName(string zipEntryName) { return zipEntryName.Replace(cZipSeparator, Path.DirectorySeparatorChar); }

        static bool EntryInDir(string entryName, string dirName)
        {
            int dirL = dirName.Length;
            if (dirL >= entryName.Length)
                return false;
            if (!entryName.StartsWith(dirName))
                return false;
            if (entryName.IndexOf(cZipSeparator, dirL) >= 0)
                return false;
            return true;
        }

        IEnumerable<string> EnumerateAllFiles()
        {
            IEnumerable<string> filesInZip;
            lock (this)
            {
                filesInZip = zip.OfType<ZipEntry>()
                    .Where(entry =>
                    {
                        byte[] data;
                        if (rewrittenFiles.TryGetValue(entry.Name, out data) && data == null)
                            return false;
                        return true;
                    })
                    .Select(e => e.Name);

                var filesRewritten = rewrittenFiles.Where(p => p.Value != null).Select(p => p.Key);

                var files = filesInZip.Concat(filesRewritten).Distinct();
                return files;
            }
        }

        #region IReadFiles implementation
        public bool FileExists(string fileName)
        {
            var entry = zip.GetEntry(GetZipEntryName(fileName));
            return entry != null;
        }

        public IEnumerable<string> EnumerateFiles(string directory)
        {
            var files = EnumerateAllFiles();
            if (directory != null)
            {
                var dirEntryName = GetZipEntryName(directory) + cZipSeparator;
                files = files.Where(entryName => EntryInDir(entryName, dirEntryName));
            }
            return files.Select(s => GetFileName(s));
        }

        public Stream OpenReadFile(string fileName)
        {
            var entryName = GetZipEntryName(fileName);
            byte[] data;
            lock (this)
            {
                if (rewrittenFiles.TryGetValue(entryName, out data))
                {
                    if (data == null)
                        return null;
                    return new MemoryStream(data, false);
                }
                var entry = zip.GetEntry(entryName);
                var stream = zip.GetInputStream(entry);
                if ((entryName.EndsWith(".xlsx") || entryName.EndsWith(".xlsm")) && !stream.CanSeek)
                {
                    var ms = new MemoryStream((int)entry.Size);
                    stream.CopyTo(ms);
                    data = ms.GetBuffer();
                    rewrittenFiles.Add(entryName, data);
                    stream = new MemoryStream(data, false);
                }
                return stream;
            }
        }

        public void RewriteFile(string fileName, byte[] data)
        {
            var entryName = GetZipEntryName(fileName);
            lock (this)
                rewrittenFiles[entryName] = data;
        }
        #endregion

        public override string ToString()
        {
            return Path.GetFileName(zip.Name) + ':' + Path.DirectorySeparatorChar;
        }

        public void Dispose()
        {
            zip.Close();
        }
    }

    public class FilesInDir : IReadFiles, IWriteFiles
    {
        readonly string dir;
        public FilesInDir(string dir) { this.dir = Path.GetFullPath(dir); }

        string Combine(string path) { return Path.Combine(dir, path); }
        #region IReadFiles
        public bool FileExists(string fileName) { return File.Exists(Combine(fileName)); }
        public IEnumerable<string> EnumerateFiles(string directory)
        {
            return Directory
                .EnumerateFiles(Combine(directory))
                .Select(s => s.StartsWith(dir, StringComparison.InvariantCultureIgnoreCase) ? s.Substring(dir.Length + 1) : s)
            ;
        }
        public Stream OpenReadFile(string fileName) { return new FileStream(Combine(fileName), FileMode.Open, FileAccess.Read); }
        public void RewriteFile(string fileName, byte[] data) { File.WriteAllBytes(Combine(fileName), data); }
        #endregion

        #region IWriteFiles
        public async Task<Stream> CreateFile(string fileName, CancellationToken ct)
        {
            await W.Common.Utils.TaskFromResult(string.Empty);
            var path = Combine(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return new FileStream(path, FileMode.Create, FileAccess.Write);
        }

        public async Task CopyFile(Stream source, string fileName, CancellationToken ct, bool skipIfExists = true)
        {
            var path = Combine(fileName);
            if (skipIfExists && File.Exists(path))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                await source.CopyToAsync(dst);
        }

        public Task Done(CancellationToken ct)
        {
            // do nothing
            return W.Common.Utils.TaskFromResult(string.Empty);
        }
        #endregion

        public override string ToString()
        {
            return dir + Path.DirectorySeparatorChar;
        }
    }

    public class FilesToZip : IWriteFiles
    {
        const char cZipSeparator = '/';

        W.Common.IAsyncLock onlyLock = W.Common.Utils.NewAsyncLock();
        readonly ZipOutputStream zos;
        Dictionary<string, bool> files = new Dictionary<string, bool>();

        public static string GetZipEntryName(string fileName) { return fileName.Replace(Path.DirectorySeparatorChar, cZipSeparator); }

        public FilesToZip(Stream output, int bufferSize)
        { zos = new ZipOutputStream(output, bufferSize); }

        async Task<ZipEntry> NewEntry(string fileName, CancellationToken ct, bool throwIfExists = false)
        {
            await onlyLock.WaitAsync(ct);
            if (files.ContainsKey(fileName))
            {
                onlyLock.Release();
                if (throwIfExists)
                    throw new System.IO.IOException("File already exists");
                return null;
            }
            var entry = new ZipEntry(GetZipEntryName(fileName));
            files.Add(fileName, true);
            return entry;
        }

        public async Task CopyFile(Stream source, string fileName, CancellationToken ct, bool skipIfExists = true)
        {
            var entry = await NewEntry(fileName, ct, !skipIfExists);
            if (entry == null)
                return;
            zos.PutNextEntry(entry);
            using (var dest = new StreamWithOnClose(zos, OnEntryStreamClose))
                await source.CopyToAsync(dest, 1 << 17, ct);
        }

        public async Task<Stream> CreateFile(string fileName, CancellationToken ct)
        {
            var entry = await NewEntry(fileName, ct, true);
            zos.PutNextEntry(entry);
            return new StreamWithOnClose(zos, OnEntryStreamClose);
        }

        void OnEntryStreamClose()
        {
            zos.CloseEntry();
            onlyLock.Release();
        }

        public async Task Done(CancellationToken ct)
        {
            await onlyLock.WaitAsync(ct);
            zos.Close();
            onlyLock.Release();
        }
    }

    public class SubDirReader : IReadFiles
    {
        IReadFiles dir;
        string subDir;
        string Combine(string path) { return Path.Combine(subDir, path); }
        public SubDirReader(IReadFiles dir, string subDir) { this.dir = dir; this.subDir = subDir; }

        #region IReadFiles implementation
        public IEnumerable<string> EnumerateFiles(string directory) { return dir.EnumerateFiles(Combine(directory)); }
        public bool FileExists(string fileName) { return dir.FileExists(Combine(fileName)); }
        public Stream OpenReadFile(string fileName) { return dir.OpenReadFile(Combine(fileName)); }
        public void RewriteFile(string fileName, byte[] data) { dir.RewriteFile(Combine(fileName), data); }
        #endregion

        public override string ToString() { return string.Join(Path.DirectorySeparatorChar.ToString(), dir, subDir, string.Empty); }
    }

    public class SubDirWriter : IWriteFiles
    {
        IWriteFiles dir;
        string subDir;
        string Combine(string path) { return Path.Combine(subDir, path); }
        public SubDirWriter(IWriteFiles dir, string subDir) { this.dir = dir; this.subDir = subDir; }

        public Task CopyFile(Stream source, string fileName, CancellationToken ct, bool skipIfExists = true)
        { return dir.CopyFile(source, Combine(fileName), ct, skipIfExists); }

        public Task<Stream> CreateFile(string fileName, CancellationToken ct)
        { return dir.CreateFile(Combine(fileName), ct); }

        public Task Done(CancellationToken ct)
        { return dir.Done(ct); }
    }

    public class StreamWithOnClose : Stream
    {
        readonly Stream stream;
        readonly Action onClose;
        readonly bool isStreamOwner;

        public StreamWithOnClose(Stream stream, Action onClose, bool isStreamOwner = false)
        { this.stream = stream; this.onClose = onClose; this.isStreamOwner = isStreamOwner; }

        public override bool CanRead { get { return stream.CanRead; } }
        public override bool CanSeek { get { return stream.CanSeek; } }
        public override bool CanWrite { get { return stream.CanWrite; } }
        public override long Length { get { return stream.Length; } }
        public override long Position { get { return stream.Position; } set { stream.Position = value; } }
        public override void Flush() { stream.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) { return stream.Read(buffer, offset, count); }
        public override long Seek(long offset, SeekOrigin origin) { return stream.Seek(offset, origin); }
        public override void SetLength(long value) { stream.SetLength(value); }
        public override void Write(byte[] buffer, int offset, int count) { stream.Write(buffer, offset, count); }
        public override void Close()
        {
            if (isStreamOwner)
                stream.Close();
            onClose();
        }
    }
}