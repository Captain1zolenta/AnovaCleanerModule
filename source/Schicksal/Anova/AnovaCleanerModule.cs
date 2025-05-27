using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Schicksal.Basic;

namespace Schicksal.Anova
{
  /// <summary>
  /// Модуль для очистки данных факторного эксперимента из файла .sks, восстанавливающий полную факторную структуру.
  /// </summary>
  public class AnovaCleanerModule
  {
    private readonly ILogger m_logger;
    private static Random random = new Random();

    /// <summary>
    /// Конструктор модуля очистки.
    /// </summary>
    /// <param name="logger">Логгер для записи информации и ошибок. Если null, используется SimpleLogger.</param>
    public AnovaCleanerModule(ILogger logger = null)
    {
      this.m_logger = logger ?? new SimpleLogger();
    }

    /// <summary>
    /// Очищает таблицу в формате .sks, удаляя минимальное количество строк для восстановления полной факторной структуры.
    /// </summary>
    /// <param name="inputPath">Путь к входному файлу .sks.</param>
    /// <param name="factorColumns">Имена столбцов факторов (категориальных переменных).</param>
    /// <param name="resultColumn">Имя столбца результата (числовой переменной).</param>
    public void CleanTable(string inputPath, string[] factorColumns, string resultColumn)
    {
      if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
      {
        Console.WriteLine("Error: Invalid input file path.");
        return;
      }
      if (factorColumns == null || factorColumns.Length < 2)
      {
        Console.WriteLine("Error: At least two factor columns are required.");
        return;
      }
      if (string.IsNullOrEmpty(resultColumn))
      {
        Console.WriteLine("Error: Result column name is required.");
        return;
      }

      this.m_logger.Info($"Starting table cleaning: {inputPath}");

      IDataTable table = this.LoadTable(inputPath);
      this.ValidateTable(table, factorColumns, resultColumn);

      var sampleRows = this.ConvertToSampleRows(table, factorColumns, resultColumn);
      var validRows = sampleRows.Where(r => !double.IsNaN(r.ResultValue)).ToList();
      int originalCount = table.Rows.Count;
      int filteredCount = validRows.Count;
      var removedRows = this.GetRemovedRows(sampleRows, validRows);

      double lossPercentage = (double)(originalCount - filteredCount) / originalCount * 100;

      Console.WriteLine("ANOVA Table Cleaning Report");
      Console.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
      Console.WriteLine($"Original row count: {originalCount}");
      Console.WriteLine($"Filtered row count: {filteredCount}");
      Console.WriteLine($"Removed rows: {removedRows.Count} ({lossPercentage:F2}%)");
      Console.WriteLine("Removed row indices:");
      foreach (var index in removedRows)
        Console.WriteLine(index);

      Console.WriteLine("\nCleaned Table:");
      this.SaveCleanedTable(validRows, factorColumns, resultColumn);

      this.m_logger.Info($"Table cleaning completed. Removed {removedRows.Count} rows ({lossPercentage:F2}%).");
    }

    /// <summary>
    /// Генерирует случайную таблицу и очищает её, сохраняя результат в файл .sks.
    /// </summary>
    /// <param name="outputPath">Путь к выходному файлу .sks.</param>
    /// <param name="sizeCategory">Категория размера таблицы (1, 2 или 3).</param>
    /// <exception cref="ArgumentException">Выбрасывается, если sizeCategory не находится в диапазоне 1–3.</exception>
    public void GenerateAndCleanTable(string outputPath, int sizeCategory)
    {
      int columnCount, maxValuesPerColumn;
      switch (sizeCategory)
      {
        case 1:
          columnCount = AnovaCleanerModule.random.Next(2, 4);
          maxValuesPerColumn = AnovaCleanerModule.random.Next(3, 7);
          break;
        case 2:
          columnCount = AnovaCleanerModule.random.Next(3, 5);
          maxValuesPerColumn = AnovaCleanerModule.random.Next(6, 11);
          break;
        case 3:
          columnCount = AnovaCleanerModule.random.Next(2, 5);
          maxValuesPerColumn = Math.Min(12, AnovaCleanerModule.random.Next(8, 13));
          break;
        default:
          throw new ArgumentException("sizeCategory должно быть 1, 2 или 3");
      }

      var factorColumns = Enumerable.Range(0, columnCount).Select(i => $"Factor{i + 1}").ToArray();
      var datasets = this.GenerateRandomTable(sizeCategory, columnCount, maxValuesPerColumn);
      var fullDataset = datasets[0];
      var reducedDataset = datasets[1];

      this.SaveDatasetToFile(fullDataset, outputPath, factorColumns, "Result");
      this.CleanTable(outputPath, factorColumns, "Result");
    }

