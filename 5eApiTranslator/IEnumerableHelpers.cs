using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator
{
    public static class IEnumerableHelpers
    {
        #region ToDataTable Overloads
        public static DataTable ToDataTable<T>(this IEnumerable<T> list) where T : class
        {
            DataTable dt = new DataTable();

            foreach (PropertyInfo prop in typeof(T).GetProperties())
            {
                dt.Columns.Add(prop.Name, prop.PropertyType);
            }

            foreach (object o in list)
            {
                var newRow = dt.NewRow();

                foreach (DataColumn column in dt.Columns)
                {
                    newRow[column.ColumnName] = typeof(T).GetProperty(column.ColumnName).GetValue(o);
                }

                dt.Rows.Add(newRow);
            }

            return dt;
        }

        public static DataTable ToDataTable(this IEnumerable<int> list, string columnName)
        {
            DataTable dt = new DataTable();

            dt.Columns.Add(columnName);

            foreach (int i in list)
            {
                var newRow = dt.NewRow();
                newRow[columnName] = i;
                dt.Rows.Add(newRow);
            }

            return dt;
        }

        public static DataTable ToDataTable(this IEnumerable<double> list, string columnName)
        {
            DataTable dt = new DataTable();

            dt.Columns.Add(columnName);

            foreach (double d in list)
            {
                var newRow = dt.NewRow();
                newRow[columnName] = d;
                dt.Rows.Add(newRow);
            }

            return dt;
        }

        public static DataTable ToDataTable(this IEnumerable<decimal> list, string columnName)
        {
            DataTable dt = new DataTable();

            dt.Columns.Add(columnName);

            foreach (decimal d in list)
            {
                var newRow = dt.NewRow();
                newRow[columnName] = d;
                dt.Rows.Add(newRow);
            }

            return dt;
        }

        public static DataTable ToDataTable(this IEnumerable<string> list, string columnName)
        {
            DataTable dt = new DataTable();

            dt.Columns.Add(columnName);

            foreach (string s in list)
            {
                var newRow = dt.NewRow();
                newRow[columnName] = s.Trim();
                dt.Rows.Add(newRow);
            }

            return dt;
        }
        #endregion ToDataTable Overloads
    }
}
