using System.Collections.Generic;

namespace PG.Localization
{
    // One parsed CSV table: first column is the key, the other columns are languages
    public class LocalizationTable
    {
        public class Row
        {
            public string key;
            // One value per language, null when the row has no such column
            public string[] values;

            public Row Clone() => new Row { key = key, values = (string[])values.Clone() };
        }

        private const char Delimiter = ',';

        public string name;
        public string keyHeader = "key";
        public readonly List<string> languages = new List<string>();
        public readonly List<Row> rows = new List<Row>();

        public static LocalizationTable Parse(string text, string name = null)
        {
            var table = new LocalizationTable { name = name };
            List<string[]> records = ParseCSV(text);
            if (records.Count == 0) return table;

            string[] headers = records[0];
            if (headers[0].Trim().Length > 0) table.keyHeader = headers[0].Trim();
            for (int i = 1; i < headers.Length; i++) table.languages.Add(headers[i].Trim());

            for (int r = 1; r < records.Count; r++)
            {
                string[] fields = records[r];
                string key = fields[0].Trim();
                if (key.Length == 0) continue;

                var values = new string[table.languages.Count];
                for (int i = 0; i < values.Length; i++)
                    values[i] = i + 1 < fields.Length ? fields[i + 1] : null;
                table.rows.Add(new Row { key = key, values = values });
            }
            return table;
        }

        public int LanguageIndex(string language) => languages.IndexOf(language);

        public LocalizationTable Clone()
        {
            var copy = new LocalizationTable { name = name, keyHeader = keyHeader };
            copy.languages.AddRange(languages);
            foreach (Row row in rows) copy.rows.Add(row.Clone());
            return copy;
        }

        public void AddLanguage(string language)
        {
            languages.Add(language);
            foreach (Row row in rows)
            {
                var values = new string[languages.Count];
                row.values.CopyTo(values, 0);
                row.values = values;
            }
        }

        public void RemoveLanguage(int index)
        {
            languages.RemoveAt(index);
            foreach (Row row in rows)
            {
                var values = new string[languages.Count];
                for (int i = 0, j = 0; i < row.values.Length; i++)
                    if (i != index) values[j++] = row.values[i];
                row.values = values;
            }
        }

        // A field is quoted only when plain text would be read back differently
        public static string EscapeField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            bool needsQuotes = field.IndexOfAny(new[] { Delimiter, '"', '\n', '\r' }) >= 0
                || field[0] == ' ' || field[0] == '\t' || field[field.Length - 1] == ' ' || field[field.Length - 1] == '\t';
            return needsQuotes ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
        }

        public string ToCSV(string newline = "\n")
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(EscapeField(keyHeader));
            foreach (string language in languages) sb.Append(Delimiter).Append(EscapeField(language));
            sb.Append(newline);
            foreach (Row row in rows)
            {
                sb.Append(EscapeField(row.key));
                for (int i = 0; i < languages.Count; i++)
                    sb.Append(Delimiter).Append(EscapeField(i < row.values.Length ? row.values[i] : null));
                sb.Append(newline);
            }
            return sb.ToString();
        }

        // Quoted fields follow RFC 4180 (delimiters, line breaks and doubled quotes inside).
        // In unquoted fields a delimiter followed by a space is text, not a separator.
        public static List<string[]> ParseCSV(string text)
        {
            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new System.Text.StringBuilder();
            bool inQuotes = false;
            bool quoted = false;
            int i = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;

            // Spaces around unquoted fields are dropped, quoted fields keep theirs
            void EndField()
            {
                fields.Add(quoted ? field.ToString() : field.ToString().Trim());
                field.Clear();
                quoted = false;
            }

            void EndRow()
            {
                EndField();
                // Skip blank lines
                if (!(fields.Count == 1 && fields[0].Length == 0)) rows.Add(fields.ToArray());
                fields.Clear();
            }

            for (; i < text.Length; i++)
            {
                char c = text[i];
                bool hasNext = i + 1 < text.Length;
                if (inQuotes)
                {
                    if (c != '"')
                    {
                        field.Append(c);
                    }
                    else if (hasNext && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (!hasNext || text[i + 1] == Delimiter || text[i + 1] == '\n' || text[i + 1] == '\r')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        // A lone quote inside quoted text stays as a character
                        field.Append(c);
                    }
                }
                else if (c == '"' && field.ToString().Trim().Length == 0)
                {
                    inQuotes = true;
                    quoted = true;
                    field.Clear();
                }
                else if (c == Delimiter && !(hasNext && text[i + 1] == ' '))
                {
                    EndField();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && hasNext && text[i + 1] == '\n') i++;
                    EndRow();
                }
                else
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || fields.Count > 0) EndRow();
            return rows;
        }
    }
}