    /// <summary>
    /// Загружает таблицу из файла .sks.
    /// </summary>
    /// <param name="filePath">Путь к файлу .sks.</param>
    /// <returns>Объект IDataTable с данными таблицы.</returns>
    /// <exception cref="InvalidOperationException">Выбрасывается, если файл пуст или имеет некорректный формат.</exception>
    private IDataTable LoadTable(string filePath)
    {
      var importer = new SksTableImporter();
      return importer.Import(filePath);
    }

    /// <summary>
    /// Проверяет корректность таблицы и наличие указанных столбцов.
    /// </summary>
    /// <param name="table">Таблица данных.</param>
    /// <param name="factorColumns">Имена столбцов факторов.</param>
    /// <param name="resultColumn">Имя столбца результата.</param>
    /// <exception cref="ArgumentException">Выбрасывается, если указанные столбцы отсутствуют в таблице.</exception>
    private void ValidateTable(IDataTable table, string[] factorColumns, string resultColumn)
    {
      foreach (var col in factorColumns.Concat(new[] { resultColumn }))
      {
        if (!table.Columns.Contains(col))
          throw new ArgumentException($"Column {col} not found in table");
      }
    }

    /// <summary>
    /// Преобразует таблицу в список объектов SampleRow для дальнейшей обработки.
    /// </summary>
    /// <param name="table">Исходная таблица.</param>
    /// <param name="factorColumns">Имена столбцов факторов.</param>
    /// <param name="resultColumn">Имя столбца результата.</param>
    /// <returns>Список объектов SampleRow с данными из таблицы.</returns>
    private List<SampleRow> ConvertToSampleRows(IDataTable table, string[] factorColumns, string resultColumn)
    {
      var sampleRows = new List<SampleRow>();
      for (int i = 0; i < table.Rows.Count; i++)
      {
        var factorValues = factorColumns.Select(col => table.Rows[i][col]).ToArray();
        var resultValueStr = table.Rows[i][resultColumn]?.ToString() ?? "NaN";
        if (double.TryParse(resultValueStr, out double resultValue))
        {
          sampleRows.Add(new SampleRow(i, factorValues, resultValue));
        }
        else
        {
          sampleRows.Add(new SampleRow(i, factorValues, double.NaN));
        }
      }
      return sampleRows;
    }

    /// <summary>
    /// Определяет индексы строк, удаленных в процессе очистки.
    /// </summary>
    /// <param name="original">Исходный список строк.</param>
    /// <param name="filtered">Отфильтрованный список строк.</param>
    /// <returns>Список индексов удаленных строк.</returns>
    private List<int> GetRemovedRows(List<SampleRow> original, List<SampleRow> filtered)
    {
      var originalIndices = original.Select(row => row.Index).ToList();
      var filteredIndices = new HashSet<int>(filtered.Select(row => row.Index));
      return originalIndices.Where(idx => !filteredIndices.Contains(idx)).ToList();
    }

    /// <summary>
    /// Выводит очищенную таблицу в консоль.
    /// </summary>
    /// <param name="filteredSample">Список очищенных строк.</param>
    /// <param name="factorColumns">Имена столбцов факторов.</param>
    /// <param name="resultColumn">Имя столбца результата.</param>
    private void SaveCleanedTable(List<SampleRow> filteredSample, string[] factorColumns, string resultColumn)
    {
      Console.WriteLine("\nCleaned Table (Detailed):");
      foreach (var row in filteredSample)
      {
        Console.WriteLine($"{string.Join("\t", row.FactorValues)}\t{row.ResultValue}");
      }
    }

