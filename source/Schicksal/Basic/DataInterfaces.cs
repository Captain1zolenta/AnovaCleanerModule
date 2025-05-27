using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using Schicksal.Properties;

namespace Schicksal.Basic
{
  public interface IDataTable
  {
    int RowCount { get; }
    IEnumerable<object> Columns { get; }
    object this[int row, string column] { get; set; }
    void NewRow();
    void AddRows(object row);
  }

  public interface ITableImport
  {
    IDataTable Import(string filePath);
  }

  public interface ITableExport
  {
    void Export(IDataTable table, string filePath);
  }
}
