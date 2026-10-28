using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RegistradorHoras
{
    // Usa o SQLite que já vem dentro do Windows 10/11 (winsqlite3.dll),
    // assim o programa continua sendo um único .exe, sem DLL para acompanhar.
    static class Native
    {
        const string Lib = "winsqlite3.dll";
        const CallingConvention Cc = CallingConvention.Cdecl;

        [DllImport(Lib, CallingConvention = Cc, CharSet = CharSet.Unicode)]
        public static extern int sqlite3_open16(string arquivo, out IntPtr db);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_close(IntPtr db);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern IntPtr sqlite3_errmsg16(IntPtr db);
        [DllImport(Lib, CallingConvention = Cc, CharSet = CharSet.Unicode)]
        public static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int nBytes, out IntPtr stmt, IntPtr tail);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_bind_null(IntPtr stmt, int i);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_bind_int64(IntPtr stmt, int i, long valor);
        [DllImport(Lib, CallingConvention = Cc, CharSet = CharSet.Unicode)]
        public static extern int sqlite3_bind_text16(IntPtr stmt, int i, string valor, int nBytes, IntPtr destrutor);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_column_type(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern double sqlite3_column_double(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern IntPtr sqlite3_column_text16(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern long sqlite3_last_insert_rowid(IntPtr db);
        [DllImport(Lib, CallingConvention = Cc)]
        public static extern int sqlite3_changes(IntPtr db);
    }

    // Camada mínima sobre o SQLite: Exec (comandos), Query (consultas) e Transacao.
    // Valores voltam como long, double, string ou null.
    class Db : IDisposable
    {
        const int Linha = 100, Pronto = 101;
        static readonly IntPtr Transiente = new IntPtr(-1); // SQLITE_TRANSIENT: o SQLite copia o texto
        readonly object trava = new object();
        IntPtr h;

        public Db(string arquivo)
        {
            if (Native.sqlite3_open16(arquivo, out h) != 0)
                throw new Exception("Não foi possível abrir o banco de dados: " + arquivo);
            Native.sqlite3_busy_timeout(h, 5000);
            Exec("PRAGMA foreign_keys = ON");
        }

        Exception Erro(string sql)
        {
            return new Exception("Erro no banco de dados: " + Marshal.PtrToStringUni(Native.sqlite3_errmsg16(h)) + "\n" + sql);
        }

        IntPtr Preparar(string sql, object[] args)
        {
            if (args == null) args = new object[] { null };
            IntPtr st;
            if (Native.sqlite3_prepare16_v2(h, sql, -1, out st, IntPtr.Zero) != 0) throw Erro(sql);
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    object a = args[i];
                    int rc;
                    if (a == null) rc = Native.sqlite3_bind_null(st, i + 1);
                    else if (a is long) rc = Native.sqlite3_bind_int64(st, i + 1, (long)a);
                    else if (a is int) rc = Native.sqlite3_bind_int64(st, i + 1, (int)a);
                    else if (a is DateTime) rc = Native.sqlite3_bind_text16(st, i + 1, Store.F((DateTime)a), -1, Transiente);
                    else rc = Native.sqlite3_bind_text16(st, i + 1, Convert.ToString(a, CultureInfo.InvariantCulture), -1, Transiente);
                    if (rc != 0) throw Erro(sql);
                }
            }
            catch
            {
                Native.sqlite3_finalize(st);
                throw;
            }
            return st;
        }

        public int Exec(string sql, params object[] args)
        {
            lock (trava)
            {
                IntPtr st = Preparar(sql, args);
                try
                {
                    int rc;
                    while ((rc = Native.sqlite3_step(st)) == Linha) { }
                    if (rc != Pronto) throw Erro(sql);
                    return Native.sqlite3_changes(h);
                }
                finally { Native.sqlite3_finalize(st); }
            }
        }

        public List<object[]> Query(string sql, params object[] args)
        {
            lock (trava)
            {
                IntPtr st = Preparar(sql, args);
                try
                {
                    var res = new List<object[]>();
                    int n = Native.sqlite3_column_count(st);
                    int rc;
                    while ((rc = Native.sqlite3_step(st)) == Linha)
                    {
                        var row = new object[n];
                        for (int i = 0; i < n; i++)
                        {
                            switch (Native.sqlite3_column_type(st, i))
                            {
                                case 1: row[i] = Native.sqlite3_column_int64(st, i); break;
                                case 2: row[i] = Native.sqlite3_column_double(st, i); break;
                                case 3: row[i] = Marshal.PtrToStringUni(Native.sqlite3_column_text16(st, i)); break;
                                default: row[i] = null; break;
                            }
                        }
                        res.Add(row);
                    }
                    if (rc != Pronto) throw Erro(sql);
                    return res;
                }
                finally { Native.sqlite3_finalize(st); }
            }
        }

        public long LastId
        {
            get { lock (trava) { return Native.sqlite3_last_insert_rowid(h); } }
        }

        public void Transacao(Action acao)
        {
            lock (trava)
            {
                Exec("BEGIN IMMEDIATE");
                try
                {
                    acao();
                    Exec("COMMIT");
                }
                catch
                {
                    try { Exec("ROLLBACK"); } catch { }
                    throw;
                }
            }
        }

        public void Dispose()
        {
            lock (trava)
            {
                if (h != IntPtr.Zero) { Native.sqlite3_close(h); h = IntPtr.Zero; }
            }
        }
    }
}