    /// <summary>
    /// Генерирует случайную таблицу с заданными параметрами.
    /// </summary>
    /// <param name="sizeCategory">Категория размера таблицы (1, 2 или 3).</param>
    /// <param name="columnCount">Количество столбцов факторов.</param>
    /// <param name="maxValuesPerColumn">Максимальное количество уникальных значений в столбце.</param>
    /// <returns>Список из двух наборов данных: полная таблица и таблица с удаленными строками.</returns>
    /// <exception cref="ArgumentException">Выбрасывается, если sizeCategory не находится в диапазоне 1–3.</exception>
    private List<List<TupleWrapperInt>> GenerateRandomTable(int sizeCategory, int columnCount, int maxValuesPerColumn)
    {
      int rowsToRemove;
      switch (sizeCategory)
      {
        case 1:
          columnCount = AnovaCleanerModule.random.Next(2, 4);
          maxValuesPerColumn = AnovaCleanerModule.random.Next(3, 7);
          rowsToRemove = AnovaCleanerModule.random.Next(Math.Max(0, (int)(this.CartesianProductCount(columnCount, maxValuesPerColumn) * 0.2) - 1), (int)(this.CartesianProductCount(columnCount, maxValuesPerColumn) * 0.5));
          break;
        case 2:
          columnCount = AnovaCleanerModule.random.Next(3, 5);
          maxValuesPerColumn = AnovaCleanerModule.random.Next(6, 11);
          rowsToRemove = AnovaCleanerModule.random.Next(Math.Max(0, (int)(this.CartesianProductCount(columnCount, maxValuesPerColumn) * 0.2) - 1), (int)(this.CartesianProductCount(columnCount, maxValuesPerColumn) * 0.5));
          break;
        case 3:
          columnCount = AnovaCleanerModule.random.Next(2, 5);
          maxValuesPerColumn = Math.Min(12, AnovaCleanerModule.random.Next(8, 13));
          rowsToRemove = AnovaCleanerModule.random.Next(Math.Max(0, (int)(this.CartesianProductCount(columnCount, maxValuesPerColumn) * 0.2) - 1), (int)(this.CartesianProductCount(columnCount, maxValuesPerColumn) * 0.5));
          break;
        default:
          throw new ArgumentException("sizeCategory должно быть 1, 2 или 3");
      }

      List<HashSet<int>> columnValues = Enumerable.Range(0, columnCount)
          .Select(i => this.GenerateRandomSet(AnovaCleanerModule.random, maxValuesPerColumn)).ToList();
      List<TupleWrapperInt> cartesianProduct = this.CartesianProduct(columnValues);
      var indicesToRemove = Enumerable.Range(0, cartesianProduct.Count)
                                     .OrderBy(x => AnovaCleanerModule.random.Next())
                                     .Take(rowsToRemove)
                                     .ToList();
      List<TupleWrapperInt> reducedDataset = cartesianProduct.Where((x, i) => !indicesToRemove.Contains(i)).ToList();

      var fullDatasetWithResults = this.AddRandomResults(cartesianProduct);
      var reducedDatasetWithResults = this.AddRandomResults(reducedDataset);

      return new List<List<TupleWrapperInt>> { fullDatasetWithResults, reducedDatasetWithResults };
    }

    /// <summary>
    /// Добавляет случайные результаты к набору данных.
    /// </summary>
    /// <param name="dataset">Набор данных для обработки.</param>
    /// <returns>Обновленный набор данных с добавленными результатами.</returns>
    private List<TupleWrapperInt> AddRandomResults(List<TupleWrapperInt> dataset)
    {
      return dataset.Select(t => new TupleWrapperInt(t.Values.Concat(new[] { (int)(AnovaCleanerModule.random.NextDouble() * 100) }).ToArray())).ToList();
    }

    /// <summary>
    /// Генерирует случайный набор уникальных значений для столбца.
    /// </summary>
    /// <param name="random">Генератор случайных чисел.</param>
    /// <param name="maxValues">Максимальное количество уникальных значений.</param>
    /// <returns>Множество случайных значений.</returns>
    private HashSet<int> GenerateRandomSet(Random random, int maxValues)
    {
      int count = random.Next(1, maxValues + 1);
      return new HashSet<int>(Enumerable.Range(0, count).Select(_ => random.Next(1, 101)));
    }

