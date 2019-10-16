using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace W.Files
{
    public interface IReadFiles
    {
        bool FileExists(string fileName);
        IEnumerable<string> EnumerateFiles(string directory);
        Stream OpenReadFile(string fileName);
        void RewriteFile(string fileName, byte[] data);
    }

    public interface IWriteFiles
    {
        Task<Stream> CreateFile(string fileName, CancellationToken ct);
        Task CopyFile(Stream source, string fileName, CancellationToken ct, bool skipIfExists = true);
        Task Done(CancellationToken ct);
    }

    public struct CellInfo
    {
        public string name;
        public string type;
        public object value;
    }

    public struct RowInfo
    {
        public int num;
        public CellInfo[] cells;
    }

    public interface ITabReader
    {
        string SheetName { get; }
        IEnumerable<RowInfo> EnumerateRows();
    }

    public interface ITabbedFileReader : IDisposable
    {
        string Filename { get; }
        IEnumerable<ITabReader> EnumerateTabs();
    }

}
