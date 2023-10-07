using CsvHelper;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace OgameSkaner.Utils
{
    public class CsvExporter
    {
        public CsvExporter()
        {

        }

        public void ExportPlayersToCsv<TItem>(IEnumerable<TItem> items)
        {
            var sfd = new SaveFileDialog();
            DialogResult result = sfd.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(sfd.FileName))
            {
                var writer = new StreamWriter($"{sfd.FileName}.csv");
                var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
               
                csv.WriteRecords(items);
            }
        }
    }
}