    /// <summary>
    /// Вычисляет декартово произведение множества наборов значений.
    /// </summary>
    /// <param name="sets">Список множеств значений для каждого столбца.</param>
    /// <returns>Список кортежей, представляющих все возможные комбинации значений.</returns>
    private List<TupleWrapperInt> CartesianProduct(List<HashSet<int>> sets)
    {
      if (sets == null || sets.Count == 0) return new List<TupleWrapperInt>();
      List<TupleWrapperInt> resultTuples = new List<TupleWrapperInt>();
      this.CartesianProductRecursive(sets, 0, new List<int>(), resultTuples);
      return resultTuples;
    }

    /// <summary>
    /// Рекурсивно вычисляет декартово произведение множества наборов значений.
    /// </summary>
    /// <param name="sets">Список множеств значений для каждого столбца.</param>
    /// <param name="index">Текущий индекс столбца.</param>
    /// <param name="current">Текущая комбинация значений.</param>
    /// <param name="result">Список для хранения результатов.</param>
    private void CartesianProductRecursive(List<HashSet<int>> sets, int index, List<int> current, List<TupleWrapperInt> result)
    {
      if (index == sets.Count)
      {
        result.Add(new TupleWrapperInt(current.ToArray()));
        return;
      }
      foreach (var value in sets[index])
      {
        current.Add(value);
        this.CartesianProductRecursive(sets, index + 1, current, result);
        current.RemoveAt(current.Count - 1);
      }
    }

    /// <summary>
    /// Сохраняет сгенерированный набор данных в файл .sks.
    /// </summary>
    /// <param name="dataset">Набор данных для сохранения.</param>
    /// <param name="filePath">Путь к выходному файлу .sks.</param>
    /// <param name="factorColumns">Имена столбцов факторов.</param>
    /// <param name="resultColumn">Имя столбца результата.</param>
    private void SaveDatasetToFile(List<TupleWrapperInt> dataset, string filePath, string[] factorColumns, string resultColumn)
    {
      using (StreamWriter writer = new StreamWriter(filePath))
      {
        writer.WriteLine(string.Join("\t", factorColumns.Concat(new[] { resultColumn })));
        foreach (var tuple in dataset)
        {
          // Преобразуем int в string перед передачей в string.Join
          var factorValues = tuple.Values.Take(tuple.Values.Length - 1).Select(x => x.ToString());
          var resultValue = tuple.Values.Last().ToString();
          writer.WriteLine($"{string.Join("\t", factorValues)}\t{resultValue}");
        }
      }
    }

    /// <summary>
    /// Вычисляет общее количество комбинаций для декартова произведения.
    /// </summary>
    /// <param name="columnCount">Количество столбцов.</param>
    /// <param name="maxValuesPerColumn">Максимальное количество значений в каждом столбце.</param>
    /// <returns>Общее количество возможных комбинаций.</returns>
    private long CartesianProductCount(int columnCount, int maxValuesPerColumn)
    {
      long result = 1;
      for (int i = 0; i < columnCount; i++)
      {
        result *= maxValuesPerColumn;
      }
      return result;
    }
  }

  /// <summary>
  /// Класс для представления строки данных с факторами и результатом.
  /// </summary>
  internal class SampleRow
  {
    /// <summary>
    /// Индекс строки в исходной таблице.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Значения факторов строки.
    /// </summary>
    public object[] FactorValues { get; }

    /// <summary>
    /// Значение результата строки.
    /// </summary>
    public double ResultValue { get; }

    /// <summary>
    /// Конструктор класса SampleRow.
    /// </summary>
    /// <param name="index">Индекс строки в исходной таблице.</param>
    /// <param name="factorValues">Значения факторов строки.</param>
    /// <param name="resultValue">Значение результата строки.</param>
    public SampleRow(int index, object[] factorValues, double resultValue)
    {
      Index = index;
      FactorValues = factorValues;
      ResultValue = resultValue;
    }
  }

