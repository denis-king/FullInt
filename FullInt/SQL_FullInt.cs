using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.Server;
using System.Data.SqlTypes;
//using FullIntCore = FullInt.Core;


//public class SQL_FullInt
//    {

//        #region Процудуры
//        [SqlProcedure]
//        public static void add(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Text));
//            result.SetSqlString(0, (String)((FullIntCore)value1.ToString() + (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void sub(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Text));
//            result.SetSqlString(0, (String)((FullInt)value1.ToString() - (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void mult(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Text));
//            result.SetSqlString(0, (String)((FullInt)value1.ToString() * (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void div(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Text));
//            result.SetSqlString(0, (String)((FullInt)value1.ToString() / (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void mod(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Text));
//            result.SetSqlString(0, (String)((FullInt)value1.ToString() % (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void pow(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Text));
//            result.SetSqlString(0, (String)((FullInt)value1.ToString() ^ (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void jb(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Bit));
//            result.SetBoolean(0, ((FullInt)value1.ToString() > (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void jbe(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Bit));
//            result.SetBoolean(0, ((FullInt)value1.ToString() >= (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void jl(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Bit));
//            result.SetBoolean(0, ((FullInt)value1.ToString() < (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void jle(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Bit));
//            result.SetBoolean(0, ((FullInt)value1.ToString() <= (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void je(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Bit));
//            result.SetBoolean(0, ((FullInt)value1.ToString() == (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        [SqlProcedure]
//        public static void jne(SqlString value1, SqlString value2)
//        {
//            SqlDataRecord result = new SqlDataRecord(new SqlMetaData("Result", System.Data.SqlDbType.Bit));
//            result.SetBoolean(0, ((FullInt)value1.ToString() != (FullInt)value2.ToString()));
//            SqlContext.Pipe.Send(result);
//        }

//        #endregion

//        #region Функции
//        [SqlFunction]
//        public static SqlString F_add(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() + (FullInt)value2.ToString()).ToString();
//        }

//        [SqlFunction]
//        public static SqlString F_sub(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() - (FullInt)value2.ToString()).ToString();
//        }

//        [SqlFunction]
//        public static SqlString F_mult(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() * (FullInt)value2.ToString()).ToString();
//        }

//        [SqlFunction]
//        public static SqlString F_div(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() / (FullInt)value2.ToString()).ToString();
//        }

//        [SqlFunction]
//        public static SqlString F_mod(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() % (FullInt)value2.ToString()).ToString();
//        }

//        [SqlFunction]
//        public static SqlString F_pow(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() ^ (FullInt)value2.ToString()).ToString();
//        }

//        [SqlFunction]
//        public static bool F_jb(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() > (FullInt)value2.ToString());
//        }

//        [SqlFunction]
//        public static bool F_jbe(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() >= (FullInt)value2.ToString());
//        }

//        [SqlFunction]
//        public static bool F_jl(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() < (FullInt)value2.ToString());
//        }

//        [SqlFunction]
//        public static bool F_jle(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() <= (FullInt)value2.ToString());
//        }

//        [SqlFunction]
//        public static bool F_je(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() == (FullInt)value2.ToString());
//        }

//        [SqlFunction]
//        public static bool F_jne(SqlString value1, SqlString value2)
//        {
//            return ((FullInt)value1.ToString() != (FullInt)value2.ToString());
//        }

//        #endregion
//    }

