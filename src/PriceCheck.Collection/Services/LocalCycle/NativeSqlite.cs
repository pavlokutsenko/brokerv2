using System.Runtime.InteropServices;
using System.Text;

namespace PriceCheck.Collector.Services;

// Windows' inbox SQLite avoids a runtime/package installation dependency.
internal sealed class NativeSqlite : IDisposable
{
    private IntPtr _db;
    public NativeSqlite(string path)
    {
        var result = Open(Utf8(path), out _db, 2 | 4 | 0x10000, IntPtr.Zero);
        if (result != 0) throw Error(result);
        BusyTimeout(_db, 5000);
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;");
    }
    public void Execute(string sql)
    {
        var result = Exec(_db, Utf8(sql), IntPtr.Zero, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) Free(error);
        if (result != 0) throw Error(result);
    }
    public void Command(string sql, params object?[] values) => Query(sql, values);
    public List<string?[]> Query(string sql, params object?[] values)
    {
        var result = Prepare(_db, Utf8(sql), -1, out var statement, IntPtr.Zero);
        if (result != 0) throw Error(result);
        try
        {
            for (var index = 0; index < values.Length; index++)
            {
                result = values[index] switch
                {
                    null => BindNull(statement, index + 1),
                    int value => BindInt64(statement, index + 1, value),
                    long value => BindInt64(statement, index + 1, value),
                    double value => BindDouble(statement, index + 1, value),
                    bool value => BindInt64(statement, index + 1, value ? 1 : 0),
                    _ => BindText(statement, index + 1, Utf8(Convert.ToString(values[index], System.Globalization.CultureInfo.InvariantCulture)!), -1, new IntPtr(-1))
                };
                if (result != 0) throw Error(result);
            }
            var rows = new List<string?[]>();
            while ((result = Step(statement)) == 100)
            {
                var row = new string?[ColumnCount(statement)];
                for (var index = 0; index < row.Length; index++)
                    row[index] = Marshal.PtrToStringUTF8(ColumnText(statement, index));
                rows.Add(row);
            }
            if (result != 101) throw Error(result);
            return rows;
        }
        finally { Finalize(statement); }
    }
    public void Transaction(Action action)
    {
        Execute("BEGIN IMMEDIATE");
        try { action(); Execute("COMMIT"); }
        catch { Execute("ROLLBACK"); throw; }
    }
    private InvalidDataException Error(int code) => new($"SQLite {code}: {Marshal.PtrToStringUTF8(Errmsg(_db))}");
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + '\0');
    public void Dispose() { if (_db != IntPtr.Zero) { Close(_db); _db = IntPtr.Zero; } }
    [DllImport("winsqlite3", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)] private static extern int Open(byte[] path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_close_v2", CallingConvention = CallingConvention.Cdecl)] private static extern int Close(IntPtr db);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)] private static extern int Exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr context, out IntPtr error);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_free", CallingConvention = CallingConvention.Cdecl)] private static extern void Free(IntPtr value);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_errmsg", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Errmsg(IntPtr db);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_busy_timeout", CallingConvention = CallingConvention.Cdecl)] private static extern int BusyTimeout(IntPtr db, int ms);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)] private static extern int Prepare(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)] private static extern int Step(IntPtr statement);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)] private static extern int Finalize(IntPtr statement);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_bind_text", CallingConvention = CallingConvention.Cdecl)] private static extern int BindText(IntPtr statement, int index, byte[] text, int bytes, IntPtr destructor);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_bind_int64", CallingConvention = CallingConvention.Cdecl)] private static extern int BindInt64(IntPtr statement, int index, long value);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_bind_double", CallingConvention = CallingConvention.Cdecl)] private static extern int BindDouble(IntPtr statement, int index, double value);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_bind_null", CallingConvention = CallingConvention.Cdecl)] private static extern int BindNull(IntPtr statement, int index);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_column_count", CallingConvention = CallingConvention.Cdecl)] private static extern int ColumnCount(IntPtr statement);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ColumnText(IntPtr statement, int index);
}

