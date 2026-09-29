// XlsxWriter.cs —— 零第三方依赖的 .xlsx 写出器
//
// 只用 .NET 内置库：
//   System.IO.Compression.ZipArchive  负责 OOXML 的 zip 打包
//   System.Text.StringBuilder         负责拼 XML
// 不引用任何 System.Windows / WPF 类型，可被控制台程序或 WPF 程序共用。
//
// 生成的包结构（ECMA-376 SpreadsheetML，最小可用集）：
//   [Content_Types].xml
//   _rels/.rels
//   xl/workbook.xml
//   xl/_rels/workbook.xml.rels
//   xl/styles.xml                      （表头底纹 + 金额两位小数数字格式）
//   xl/worksheets/sheetN.xml
//
// 全部 XML 以 UTF-8（无 BOM）写出，并带 standalone="yes" 声明。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MoneyTracker
{
    /// <summary>
    /// 一个工作表。字段为公开字段，保持与调用方约定的结构一致。
    /// </summary>
    public class Sheet
    {
        /// <summary>工作表名，如 "明细"。非法字符会被自动替换。</summary>
        public string Name;

        /// <summary>表头，如 new[]{"日期","类型","金额"}。可为 null（则无表头行）。</summary>
        public string[] Headers;

        /// <summary>数据行，每个元素是各列的文本。可为 null。</summary>
        public List<string[]> Rows;

        /// <summary>这些列索引按「数字 + 两位小数」写。可为 null。</summary>
        public List<int> MoneyColumns;
    }

    /// <summary>把若干工作表写成真正的 .xlsx。</summary>
    public static class XlsxWriter
    {
        // ---- OOXML 命名空间 ----
        private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string NsContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

        private const string CtWorkbook = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
        private const string CtWorksheet = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
        private const string CtStyles = "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";

        // 单元格样式索引，必须与 StylesXml 里 cellXfs 的顺序一致
        private const int StyleDefault = 0;
        private const int StyleHeader = 1;
        private const int StyleMoney = 2;

        private const int MaxSheetNameLength = 31;
        private const double DefaultColumnWidth = 8.43;
        private const double MinColumnWidth = 8;
        private const double MaxColumnWidth = 50;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string[] NoCells = new string[0];

        /// <summary>写出多工作表 xlsx。path 的上级目录不存在时会自动创建。</summary>
        public static void Write(string path, List<Sheet> sheets)
        {
            if (path == null || path.Trim().Length == 0)
                throw new ArgumentException("路径不能为空。", "path");

            List<Sheet> list = NormalizeSheets(sheets);
            string[] names = BuildUniqueSheetNames(list);

            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (dir != null && dir.Length > 0 && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (FileStream fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // 部件顺序和 Excel 自己保存出来的文件保持一致
                AddEntry(zip, "[Content_Types].xml", BuildContentTypes(list.Count));
                AddEntry(zip, "_rels/.rels", BuildRootRels());
                AddEntry(zip, "docProps/core.xml", BuildCoreProps());
                AddEntry(zip, "docProps/app.xml", BuildAppProps());
                AddEntry(zip, "xl/workbook.xml", BuildWorkbook(names));
                AddEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(list.Count));
                AddEntry(zip, "xl/styles.xml", BuildStyles());
                AddEntry(zip, "xl/theme/theme1.xml", XlsxTheme.Theme1Xml);

                for (int i = 0; i < list.Count; i++)
                {
                    string part = "xl/worksheets/sheet" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml";
                    AddEntry(zip, part, BuildWorksheet(list[i]));
                }
            }
        }

        // ------------------------------------------------------------------
        // 包部件
        // ------------------------------------------------------------------

        private static string BuildContentTypes(int sheetCount)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"").Append(NsContentTypes).Append("\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"").Append(CtWorkbook).Append("\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"").Append(CtStyles).Append("\"/>");
            sb.Append("<Override PartName=\"/xl/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");
            sb.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            sb.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
            for (int i = 0; i < sheetCount; i++)
            {
                sb.Append("<Override PartName=\"/xl/worksheets/sheet")
                  .Append((i + 1).ToString(CultureInfo.InvariantCulture))
                  .Append(".xml\" ContentType=\"").Append(CtWorksheet).Append("\"/>");
            }
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string BuildRootRels()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"").Append(NsPkgRel).Append("\">");
            sb.Append("<Relationship Id=\"rId1\" Type=\"").Append(NsRel)
              .Append("/officeDocument\" Target=\"xl/workbook.xml\"/>");
            sb.Append("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>");
            sb.Append("<Relationship Id=\"rId3\" Type=\"").Append(NsRel)
              .Append("/extended-properties\" Target=\"docProps/app.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        /// <summary>文档属性。Excel 自己存的文件一定有这两个部件，补上更保险。</summary>
        private static string BuildCoreProps()
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\"");
            sb.Append(" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"");
            sb.Append(" xmlns:dcterms=\"http://purl.org/dc/terms/\"");
            sb.Append(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.Append("<dc:creator>\u8bb0\u8d26\u672c</dc:creator>");
            sb.Append("<cp:lastModifiedBy>\u8bb0\u8d26\u672c</cp:lastModifiedBy>");
            sb.Append("<dcterms:created xsi:type=\"dcterms:W3CDTF\">").Append(now).Append("</dcterms:created>");
            sb.Append("<dcterms:modified xsi:type=\"dcterms:W3CDTF\">").Append(now).Append("</dcterms:modified>");
            sb.Append("</cp:coreProperties>");
            return sb.ToString();
        }

        private static string BuildAppProps()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\">");
            sb.Append("<Application>\u8bb0\u8d26\u672c</Application>");
            sb.Append("<AppVersion>1.0</AppVersion>");
            sb.Append("</Properties>");
            return sb.ToString();
        }

        private static string BuildWorkbook(string[] sheetNames)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"").Append(NsMain).Append("\" xmlns:r=\"").Append(NsRel).Append("\">");
            // 这里千万别加 <fileVersion appName="xl"/>：实测真 Excel 会直接判定
            // 「文件中的部分内容有问题」并弹恢复框（OpenXML 官方校验器和 openpyxl 都查不出来）。
            // 这个元素本来就是可选的，不写没有任何损失。
            sb.Append("<workbookPr/>");
            sb.Append("<bookViews><workbookView visibility=\"visible\" minimized=\"0\" showHorizontalScroll=\"1\" showVerticalScroll=\"1\" showSheetTabs=\"1\" tabRatio=\"600\" firstSheet=\"0\" activeTab=\"0\" autoFilterDateGrouping=\"1\"/></bookViews>");
            sb.Append("<sheets>");
            for (int i = 0; i < sheetNames.Length; i++)
            {
                sb.Append("<sheet name=\"").Append(Escape(sheetNames[i]))
                  .Append("\" sheetId=\"").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                  .Append("\" state=\"visible\" r:id=\"rId").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("\"/>");
            }
            sb.Append("</sheets>");
            sb.Append("<definedNames/>");
            sb.Append("<calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/>");
            sb.Append("</workbook>");
            return sb.ToString();
        }

        private static string BuildWorkbookRels(int sheetCount)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"").Append(NsPkgRel).Append("\">");
            for (int i = 0; i < sheetCount; i++)
            {
                sb.Append("<Relationship Id=\"rId").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                  .Append("\" Type=\"").Append(NsRel).Append("/worksheet\" Target=\"worksheets/sheet")
                  .Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(".xml\"/>");
            }
            sb.Append("<Relationship Id=\"rId").Append((sheetCount + 1).ToString(CultureInfo.InvariantCulture))
              .Append("\" Type=\"").Append(NsRel).Append("/styles\" Target=\"styles.xml\"/>");
            sb.Append("<Relationship Id=\"rId").Append((sheetCount + 2).ToString(CultureInfo.InvariantCulture))
              .Append("\" Type=\"").Append(NsRel).Append("/theme\" Target=\"theme/theme1.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        /// <summary>最小样式表：0=默认，1=表头（加粗+底纹+居中），2=金额（用内置的 0.00 数字格式）。</summary>
        private static string BuildStyles()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<styleSheet xmlns=\"").Append(NsMain).Append("\">");
            sb.Append("<numFmts count=\"0\"/>");
            sb.Append("<fonts count=\"2\">");
            sb.Append("<font><sz val=\"11\"/><color rgb=\"FF1A1A1A\"/><name val=\"\u7b49\u7ebf\"/><family val=\"2\"/></font>");
            sb.Append("<font><b/><sz val=\"11\"/><color rgb=\"FF1F3864\"/><name val=\"\u7b49\u7ebf\"/><family val=\"2\"/></font>");
            sb.Append("</fonts>");
            sb.Append("<fills count=\"3\">");
            sb.Append("<fill><patternFill patternType=\"none\"/></fill>");
            sb.Append("<fill><patternFill patternType=\"gray125\"/></fill>");
            sb.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDCE6F1\"/><bgColor indexed=\"64\"/></patternFill></fill>");
            sb.Append("</fills>");
            sb.Append("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
            sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
            sb.Append("<cellXfs count=\"3\">");
            sb.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>");
            sb.Append("<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\">");
            sb.Append("<alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            sb.Append("<xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>");
            sb.Append("</cellXfs>");
            sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
            sb.Append("<dxfs count=\"0\"/>");
            sb.Append("<tableStyles count=\"0\" defaultTableStyle=\"TableStyleMedium2\" defaultPivotStyle=\"PivotStyleLight16\"/>");
            sb.Append("</styleSheet>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 工作表
        // ------------------------------------------------------------------

        private static string BuildWorksheet(Sheet sheet)
        {
            string[] headers = (sheet != null && sheet.Headers != null) ? sheet.Headers : NoCells;
            List<string[]> rows = (sheet != null && sheet.Rows != null) ? sheet.Rows : new List<string[]>();
            HashSet<int> money = BuildMoneyColumnSet(sheet);

            int columnCount = headers.Length;
            for (int i = 0; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row != null && row.Length > columnCount) columnCount = row.Length;
            }
            if (columnCount <= 0) columnCount = 1;

            int rowNumber = 1;
            int dataRowCount = 0;

            StringBuilder data = new StringBuilder();

            if (headers.Length > 0)
            {
                data.Append("<row r=\"1\">");
                for (int c = 0; c < headers.Length; c++)
                    AppendTextCell(data, c + 1, rowNumber, headers[c], StyleHeader);
                data.Append("</row>");
                rowNumber++;
                dataRowCount++;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                string[] row = rows[i] ?? NoCells;
                data.Append("<row r=\"").Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append("\">");
                for (int c = 0; c < row.Length; c++)
                {
                    if (money.Contains(c) && TryMoney(row[c], out double amount))
                        AppendNumberCell(data, c + 1, rowNumber, amount, StyleMoney);
                    else
                        AppendTextCell(data, c + 1, rowNumber, row[c], StyleDefault);
                }
                data.Append("</row>");
                rowNumber++;
                dataRowCount++;
            }

            if (dataRowCount == 0)
            {
                data.Append("<row r=\"1\"/>");
                rowNumber = 1;
            }

            string lastCell = ColumnName(columnCount - 1) + Math.Max(1, rowNumber - 1).ToString(CultureInfo.InvariantCulture);

            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"").Append(NsMain).Append("\" xmlns:r=\"").Append(NsRel).Append("\">");
            sb.Append("<sheetPr/>");
            sb.Append("<dimension ref=\"A1:").Append(lastCell).Append("\"/>");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
            if (headers.Length > 0 && rows.Count > 0)
            {
                // 冻结表头行，滚动时表头保持可见
                sb.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
                sb.Append("<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/>");
            }
            else
            {
                sb.Append("<selection activeCell=\"A1\" sqref=\"A1\"/>");
            }
            sb.Append("</sheetView></sheetViews>");
            sb.Append("<sheetFormatPr defaultRowHeight=\"14.25\"/>");
            AppendColumns(sb, columnCount, headers, rows);
            sb.Append("<sheetData>").Append(data).Append("</sheetData>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void AppendColumns(StringBuilder sb, int columnCount, string[] headers, List<string[]> rows)
        {
            bool anyWidth = false;
            double[] widths = new double[columnCount];

            for (int c = 0; c < columnCount; c++)
            {
                double w = (c < headers.Length) ? DisplayWidth(headers[c]) : 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    string[] row = rows[i];
                    if (row == null || c >= row.Length) continue;
                    double rw = DisplayWidth(row[c]);
                    if (rw > w) w = rw;
                }
                w = Math.Ceiling(w) + 2.0;
                if (w > MaxColumnWidth) w = MaxColumnWidth;
                if (w < MinColumnWidth) w = MinColumnWidth;
                widths[c] = w;
                if (w > DefaultColumnWidth) anyWidth = true;
            }

            if (!anyWidth) return;

            sb.Append("<cols>");
            for (int c = 0; c < columnCount; c++)
            {
                sb.Append("<col min=\"").Append((c + 1).ToString(CultureInfo.InvariantCulture))
                  .Append("\" max=\"").Append((c + 1).ToString(CultureInfo.InvariantCulture))
                  .Append("\" width=\"").Append(widths[c].ToString("0.##", CultureInfo.InvariantCulture))
                  .Append("\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }

        private static void AppendTextCell(StringBuilder sb, int column, int row, string text, int style)
        {
            sb.Append("<c r=\"").Append(CellRef(column, row)).Append("\"");
            if (style != StyleDefault) sb.Append(" s=\"").Append(style.ToString(CultureInfo.InvariantCulture)).Append("\"");
            sb.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(EscapeForCell(text)).Append("</t></is></c>");
        }

        private static void AppendNumberCell(StringBuilder sb, int column, int row, double value, int style)
        {
            sb.Append("<c r=\"").Append(CellRef(column, row)).Append("\"");
            if (style != StyleDefault) sb.Append(" s=\"").Append(style.ToString(CultureInfo.InvariantCulture)).Append("\"");
            sb.Append("><v>").Append(value.ToString("0.00", CultureInfo.InvariantCulture)).Append("</v></c>");
        }

        private static HashSet<int> BuildMoneyColumnSet(Sheet sheet)
        {
            HashSet<int> set = new HashSet<int>();
            if (sheet != null && sheet.MoneyColumns != null)
            {
                for (int i = 0; i < sheet.MoneyColumns.Count; i++)
                {
                    int c = sheet.MoneyColumns[i];
                    if (c >= 0) set.Add(c);
                }
            }
            return set;
        }

        /// <summary>把 "1,234.50"、"￥88"、"RMB1,234.56"、" -12 " 之类解析成数字；解析不了就返回 false（降级成文本）。</summary>
        private static bool TryMoney(string text, out double value)
        {
            value = 0.0;
            if (text == null) return false;

            string s = text.Trim();
            if (s.Length == 0) return false;

            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ',' || c == '\uff0c' || c == '\u00a0' || c == ' ' || c == '\t'
                    || c == '\uffe5' || c == '\u00a5' || c == '$' || c == '\u20ac' || c == '\u00a3'
                    || c == '\u20a9' || c == '\u20b9' || c == '\u20bd'
                    || c == '\u5143' || c == '\u5706' || c == '\u5757')
                    continue;
                sb.Append(c);
            }
            s = sb.ToString();
            if (s.Length == 0) return false;

            // 货币代码前缀，如 RMB1234.56 / USD88 / HK$88（$ 上面已经去掉了）
            int letters = 0;
            while (letters < s.Length && letters < 4 && IsAsciiLetter(s[letters])) letters++;
            if (letters > 0 && letters < s.Length)
            {
                char next = s[letters];
                if (char.IsDigit(next) || next == '-' || next == '+' || next == '.')
                    s = s.Substring(letters);
            }
            if (s.Length == 0) return false;

            double parsed;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            if (double.IsNaN(parsed) || double.IsInfinity(parsed)) return false;

            value = parsed;
            return true;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        // ------------------------------------------------------------------
        // 名称 / 引用 / 转义
        // ------------------------------------------------------------------

        private static List<Sheet> NormalizeSheets(List<Sheet> sheets)
        {
            List<Sheet> list = new List<Sheet>();
            if (sheets != null)
            {
                for (int i = 0; i < sheets.Count; i++)
                {
                    if (sheets[i] != null) list.Add(sheets[i]);
                }
            }
            if (list.Count == 0) list.Add(new Sheet());   // xlsx 至少要有一个工作表
            return list;
        }

        private static string[] BuildUniqueSheetNames(List<Sheet> sheets)
        {
            string[] names = new string[sheets.Count];
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sheets.Count; i++)
            {
                string baseName = SanitizeSheetName(sheets[i].Name, i + 1);
                string candidate = baseName;
                int n = 2;
                while (used.Contains(candidate))
                {
                    string suffix = "(" + n.ToString(CultureInfo.InvariantCulture) + ")";
                    int keep = MaxSheetNameLength - suffix.Length;
                    string head = baseName.Length > keep ? baseName.Substring(0, keep) : baseName;
                    candidate = head + suffix;
                    n++;
                }
                used.Add(candidate);
                names[i] = candidate;
            }
            return names;
        }

        private static string SanitizeSheetName(string raw, int index)
        {
            string s = raw == null ? string.Empty : raw;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x20 || c == 0x7f) continue;                       // 控制字符
                if (c == ':' || c == '\\' || c == '/' || c == '?' || c == '*'
                    || c == '[' || c == ']' || c == '\'' || c == '"')
                {
                    sb.Append('_');                                        // Excel 禁止的工作表名字符
                    continue;
                }
                sb.Append(c);
            }
            string name = sb.ToString().Trim();
            if (name.Length > MaxSheetNameLength) name = name.Substring(0, MaxSheetNameLength);
            name = name.Trim();
            if (name.Length == 0) name = "Sheet" + index.ToString(CultureInfo.InvariantCulture);
            return name;
        }

        /// <summary>单元格引用。column1Based 是 Excel 的列号（A=1）。</summary>
        private static string CellRef(int column1Based, int rowNumber)
        {
            return ColumnName(column1Based - 1) + rowNumber.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>列号转列名。zeroBasedIndex 从 0 开始（0=A，25=Z，26=AA）。</summary>
        private static string ColumnName(int zeroBasedIndex)
        {
            if (zeroBasedIndex < 0) zeroBasedIndex = 0;
            StringBuilder sb = new StringBuilder();
            int n = zeroBasedIndex;
            do
            {
                sb.Insert(0, (char)('A' + (n % 26)));
                n = n / 26 - 1;
            }
            while (n >= 0);
            return sb.ToString();
        }

        /// <summary>粗略的显示宽度：CJK / 全角按 2 个字符宽算。</summary>
        private static double DisplayWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double w = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || c == '\r') { w += 2; continue; }
                w += IsWide(c) ? 2.0 : 1.0;
            }
            return w;
        }

        private static bool IsWide(char c)
        {
            return (c >= 0x1100 && c <= 0x115F)
                || (c >= 0x2E80 && c <= 0x303E)
                || (c >= 0x3041 && c <= 0x33FF)
                || (c >= 0x3400 && c <= 0x4DBF)
                || (c >= 0x4E00 && c <= 0x9FFF)
                || (c >= 0xA000 && c <= 0xA4CF)
                || (c >= 0xAC00 && c <= 0xD7A3)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFE30 && c <= 0xFE6F)
                || (c >= 0xFF00 && c <= 0xFF60)
                || (c >= 0xFFE0 && c <= 0xFFE6);
        }

        /// <summary>XML 转义。文本节点用（" ' 也一并转，属性/文本共用一套更省心）。</summary>
        private static string Escape(string text)
        {
            return EscapeCore(text, false);
        }

        /// <summary>单元格文本：先做转义，再处理 Excel 的 _xHHHH_ 特殊编码。</summary>
        private static string EscapeForCell(string text)
        {
            return EscapeCore(text, true);
        }

        private static string EscapeCore(string text, bool forCellText)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string s = StripInvalidXmlChars(text);
            StringBuilder sb = new StringBuilder(s.Length + 16);

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); continue;
                    case '<': sb.Append("&lt;"); continue;
                    case '>': sb.Append("&gt;"); continue;
                    case '"': sb.Append("&quot;"); continue;
                    case '\'': sb.Append("&apos;"); continue;
                }

                // Excel 会把 _xHHHH_ 当成字符转义序列解析，遇到这种字面量要先把下划线保护起来
                if (forCellText && c == '_' && i + 6 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X')
                    && IsHex4(s, i + 2) && s[i + 6] == '_')
                {
                    sb.Append("_x005F_");
                    continue;
                }

                sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool IsHex4(string s, int start)
        {
            for (int i = start; i < start + 4; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>去掉 XML 1.0 不允许的字符，并把落单的代理项换成 U+FFFD，否则文件会被判定为损坏。</summary>
        private static string StripInvalidXmlChars(string text)
        {
            StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool ok;
                int len = 1;

                if (c == '\t' || c == '\n' || c == '\r')
                {
                    ok = true;
                }
                else if (char.IsHighSurrogate(c))
                {
                    ok = i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                    if (ok) len = 2;
                }
                else if (char.IsLowSurrogate(c))
                {
                    ok = false;
                }
                else
                {
                    ok = c >= 0x20 && c != 0xFFFE && c != 0xFFFF;
                }

                if (ok)
                {
                    if (sb != null) sb.Append(text, i, len);
                    if (len == 2) i++;
                }
                else
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(text.Length + 8);
                        sb.Append(text, 0, i);          // 前面都是合法字符，原样保留
                    }
                    sb.Append('\ufffd');
                }
            }
            return sb == null ? text : sb.ToString();
        }

        // ------------------------------------------------------------------
        // zip
        // ------------------------------------------------------------------

        private static void AddEntry(ZipArchive zip, string entryName, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            byte[] bytes = Utf8NoBom.GetBytes(content);
            using (Stream stream = entry.Open())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}