  /// <summary>
  /// Импортер для чтения файлов .sks.
  /// </summary>
  internal class SksTableImporter : ITableImport
  {
    /// <summary>
    /// Импортирует таблицу из файла .sks.
    /// </summary>
    /// <param name="filePath">Путь к файлу .sks.</param>
    /// <returns>Объект IDataTable с данными таблицы.</returns>
    /// <exception cref="InvalidOperationException">Выбрасывается, если файл пуст или имеет некорректный формат.</exception>
    public IDataTable Import(string filePath)
    {
      var lines = File.ReadAllLines(filePath);
      if (lines.Length == 0)
        throw new InvalidOperationException("Empty file");

      var headers = lines[0].Split('\t');
      var table = new DataTable(headers);

      for (int i = 1; i < lines.Length; i++)
      {
        var values = lines[i].Split('\t');
        if (values.Length != headers.Length)
          throw new InvalidOperationException($"Invalid row format at line {i + 1}");

        var row = table.NewRow();
        for (int j = 0; j < headers.Length; j++)
          row[j] = values[j];
        table.Rows.Add(row);
      }

      return table;
    }
  }

  /// <summary>
  /// Экспортер для сохранения таблиц в формат .sks (пустая реализация).
  /// </summary>
  internal class SksTableExporter : ITableExport
  {
    /// <summary>
    /// Экспортирует таблицу в файл .sks (пустая реализация).
    /// </summary>
    /// <param name="table">Таблица для экспорта.</param>
    /// <param name="filePath">Путь к выходному файлу.</param>
    public void Export(IDataTable table, string filePath)
    {
    }
  }

  /// <summary>
  /// Простая реализация логгера для записи сообщений в консоль.
  /// </summary>
  internal class SimpleLogger : ILogger
  {
    /// <summary>
    /// Записывает информационное сообщение в консоль.
    /// </summary>
    /// <param name="message">Сообщение для записи.</param>
    public void Info(string message)
    {
      Console.WriteLine($"[INFO] {message}");
    }

    /// <summary>
    /// Записывает сообщение об ошибке в консоль.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public void Error(string message)
    {
      Console.WriteLine($"[ERROR] {message}");
    }
  }

  /// <summary>
  /// Класс для представления кортежа целых чисел с поддержкой сравнения.
  /// </summary>
  internal class TupleWrapperInt : IEquatable<TupleWrapperInt>
  {
    /// <summary>
    /// Значения кортежа.
    /// </summary>
    public int[] Values { get; }

    /// <summary>
    /// Конструктор класса TupleWrapperInt.
    /// </summary>
    /// <param name="values">Массив значений кортежа.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если values равен null.</exception>
    public TupleWrapperInt(int[] values)
    {
      Values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>
    /// Проверяет равенство двух кортежей.
    /// </summary>
    /// <param name="other">Другой кортеж для сравнения.</param>
    /// <returns>True, если кортежи равны, иначе false.</returns>
    public bool Equals(TupleWrapperInt other)
    {
      if (ReferenceEquals(this, other)) return true;
      if (other == null) return false;
      if (Values.Length != other.Values.Length) return false;
      return !Values.Where((t, i) => t != other.Values[i]).Any();
    }

    /// <summary>
    /// Проверяет равенство двух объектов.
    /// </summary>
    /// <param name="obj">Объект для сравнения.</param>
    /// <returns>True, если объекты равны, иначе false.</returns>
    public override bool Equals(object obj) => Equals(obj as TupleWrapperInt);

    /// <summary>
    /// Вычисляет хэш-код кортежа.
    /// </summary>
    /// <returns>Хэш-код кортежа.</returns>
    public override int GetHashCode()
    {
      unchecked
      {
        int hash = 17;
        foreach (var value in Values)
        {
          hash = hash * 23 + value.GetHashCode();
        }
        return hash;
      }
    }

    /// <summary>
    /// Возвращает строковое представление кортежа.
    /// </summary>
    /// <returns>Строка, содержащая значения кортежа, разделенные пробелами.</returns>
    public override string ToString() => string.Join(" ", Values);
  }
}